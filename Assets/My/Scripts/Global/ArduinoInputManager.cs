using UnityEngine;
using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

/// <summary>
/// 아두이노와 시리얼 통신을 담당하는 매니저
/// - 버튼: 아두이노가 보내는 "Button n Pressed" 한 줄을 받아서, 한 번만 소비되는 플래그로 제공
/// - LED: Unity에서 "LEDn ON/OFF" 문자열을 전송하여 릴레이(버튼 LED) 제어
/// 큐를 제거하고, 스레드-세이프한 비트마스크 방식으로 구현
/// </summary>
public class ArduinoInputManager : MonoBehaviour
{
    public static ArduinoInputManager Instance;
    // 과거 코드 호환용
    public static ArduinoInputManager instance => Instance;

    public enum ButtonId { Button1 = 1, Button2 = 2, Button3 = 3 }

    // Settings.json에서 가져올 포트/보레이트
    private string _portName;
    private int _baudRate;

    private SerialPort _serialPort;
    private Thread _readThread;
    private volatile bool _running;

    // 버튼 눌림을 한 번만 전달하기 위한 비트마스크 플래그
    // bit0: Button1, bit1: Button2, bit2: Button3
    private volatile int _pressedBits; // 멀티스레드 환경에서 사용

    // 앱 시작 이후 경과 시간(밀리초) 제공
    private static Stopwatch _clock;
    public static long NowMs => _clock?.ElapsedMilliseconds ?? 0;

    private Settings _jsonSettings;

    // 내부 상수: 비트마스크
    private const int BIT_B1 = 1 << 0;
    private const int BIT_B2 = 1 << 1;
    private const int BIT_B3 = 1 << 2;

    private void Awake()
    {
        if (_clock == null) _clock = Stopwatch.StartNew();

        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(gameObject); return; }

        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        try
        {
            _jsonSettings ??= JsonLoader.Instance.settings;
            _portName = _jsonSettings.portName;
            _baudRate = _jsonSettings.baudRate;

            _serialPort = new SerialPort(_portName, _baudRate)
            {
                ReadTimeout = 100,
                NewLine = "\n"
            };
            _serialPort.Open();

            _running = true;
            _readThread = new Thread(ReadSerial) { IsBackground = true };
            _readThread.Start();

            Debug.Log($"[ArduinoInputManager] Opened {_portName} @ {_baudRate}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ArduinoInputManager] 포트 열기 실패: {e.Message}");
        }
    }

    private void ReadSerial()
    {
        while (_running && _serialPort != null && _serialPort.IsOpen)
        {
            try
            {
                string line = _serialPort.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;

                string s = line.Trim();

                // 아두이노 포맷: "Button 1 Pressed" 등
                if (s.IndexOf("Button 1 Pressed", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    SetPressedBit(BIT_B1);
                }
                else if (s.IndexOf("Button 2 Pressed", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    SetPressedBit(BIT_B2);
                }
                else if (s.IndexOf("Button 3 Pressed", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    SetPressedBit(BIT_B3);
                }
                // 아두이노가 "OK" 같은 응답을 보내는 경우가 있어도 무시해도 됨
            }
            catch (TimeoutException) { }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArduinoInputManager] 수신 오류: {e.Message}");
                Thread.Sleep(100);
            }
        }
    }

    // 스레드에서 눌림 비트를 세팅
    private void SetPressedBit(int bit)
    {
        // 원자적 OR 연산 대체: 루프 CAS
        while (true)
        {
            int original = _pressedBits;
            int updated = original | bit;
            if (System.Threading.Interlocked.CompareExchange(ref _pressedBits, updated, original) == original)
                break;
        }
    }

    private void OnApplicationQuit()
    {
        _running = false;

        try
        {
            if (_readThread != null && _readThread.IsAlive)
                _readThread.Join(200);
        }
        catch { }

        try
        {
            if (_serialPort != null && _serialPort.IsOpen)
                _serialPort.Close();
        }
        catch { }
    }

    // 한 번만 소비하는 입력: 누적된 눌림 중 하나를 반환하고, 해당 비트를 클리어
    public bool TryConsumeAnyPress(out ButtonId id)
    {
        id = default;

        // 전체 비트를 원자적으로 읽고 나서 우선순위대로 하나를 소비
        while (true)
        {
            int bits = _pressedBits;
            if (bits == 0) return false;

            int consumeBit;
            if ((bits & BIT_B1) != 0) { id = ButtonId.Button1; consumeBit = BIT_B1; }
            else if ((bits & BIT_B2) != 0) { id = ButtonId.Button2; consumeBit = BIT_B2; }
            else { id = ButtonId.Button3; consumeBit = BIT_B3; }

            int newBits = bits & ~consumeBit;
            if (System.Threading.Interlocked.CompareExchange(ref _pressedBits, newBits, bits) == bits)
                return true;
        }
    }

    // 특정 버튼만 소비하고 싶을 때 사용
    public bool TryConsumePress(ButtonId target)
    {
        int bit = target == ButtonId.Button1 ? BIT_B1
                : target == ButtonId.Button2 ? BIT_B2
                : BIT_B3;

        while (true)
        {
            int bits = _pressedBits;
            if ((bits & bit) == 0) return false;

            int newBits = bits & ~bit;
            if (System.Threading.Interlocked.CompareExchange(ref _pressedBits, newBits, bits) == bits)
                return true;
        }
    }

    // 기존에 쓰던 FlushAll 대체: 모든 눌림 플래그 제거
    public int FlushAll()
    {
        // 원자적으로 비트를 0으로
        int bits = System.Threading.Interlocked.Exchange(ref _pressedBits, 0);
        // 몇 개를 지웠는지 대략 계산
        int count = 0;
        if ((bits & BIT_B1) != 0) count++;
        if ((bits & BIT_B2) != 0) count++;
        if ((bits & BIT_B3) != 0) count++;
        return count;
    }

    // 필요 시 아두이노에 딜레이 값 전송(아두이노가 이 값 처리할 때만 의미 있음)
    public void SendButtonDelay(int ms)
    {
        if (_serialPort != null && _serialPort.IsOpen)
            _serialPort.WriteLine(ms.ToString());
        else
            Debug.LogError("[ArduinoInputManager] SendButtonDelay: 포트가 닫혀 있음");
    }

    // LED 제어: "LEDn ON/OFF" 전송
    public void SetLed(int ledIndex, bool on)
    {
        if (_serialPort == null || !_serialPort.IsOpen) return;
        try
        {
            _serialPort.WriteLine($"LED{ledIndex} {(on ? "ON" : "OFF")}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ArduinoInputManager] SetLed write error: {e.Message}");
        }
    }

    public void SetLedAll(bool on)
    {
        SetLed(1, on);
        SetLed(2, on);
        SetLed(3, on);
        // 필요 시 4번도 사용
        // SetLed(4, on);
    }
}
