using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 아두이노 NeoPixel 스트립 제어 유틸리티
/// </summary>
public static class LedStrip
{
    public static void Pixel(int index, int r, int g, int b)
    {
        ArduinoInputManager inst = ArduinoInputManager.Instance;
        if (!inst)
        {
            Debug.LogError("ArduinoInputManager.Instance is null.");
            return;
        }

        inst.Send($"PIX {index} {r} {g} {b}");
    }

    public static void Range(int start, int end, int r, int g, int b)
    {
        ArduinoInputManager inst = ArduinoInputManager.Instance;
        if (!inst)
        {
            Debug.LogError("ArduinoInputManager.Instance is null.");
            return;
        }

        inst.Send($"RANGE {start} {end} {r} {g} {b}");
    }

    public static void Fill(int r, int g, int b)
    {
        ArduinoInputManager inst = ArduinoInputManager.Instance;
        if (!inst)
        {
            Debug.LogError("ArduinoInputManager.Instance is null.");
            return;
        }

        inst.Send($"FILL {r} {g} {b}");
    }

    public static void Clear()
    {
        ArduinoInputManager inst = ArduinoInputManager.Instance;
        if (!inst)
        {
            Debug.LogError("ArduinoInputManager.Instance is null.");
            return;
        }

        inst.Send("CLEAR");
    }

    public static void Bright(int brightness)
    {
        ArduinoInputManager inst = ArduinoInputManager.Instance;
        if (!inst)
        {
            Debug.LogError("ArduinoInputManager.Instance is null.");
            return;
        }

        inst.Send($"BRIGHT {brightness}");
    }
}

/// <summary>
/// 아두이노와 시리얼 통신을 담당하는 매니저
/// - 버튼: 아두이노가 보내는 "Button n Pressed" 한 줄을 받아서, 한 번만 소비되는 플래그로 제공
/// - LED: Unity에서 "LEDn ON/OFF" 문자열을 전송하여 릴레이(버튼 LED) 제어
/// 큐를 제거하고, 스레드-세이프한 비트마스크 방식으로 구현
/// </summary>
public class ArduinoInputManager : MonoBehaviour
{
    public static ArduinoInputManager Instance;

    public enum ButtonId
    {
        None = 0,
        Button1 = 1,
        Button2 = 2,
        Button3 = 3
    }

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
    
    // 스로틀에서 받아온 마지막 값을 저장함
    private volatile int _lastThrottleValue;
    public int LastThrottleValue
    {
        get => _lastThrottleValue;
        set => _lastThrottleValue = value;
    }
    
    private volatile int _throttleVersion;          // 값 갱신 버전
    public int ThrottleVersion => _throttleVersion; // 외부에서 읽기 전용
    public long LastThrottleAtMs { get; private set; } // 마지막 수신 시각(ms)

    public bool arduinoReady;

    public bool ArduinoReady
    {
        get => arduinoReady;
        private set => arduinoReady = value;
    }

    public event Action<string> LineReceived;
    
    private void Awake()
    {
        if (_clock == null) _clock = Stopwatch.StartNew();

        if (Instance == null) Instance = this;
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        StartAsync().Forget();
    }
    
    private async UniTaskVoid StartAsync()
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

            // 아두이노 리셋 안정화 대기
            await UniTask.Delay(2000);
            _serialPort.DiscardInBuffer();

            _running = true;
            _readThread = new Thread(ReadSerial) { IsBackground = true };
            _readThread.Start();

            Debug.Log($"[Arduino] 포트 오픈 {_portName} @ {_baudRate}");
            ArduinoReady = true;
            
