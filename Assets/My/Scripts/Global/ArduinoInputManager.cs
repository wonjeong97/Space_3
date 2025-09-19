using UnityEngine;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using Debug = UnityEngine.Debug;

public class ArduinoInputManager : MonoBehaviour
{
    public static ArduinoInputManager instance;
    
    public enum ButtonId { Button1 = 1, Button2 = 2, Button3 = 3 }
    
    private string _portName; 
    private int _baudRate;   

    private SerialPort _serialPort;
    private Thread _readThread;
    private volatile bool _running;

    private readonly ConcurrentQueue<(ButtonId id, long ms)> _pressQueue
        = new ConcurrentQueue<(ButtonId, long)>();

    // Stopwatch는 애플리케이션 시작 이후 경과 시간을 제공
    private static Stopwatch _clock;

    private Settings _jsonSettings;

    private void Awake()
    {   
        if (_clock == null) _clock = Stopwatch.StartNew();
        if (instance == null) instance = this;
        else if  (instance != this) Destroy(gameObject);
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {   
        try
        {
            _jsonSettings ??= JsonLoader.Instance.settings;
            _portName = _jsonSettings.portName;
            _baudRate = _jsonSettings.baudRate;
            
            _serialPort = new SerialPort(_portName, _baudRate);
            _serialPort.ReadTimeout = 100;  
            _serialPort.Open(); 
            
            _running = true;
            _readThread = new Thread(ReadSerial); 
            _readThread.Start();

            Debug.Log($"[ArduinoInputManager] 포트: {_portName} @ {_baudRate}");
        }
        catch (Exception e)
        {
            Debug.LogError("[ArduinoInputManager] 포트 열기 실패: " + e.Message);
        }
    }
    
    private void ReadSerial()
    {
        while (_running && _serialPort != null && _serialPort.IsOpen)
        {
            try
            {
                string line = _serialPort.ReadLine().Trim();
                long nowMs = _clock?.ElapsedMilliseconds ?? 0;

                if (line.IndexOf("Button 1 Pressed", StringComparison.OrdinalIgnoreCase) >= 0)
                    _pressQueue.Enqueue((ButtonId.Button1, nowMs));
                else if (line.IndexOf("Button 2 Pressed", StringComparison.OrdinalIgnoreCase) >= 0)
                    _pressQueue.Enqueue((ButtonId.Button2, nowMs));
                else if (line.IndexOf("Button 3 Pressed", StringComparison.OrdinalIgnoreCase) >= 0)
                    _pressQueue.Enqueue((ButtonId.Button3, nowMs));
            }
            catch (TimeoutException) { }
            catch (Exception e)
            {
                Debug.LogWarning("[ArduinoInputManager] 수신 오류: " + e.Message);
                _running = false;
            }
        }
    }

    public static long NowMs => _clock?.ElapsedMilliseconds ?? 0;

    private void OnApplicationQuit()
    {
        _running = false;
        try
        {
            if (_readThread != null && _readThread.IsAlive) _readThread.Join();
        }
        catch { }

        try
        {
            if (_serialPort != null && _serialPort.IsOpen) _serialPort.Close();
        }
        catch { }
    }
    
    public bool TryConsumeAnyPress(out ButtonId id)
    {
        if (_pressQueue.TryDequeue(out (ButtonId id, long t) e)) { id = e.id; return true; }
        id = default; return false;
    }
    
    public int FlushAll()
    {
        int n = 0;
        while (_pressQueue.TryDequeue(out _)) n++;
        return n;
    }
    
    public bool TryConsumePressNewerThan(long since, out ButtonId id)
    {
        while (_pressQueue.TryDequeue(out (ButtonId id, long t) e))
        {
            if (e.t >= since) { id = e.id; return true; }
        }
        id = default; return false;
    }
    
    public bool HasPendingPress => !_pressQueue.IsEmpty;
}
