using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// CountController의 PlusSeconds를 감시해 NuriAnimEvent 이벤트를 자동 호출.
/// T+ 2:05 → DropStage1()
/// T+ 3:56 → SeparateFairing()
/// T+ 4:30 → DropStage2()
/// T+ 12:14 → Stage3Off()
/// </summary>
public class NuriTriggerController : MonoBehaviour
{
    [Header("Refs")] [SerializeField] private CountController count;
    [SerializeField] private NuriAnimEvent nuri;

    [Header("Trigger Times (T+ in seconds)")]
    [SerializeField] private float tDropStage1 = 2f * 60f + 5f; // 2:05
    [SerializeField] private float tSeparateFairing = 3f * 60f + 56f; // 3:56
    [SerializeField] private float tDropStage2 = 4f * 60f + 30f; // 4:30
    [SerializeField] private float tStage3Off = 12f * 60f + 14f; // 12:14
    [SerializeField] private float tSeparateSatellite = 13f * 60f + 5f; // 13:05
    [SerializeField] private float tCallNextScene = 15f * 60f; // 15:00

    [Header("Polling Interval (sec)")]
    [SerializeField] private float pollInterval = 0.05f;
    
    private bool _firedDrop1;
    private bool _firedFairing;
    private bool _firedDrop2;
    private bool _firedStage3Off;
    private bool _firedSatellite;
    private bool _firedNextScene;

    private void Reset()
    {
        count = FindObjectOfType<CountController>();
        nuri = FindObjectOfType<NuriAnimEvent>();
    }

    private void OnEnable()
    {
        RunTriggerLoop().Forget();
    }