            // ===== 스로틀 OFF 후 ACK 대기 =====
            await SendAndAwaitAckAsync("THROTTLE OFF", "ACK THROTTLE OFF", 1000);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Arduino] 포트 열기 실패: {e.Message}");
        }
    }
    
    private async UniTask<bool> SendAndAwaitAckAsync(string command, string expectedAck, int timeoutMs)
    {
        if (_serialPort == null || !_serialPort.IsOpen)
        {
            Debug.LogWarning("[Arduino] 포트가 열려있지 않음.");
            return false;
        }

        UniTaskCompletionSource<bool> tcs = new UniTaskCompletionSource<bool>();

        void OnLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (line.Trim().Equals(expectedAck, StringComparison.OrdinalIgnoreCase))
            {
                tcs.TrySetResult(true);
            }
        }

        LineReceived += OnLine;

        try
        {
            Send(command); // 명령 전송
            (bool hasResultLeft, bool result) = await UniTask.WhenAny(tcs.Task, UniTask.Delay(timeoutMs));

            if (hasResultLeft)
            {
                Debug.Log($"[Arduino] {expectedAck} 수신됨.");
                return true;
            }

            Debug.LogWarning($"[Arduino] {expectedAck} 수신 실패(타임아웃).");
            return false;
        }
        finally
        {
            LineReceived -= OnLine;
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
                Debug.Log("[Arduino]>> "+ s);

                // 먼저 원문 한 줄을 브로드캐스트
                try { LineReceived?.Invoke(s); } catch { /* 구독자 예외 방지 */ }
                
                if (s.StartsWith("THROTTLE", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = s.Split(' ');
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int throttle))
                    {
                        LastThrottleValue = throttle;
                        LastThrottleAtMs = NowMs;
                        Interlocked.Increment(ref _throttleVersion);
                    }
                    continue;
                }

                if (s.IndexOf("BTN 1", StringComparison.OrdinalIgnoreCase) >= 0)
                {   
                    SoundManager.Instance?.PlayButton();
                    SetPressedBit(BIT_B1);
                }
                else if (s.IndexOf("BTN 2", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    SoundManager.Instance?.PlayButton();
                    SetPressedBit(BIT_B2);
                }
                else if (s.IndexOf("BTN 3", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    SoundManager.Instance?.PlayButton();
                    SetPressedBit(BIT_B3);
                }
            }
            catch (TimeoutException) { }
            catch (Exception e)
            {
                Debug.LogWarning($"[Arduino] 수신 오류: {e.Message}");
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
            if (Interlocked.CompareExchange(ref _pressedBits, updated, original) == original)
                break;
        }
    }

    private void OnApplicationQuit()
    {
        _running = false;
        
        if (_readThread != null && _readThread.IsAlive)
            _readThread.Join(200);
        
        if (_serialPort != null && _serialPort.IsOpen)
            _serialPort.Close();
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
            if ((bits & BIT_B1) != 0)
            {
                id = ButtonId.Button1;
                consumeBit = BIT_B1;
            }
            else if ((bits & BIT_B2) != 0)
            {
                id = ButtonId.Button2;
                consumeBit = BIT_B2;
            }
            else
            {
                id = ButtonId.Button3;
                consumeBit = BIT_B3;
            }

            int newBits = bits & ~consumeBit;
            if (Interlocked.CompareExchange(ref _pressedBits, newBits, bits) == bits)
                return true;
        }
    }

    // 모든 눌림 플래그 제거
    public int FlushAll()
    {
        // 원자적으로 비트를 0으로
        int bits = Interlocked.Exchange(ref _pressedBits, 0);
        // 몇 개를 지웠는지 대략 계산
        int count = 0;
        if ((bits & BIT_B1) != 0) count++;
        if ((bits & BIT_B2) != 0) count++;
        if ((bits & BIT_B3) != 0) count++;
        return count;
    }

    // 필요 시 아두이노에 딜레이 값 전송
    public void SendButtonDelay(int ms)
    {
        if (_serialPort != null && _serialPort.IsOpen)
            _serialPort.WriteLine(ms.ToString());
        else
            Debug.LogError("[Arduino] SendButtonDelay: 포트가 닫혀 있음");
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
            Debug.LogError($"[Arduino] SetLed write error: {e.Message}");
        }
    }

    public void SetLedAll(bool on)
    {
        SetLed(1, on);
        SetLed(2, on);
        SetLed(3, on);
    }

    public void Send(string line)
    {
        try
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.WriteLine(line);
            }
            else
            {
                Debug.LogError("[Arduino] Send failed: port not open.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[Arduino] Send exception: " + e.Message);
        }
    }
}