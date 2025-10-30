using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [SerializeField] private Reporter reporter;

    public event Action onReset;

    [SerializeField] private int ackTimeoutMs = 1000;

    // 종료 시퀀스 재진입 방지
    private bool _isQuitting = false;

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

        // 다중 디스플레이 활성화
        for (int i = 0; i < Display.displays.Length; i++)
        {
            if (i == 0) continue;
            Display.displays[i].Activate();
        }

        // 빌드/에디터 공통: 종료 의사 발생을 가로채서 안전 종료 실행
        Application.wantsToQuit += HandleWantsToQuit;

#if UNITY_EDITOR
        // 에디터에서 플레이모드 종료 시 아두이노 끄기 시도
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
        Application.wantsToQuit -= HandleWantsToQuit;

        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        Cursor.visible = false;
    }

    private void Update()
    {
        if (reporter && Input.GetKeyDown(KeyCode.D))
        {
            reporter.showGameManagerControl = !reporter.showGameManagerControl;
            if (reporter.show) reporter.show = false;
        }
        else if (Input.GetKeyDown(KeyCode.M))
        {
            Cursor.visible = !Cursor.visible;
        }
    }

#if UNITY_EDITOR
    // 에디터 플레이모드 종료 콜백 -> 비동기 호출을 fire-and-forget으로 실행
    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            TryTurnOffLocally();
            TurnOffArduinosAsync().Forget();
        }
    }
#endif

#if !UNITY_EDITOR
    // 빌드에서 OS가 강제 종료 신호를 보낼 때 마지막 백업 훅
    private void OnApplicationQuit()
    {
        // 즉시 로컬 끄기(시각상 잔류 최소화)
        TryTurnOffLocally();

        // 비동기 ACK 대기 시도(fire-and-forget)
        TurnOffArduinosAsync().Forget();

        // 너무 짧게라도 송신 버퍼 비우는 데 도움
        System.Threading.Thread.Sleep(200);
    }
#endif

    // 종료 의사 가로채기 -> false를 반환해 즉시 종료를 막고, 안전 종료 코루틴 실행
    private bool HandleWantsToQuit()
    {
        if (_isQuitting) return true; // 이미 종료 절차 중이면 그대로 종료 허용

        _isQuitting = true;

        // 즉시 로컬 정리(LED off, 픽셀 클리어, 스로틀 OFF 송신)
        TryTurnOffLocally();

        // 안전 종료 절차 비동기 실행 후 실제 종료
        QuitFlowAsync().Forget();

        // 지금은 종료하지 말고 기다리자
        return false;
    }

    // 함수: 안전 종료 전체 플로우(ACK 대기 -> 종료)
    private async UniTaskVoid QuitFlowAsync()
    {
        // OnDestroy로 파괴될 수 있으므로 토큰 결합
        CancellationToken linked = this.GetCancellationTokenOnDestroy();

        // ACK 대기 (타임아웃 내)
        await TurnOffArduinosAsync(linked);

        // 아주 짧게 양보 -> 시리얼 송신 버퍼가 비워질 시간
        await UniTask.Delay(200, cancellationToken: linked);

        // 실제 종료
        Application.Quit();
    }

    // 메서드: 외부 리셋 트리거
    public void Reset()
    {
        onReset?.Invoke();
    }

    // 공통 "명령 전송 -> 특정 ACK 대기"
    private async UniTask<bool> SendAndAwaitAckAsync(string command, string expectedAck, int timeoutMs, CancellationToken ct)
    {
        ArduinoInputManager inst = ArduinoInputManager.Instance;
        if (!inst)
        {
            Debug.LogError("[GameManager] ArduinoInputManager.Instance is null");
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

        inst.LineReceived += OnLine;
        try
        {
            inst.Send(command);

            UniTask<bool> waitTask = tcs.Task;
            UniTask timeoutTask = UniTask.Delay(timeoutMs, cancellationToken: ct);

            var (hasResultLeft, result) = await UniTask.WhenAny(waitTask, timeoutTask);

            if (hasResultLeft)
            {
                return await waitTask;
            }
            return false; // 타임아웃
        }
        finally
        {
            inst.LineReceived -= OnLine;
        }
    }

    // 아두이노 LED/네오픽셀/스로틀 정리(로컬 즉시)
    private void TryTurnOffLocally()
    {
        try
        {
            ArduinoInputManager.Instance?.SetLedAll(false);
            LedStrip.Clear();
            ArduinoInputManager.Instance?.Send("THROTTLE OFF");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GameManager] Local Arduino off failed: {e.Message}");
        }
    }

    // 아두이노 종료 절차(ACK 대기 포함)
    public async UniTask TurnOffArduinosAsync(CancellationToken ct = default)
    {
        try
        {
            ArduinoInputManager.Instance?.SetLedAll(false);
            LedStrip.Clear();

            CancellationToken linked = ct.CanBeCanceled ? ct : this.GetCancellationTokenOnDestroy();

            bool ok = await SendAndAwaitAckAsync(
                command: "THROTTLE OFF",
                expectedAck: "ACK THROTTLE OFF",
                timeoutMs: Mathf.Max(200, ackTimeoutMs),
                ct: linked
            );

            if (ok)
            {
                Debug.Log("[GameManager] 아두이노 정상 종료");
            }
            else
            {
                Debug.LogWarning("[GameManager] 아두이노 ACK 수신 실패");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GameManager] 아두이노 종료 실패: {e.Message}");
        }
    }
}
