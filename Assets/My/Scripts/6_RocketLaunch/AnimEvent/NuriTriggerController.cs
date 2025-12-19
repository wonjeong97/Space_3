using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// CountController의 PlusSeconds를 감시해 NuriAnimEvent 이벤트를 자동 호출.
/// T+ 2:05 -> DropStage1()
/// T+ 3:56 -> SeparateFairing()
/// T+ 4:30 -> DropStage2()
/// T+ 12:14 -> Stage3Off()
/// </summary>
public class NuriTriggerController : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CountController count;
    [SerializeField] private NuriAnimEvent nuri;

    [Header("Trigger Times (T+ in seconds)")]
    [SerializeField] private float tSkyboxChange = 1f * 60f + 30f;
    [SerializeField] private float tDropStage1 = 2f * 60f + 5f;           // 2:05
    [SerializeField] private float tSeparateFairing = 3f * 60f + 56f;     // 3:56
    [SerializeField] private float tDropStage2 = 4f * 60f + 30f;          // 4:30
    [SerializeField] private float tDeltaAccelStart = 5f * 60f;           // 5:00
    [SerializeField] private float tDeltaAccelStop = 11f * 60f + 45;      // 11:45
    [SerializeField] private float tStage3Off = 12f * 60f + 14f;          // 12:14
    [SerializeField] private float tSeparateSatellite = 13f * 60f + 5f;   // 13:05

    [Header("Polling Interval (sec)")]
    [SerializeField] private float pollInterval = 0.05f;
    
    private bool _firedSkyboxChange;
    private bool _firedDrop1;
    private bool _firedFairing;
    private bool _firedDrop2;
    private bool _firedDeltaAccelStart;
    private bool _firedDeltaAccelStop;
    private bool _firedStage3Off;
    private bool _firedSatellite;

    // [추가] 오디오 재생용 플래그
    private bool _firedLaunchOK;
    private bool _firedNormalFly;
    
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

        _firedDrop1 = _firedFairing = _firedDrop2 = _firedStage3Off = _firedSatellite = _firedDeltaAccelStart = _firedDeltaAccelStop = false;
        _firedLaunchOK = false;
        _firedNormalFly = false;
        _firedSkyboxChange = false;

        bool firedLaunchSound = false;

        while (!token.IsCancellationRequested)
        {
            if (!count.IsCountingDown)
            {
                float t = count.TPlusSeconds;

                if (!firedLaunchSound && t >= 0f)
                {
                    firedLaunchSound = true;
                    SoundManager.Instance?.PlayAnnounceByKey("LaunchOK");
                    try
                    {   
                        CameraShaker.Instance.PlayShake(0.4f, 30);
                        await UniTask.Delay(6000, cancellationToken: token);
                        CameraShaker.Instance.StopShake();
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    SoundManager.Instance?.CrossFadeByKey("Launch_Stage01", loop: true);
                }

                // [추가] T+ 15초: "비행 정상"
                if (!_firedNormalFly && t >= 15f)
                {
                    _firedNormalFly = true;
                    SoundManager.Instance?.PlayAnnounceByKey("NormalFly");
                }
                
                if (!_firedSkyboxChange && t >= tSkyboxChange)
                {
                    _firedSkyboxChange = true;
                    // 전환
                    LaunchManager.Instance?.StartSkyboxCrossFade(2.0f); 
                }

                // 1) T+ 2:05 DropStage1 (1단 분리)
                if (!_firedDrop1 && t >= tDropStage1 && LaunchManager.Instance != null)
                {
                    CancellationTokenSource blinkCts = new CancellationTokenSource();
                    try
                    {
                        // ===== 왼쪽 버튼 대기 =====
                        LaunchManager.Instance.SetButtonOn("Left", blinkCts.Token);
                        count.BeginExternalHold(); // T+ 시간 멈춤
                        
                        LaunchManager.Instance.StartStagePingPong(3);
                        LaunchManager.Instance.SetGuideText("1단 분리 버튼을 누르세요.");
                        LaunchManager.Instance.ForceActiveInactivityTimer();
                        LaunchManager.Instance.PublicStartBlinkGreen(500, 160);

                        try
                        {
                            await LaunchManager.Instance.WaitForLeftButtonAsync(token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        LaunchManager.Instance.LerpCamera3Fov(2, 3f).Forget();
                        LaunchManager.Instance.SetGuideText("");
                        count.EndExternalHold();
                        LaunchManager.Instance.PauseInactivityTimer();

                        LaunchManager.Instance.PublicStopLedEffects();
                        LedStrip.Range(0, 9, 255, 0, 0);
                        // ========================
                    }
                    finally
                    {
                        blinkCts.Cancel();
                        blinkCts.Dispose();
                        LaunchManager.Instance.SetButtonOff("Left");
                    }

                    _firedDrop1 = true;
                    nuri.DropStage1().Forget();

                    LaunchManager.Instance.FocusImage3ThenPingPong4();          // 시퀀스 이미지 핑퐁
                    LaunchManager.Instance.FadeInStagePublicAsync(3).Forget();   // 스테이지 이미지 페이드 인
                    LaunchManager.Instance.FadeOutSubRocketStage1Async().Forget(); // 서브모니터 stage1 이미지 페이드 아웃
                    LaunchManager.Instance.FixStageAlpha(3);
                }

                // 2) T+ 3:56 SeparateFairing (페어링 분리)
                if (!_firedFairing && t >= tSeparateFairing && LaunchManager.Instance != null)
                {
                    CancellationTokenSource blinkCts = new CancellationTokenSource();
                    try
                    {
                        // ===== 오른쪽 버튼 대기 =====
                        LaunchManager.Instance.SetButtonOn("Right", blinkCts.Token);
                        count.BeginExternalHold();
                        
                        LaunchManager.Instance.StartStagePingPong(4);
                        LaunchManager.Instance.SetGuideText("3단(페어링 분리) 버튼을\n누르세요.");
                        LaunchManager.Instance.ForceActiveInactivityTimer();
                        LaunchManager.Instance.PublicStartBlinkGreen(500, 160);

                        try
                        {
                            await LaunchManager.Instance.WaitForRightButtonAsync(token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        LaunchManager.Instance.SetGuideText("");
                        count.EndExternalHold();
                        LaunchManager.Instance.PauseInactivityTimer();

                        LaunchManager.Instance.PublicStopLedEffects();
                        LedStrip.Range(0, 9, 255, 0, 0);
                        // =========================
                    }
                    finally
                    {
                        blinkCts.Cancel();
                        blinkCts.Dispose();
                        LaunchManager.Instance.SetButtonOff("Right");
                    }

                    _firedFairing = true;
                    nuri.SeparateFairing().Forget();

                    LaunchManager.Instance.FadeInStagePublicAsync(4).Forget();
                    LaunchManager.Instance.FadeOutSubRocketPairingAsync().Forget();
                    LaunchManager.Instance.FixStageAlpha(4);
                }

                // 3) T+ 4:30 DropStage2 (2단 분리)
                if (!_firedDrop2 && t >= tDropStage2 && LaunchManager.Instance != null)
                {
                    CancellationTokenSource blinkCts = new CancellationTokenSource();
                    try
                    {
                        // ===== 가운데 버튼 대기 =====
                        LaunchManager.Instance.SetButtonOn("Middle", blinkCts.Token);
                        count.BeginExternalHold();
                        
                        LaunchManager.Instance.StartStagePingPong(5);
                        LaunchManager.Instance.SetGuideText("2단 분리 버튼을 누르세요");
                        LaunchManager.Instance.ForceActiveInactivityTimer();
                        LaunchManager.Instance.PublicStartBlinkGreen(500, 160);

                        try
                        {
                            await LaunchManager.Instance.WaitForMiddleButtonAsync(token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        
                        LaunchManager.Instance.LerpCamera3Fov(2, 1).Forget();
                        LaunchManager.Instance.SetGuideText("");
                        count.EndExternalHold();
                        LaunchManager.Instance.PauseInactivityTimer();

                        LaunchManager.Instance.PublicStopLedEffects();
                        LedStrip.Range(0, 9, 255, 0, 0);
                        // =========================
                    }
                    finally
                    {
                        blinkCts.Cancel();
                        blinkCts.Dispose();
                        LaunchManager.Instance.SetButtonOff("Middle");
                    }

                    _firedDrop2 = true;
                    nuri.DropStage2().Forget();

                    LaunchManager.Instance.FadeInStagePublicAsync(5).Forget();
                    LaunchManager.Instance.FadeOutSubRocketStage2Async().Forget();
                    LaunchManager.Instance.FixStageAlpha(5);
                    
                    // 다음 단계(3단 분리)를 위해 Stage 6번 핑퐁 미리 시작
                    LaunchManager.Instance.StartStagePingPong(6);
                }

                if (!_firedDeltaAccelStart && t >= tDeltaAccelStart && CountController.Instance != null)
                {
                    _firedDeltaAccelStart = true;
                    CountController.Instance.DeltaTimeSpeed = 20f;
                }

                if (!_firedDeltaAccelStop && t >= tDeltaAccelStop && CountController.Instance != null)
                {
                    _firedDeltaAccelStop = true;
                    CountController.Instance.DeltaTimeSpeed = 5f;
                }

                // T+ 12:14 Stage3Off
                if (!_firedStage3Off && t >= tStage3Off)
                {
                    _firedStage3Off = true;
                    nuri.Stage3Off().Forget();

                    LaunchManager.Instance.FixStageAlpha(6);
                }

                // T+ 13:04 SeparateSatellite
                if (!_firedSatellite && t >= tSeparateSatellite)
                {
                    _firedSatellite = true;

                    LaunchManager.Instance?.FocusImage4ThenPingPong5();
                    LaunchManager.Instance?.FadeInStagePublicAsync(7).Forget();
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