    /// <summary> CountController의 T+ 시간을 감시해 지정 시각을 넘길 때 각 이벤트를 1회 호출한다. </summary>
    private async UniTaskVoid RunTriggerLoop()
    {
        CancellationToken token = this.GetCancellationTokenOnDestroy();

        if (count == null)
        {
            Debug.LogError("[NuriTriggerController] CountController reference missing");
            return;
        }

        if (nuri == null)
        {
            Debug.LogError("[NuriTriggerController] NuriAnimEvent reference missing");
            return;
        }

        _firedDrop1 = _firedFairing = _firedDrop2 = _firedStage3Off = false;
        bool firedLaunchSound = false;

        while (!token.IsCancellationRequested)
        {
            if (!count.IsCountingDown)
            {
                float t = count.TPlusSeconds;

                if (!firedLaunchSound && t >= 0f)
                {
                    firedLaunchSound = true;
                    SoundManager.Instance?.PlayByKey("발사");
                    Debug.Log("[NuriTriggerController] RunTriggerLoop-> 발사 사운드 재생");

                    await UniTask.Delay(6000, cancellationToken: token);

                    SoundManager.Instance?.CrossFadeByKey("1단 발사", loop: true);
                }

                // T+ 2:05 DropStage1
                if (!_firedDrop1 && t >= tDropStage1 && LaunchManager.Instance != null)
                {
                    // ===== 왼쪽 버튼 대기 =====
                    LaunchManager.Instance.SetButtonOn("Left");
                    count.BeginExternalHold(); // T+ 시간 멈춤
                    LaunchManager.Instance.SetGuideText("1단 분리 버튼을 누르세요.");
                    ArduinoInputManager.Instance?.SetLed(1, true);
                    LaunchManager.Instance.PublicStartBlinkGreen(500, 160);
                    LaunchManager.Instance.ResumeInactivityTimer();

                    await LaunchManager.Instance.WaitForLeftButtonAsync(token);

                    LaunchManager.Instance.SetGuideText("");
                    count.EndExternalHold();
                    LaunchManager.Instance.SetButtonOff("Left");
                    LaunchManager.Instance.PauseInactivityTimer();

                    ArduinoInputManager.Instance?.SetLed(1, false);
                    LaunchManager.Instance.PublicStopLedEffects();
                    LedStrip.Range(0, 9, 255, 0, 0);
                    // ========================

                    _firedDrop1 = true;
                    FocusObject.Pose pose = new FocusObject.Pose(new Vector3(0f, 3100f, 0f), Quaternion.identity);
                    //focus.FocusTo(pose, 2f);
                    nuri.DropStage1().Forget();

                    LaunchManager.Instance.FocusImage3ThenPingPong4(); // 시퀀스 이미지 핑퐁
                    LaunchManager.Instance.FadeInStagePublicAsync(3).Forget(); // 스테이지 이미지 페이드 인
                    LaunchManager.Instance.FadeOutSubRocketStage1Async().Forget(); // 서브모니터 stage1 이미지 페이드 아웃
                }

                // T+ 3:56 SeparateFairing
                if (!_firedFairing && t >= tSeparateFairing && LaunchManager.Instance != null)
                {
                    // ===== 오른쪽 버튼 대기 =====
                    LaunchManager.Instance.SetButtonOn("Right");
                    count.BeginExternalHold();
                    LaunchManager.Instance.SetGuideText("페어링 분리 버튼을 누르세요.");
                    LaunchManager.Instance.ResumeInactivityTimer();

                    ArduinoInputManager.Instance?.SetLed(3, true);
                    LaunchManager.Instance.PublicStartBlinkGreen(500, 160);

                    await LaunchManager.Instance.WaitForRightButtonAsync(token);

                    LaunchManager.Instance.SetGuideText("");
                    count.EndExternalHold();
                    LaunchManager.Instance.SetButtonOff("Right");
                    LaunchManager.Instance.PauseInactivityTimer();

                    ArduinoInputManager.Instance?.SetLed(3, false);
                    LaunchManager.Instance.PublicStopLedEffects();
                    LedStrip.Range(0, 9, 255, 0, 0);
                    // =========================

                    _firedFairing = true;
                    nuri.SeparateFairing().Forget();

                    LaunchManager.Instance.FadeInStagePublicAsync(4).Forget();
                    LaunchManager.Instance.FadeOutSubRocketPairingAsync().Forget();
                }

                // T+ 4:30 DropStage2
                if (!_firedDrop2 && t >= tDropStage2 && LaunchManager.Instance != null)
                {
                    // ===== 가운데 버튼 대기 =====
                    LaunchManager.Instance.SetButtonOn("Middle");
                    count.BeginExternalHold();
                    LaunchManager.Instance.SetGuideText("2단 분리 버튼을 누르세요");
                    LaunchManager.Instance.ResumeInactivityTimer();

                    ArduinoInputManager.Instance?.SetLed(2, true);
                    LaunchManager.Instance.PublicStartBlinkGreen(500, 160);

                    await LaunchManager.Instance.WaitForMiddleButtonAsync(token);

                    LaunchManager.Instance.SetGuideText("");
                    count.EndExternalHold();
                    LaunchManager.Instance.SetButtonOff("Middle");
                    LaunchManager.Instance.PauseInactivityTimer();

                    ArduinoInputManager.Instance?.SetLed(2, false);
                    LaunchManager.Instance.PublicStopLedEffects();
                    LedStrip.Range(0, 9, 255, 0, 0);
                    // =========================

                    _firedDrop2 = true;
                    FocusObject.Pose pose = new(new Vector3(0f, 3800f, 0f), Quaternion.identity);
                    //focus.FocusTo(pose, 2f);
                    nuri.DropStage2().Forget();

                    LaunchManager.Instance.FadeInStagePublicAsync(5).Forget();
                    LaunchManager.Instance.FadeOutSubRocketStage2Async().Forget();
                }

                // T+ 12:14 Stage3Off
                if (!_firedStage3Off && t >= tStage3Off)
                {
                    _firedStage3Off = true;
                    FocusObject.Pose pose = new(new Vector3(-46f, 4474f, 0f), Quaternion.Euler(-75f, -90f, 90f));
                    //focus.FocusTo(pose, 2f);
                    nuri.Stage3Off().Forget();

                    LaunchManager.Instance?.FadeInStagePublicAsync(6).Forget();
                }

                // T+ 13:04 SeparateSatellite
                if (!_firedSatellite && t >= tSeparateSatellite)
                {
                    _firedSatellite = true;

                    LaunchManager.Instance?.FocusImage4ThenPingPong5();
                    LaunchManager.Instance?.FadeInStagePublicAsync(5).Forget();
                }

                // T+ 15:00 Call Next Scene
                if (!_firedNextScene && t >= tCallNextScene)
                {
                    nuri.CallNextScene().Forget();
                }
            }

            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(pollInterval), cancellationToken: token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}