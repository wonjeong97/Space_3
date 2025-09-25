// Thread.Sleep
using System;
using System.Threading;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [SerializeField] private Reporter reporter;

    public event Action onReset;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        for (int i = 0; i < Display.displays.Length; i++)
        {
            if (i == 0) continue;
            Display.displays[i].Activate(); // Display 2, 3 activate
        }

#if UNITY_EDITOR
        // 에디터 플레이모드 종료 시에도 LED 끄기
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
    }

    private void Start()
    {
        Cursor.visible = false;
    }

    private void Update()
    {
        if (reporter != null && Input.GetKeyDown(KeyCode.D))
        {
            reporter.showGameManagerControl = !reporter.showGameManagerControl;
            if (reporter.show) reporter.show = false;
        }
        else if (Input.GetKeyDown(KeyCode.M))
        {
            Cursor.visible = !Cursor.visible;
        }
    }

    private void OnApplicationQuit()
    {
        TurnOffAllLeds();
    }

#if UNITY_EDITOR
    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            TurnOffAllLeds();
        }
    }
#endif

    private void OnDestroy()
    {
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
        if (Instance == this) Instance = null;
    }

    public void Reset()
    {
        onReset?.Invoke();
    }

    private void TurnOffAllLeds()
    {
        try
        {
            ArduinoInputManager.Instance?.SetLedAll(false);
            Thread.Sleep(100); // 전송 마무리 대기
            Debug.Log("[GameManager] All LEDs turned off.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GameManager] LED OFF failed: {e.Message}");
        }
    }
}
