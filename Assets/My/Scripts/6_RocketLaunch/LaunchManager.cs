using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class LaunchSetting
{
    public int rocketCountdown;
    public ImageSetting mainBackground;
    public ImageSetting subBg;
    public ImageSetting rocketStage1;
    public ImageSetting rocketStage2;
    public ImageSetting rocketStage3;
    public ImageSetting rocketPairing;

    public ImageSetting[] stages;
    public ImageSetting[] sequence;
    public ImageSetting[] oxidizers;
    public ImageSetting[] oxidizerTextImages;
    public ImageSetting[] fuels;
    public ImageSetting[] fuelTextImages;

    public TextSetting objectiveText;

    public ImageSetting controllerBackground;
    public ImageSetting buttonLeft;
    public ImageSetting buttonMiddle;
    public ImageSetting buttonRight;
    public ImageSetting throttleBackground;
    public ImageSetting throttleButton;

    public TextSetting guideText;
    
    public TextSetting timeValueText;
    public TextSetting altitudeValueText;
    public TextSetting velocityValueText;
    public TextSetting distanceValueText;
    public ImageSetting slopeBackgroundImage;
    public ImageSetting slopePointerImage;
}

/// <summary>
/// 발사 시나리오 UI 매니저
/// - 초기 안내 -> 버튼 입력 대기 -> 카운트다운 -> 로켓 런치
/// - 서브 디스플레이 스테이지 페이드 아웃 제공
/// </summary>
public class LaunchManager : SceneManager_Base<LaunchSetting>
{
    public static LaunchManager Instance;
    private static readonly int Trigger = Animator.StringToHash("Trigger");

    [Header("UI")]
    [SerializeField] private GameObject mainBackground;
    [SerializeField] private GameObject countdownText;
    [SerializeField] private GameObject subBgImage;
    [SerializeField] private GameObject subRocketStage1Image;
    [SerializeField] private GameObject subRocketStage2Image;
    [SerializeField] private GameObject subRocketStage3Image;
    [SerializeField] private GameObject subRocketPairingImage;

    [Header("Oxidizers")]
    [SerializeField] private GameObject stage1Oxidizer;
    [SerializeField] private GameObject stage2Oxidizer;
    [SerializeField] private GameObject stage3Oxidizer;
    
    [SerializeField] private GameObject stage1OxidizerTextImage;
    [SerializeField] private GameObject stage2OxidizerTextImage;
    [SerializeField] private GameObject stage3OxidizerTextImage;

    [Header("Fuels")]
    [SerializeField] private GameObject stage1Fuel;
    [SerializeField] private GameObject stage2Fuel;
    [SerializeField] private GameObject stage3Fuel;
    
    [SerializeField] private GameObject stage1FuelTextImage;
    [SerializeField] private GameObject stage2FuelTextImage;
    [SerializeField] private GameObject stage3FuelTextImage;

    [Header("Stage & Sequence")]
    [SerializeField] private Image[] stages;
    [SerializeField] private Image[] sequences;
    [SerializeField] private GameObject textObjective;

    [Header("Controllers")]
    [SerializeField] private GameObject controllerBackground;
    [SerializeField] private GameObject buttonLeft;
    [SerializeField] private GameObject buttonMiddle;
    [SerializeField] private GameObject buttonRight;
    [SerializeField] private GameObject throttleBackground;
    [SerializeField] private GameObject throttleButton;
    [SerializeField] private GameObject textGuide;

    [Header("Values")]
    [SerializeField] private GameObject textTimeValue;
    [SerializeField] private GameObject textAltitudeValue;
    [SerializeField] private GameObject textVelocityValue;
    [SerializeField] private GameObject textDistanceValue;
    [SerializeField] private GameObject imageSlopeBackground;
    [SerializeField] private GameObject imageSlopePointer;

    [Header("Rocket")]
    [SerializeField] private GameObject launcherObj;
    [SerializeField] private GameObject rocketVFX;
    [SerializeField] private NuriAnimEvent nuriAnimEvent;

    protected override string JsonPath => "JSON/LaunchSetting.json";

    private float _throttleYEnd;
    private int _rocketCountdown;
    private RocketLaunch _rocketLaunch;

    private CancellationTokenSource[] _alphaCts; // Sequence용 CTS
    private CancellationTokenSource[] _stageCts; // Stage용 CTS
    private readonly Dictionary<GameObject, CancellationTokenSource> _rocketFadeCts = new Dictionary<GameObject, CancellationTokenSource>();

    private bool _needThrottleDown;
    private readonly int _throttleZeroDeadband = 10;

    public bool RocketReady { get; set; }
    
    public Camera VerticalCamera
    {
        get => verticalCamera;
        private set =>  verticalCamera = value;
    }
    
    public Cubemap spaceSkybox; // 인스펙터에서 할당

    #region Unity lifecycle

    protected override void Awake()
    {
        base.Awake();

        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);
    }

    protected override void OnDisable()
    {
        base.OnDisable();

        try
        {
            if (_alphaCts != null)
            {
                for (int i = 0; i < _alphaCts.Length; i++)
                {
                    CancelAndDispose(ref _alphaCts[i]);
                }
            }

            if (_stageCts != null)
            {
                for (int i = 0; i < _stageCts.Length; i++)
                {
                    CancelAndDispose(ref _stageCts[i]);
                }
            }

            if (_rocketFadeCts != null)
            {
                foreach (KeyValuePair<GameObject, CancellationTokenSource> kv in _rocketFadeCts)
                {
                    CancellationTokenSource cts = kv.Value;
                    CancelAndDispose(ref cts);
                }
                _rocketFadeCts.Clear();
            }
        }
        catch (Exception e)
        {
            LogUtil.LogError(nameof(LaunchManager),nameof(OnDisable), e.ToString());
        }
    }

    protected override async UniTask Init()
    {
        // 이미지 배치
        SettingImageObject(mainBackground, setting.mainBackground);
        SettingImageObject(subBgImage, setting.subBg);
        SettingImageObject(subRocketStage1Image, setting.rocketStage1);
        SettingImageObject(subRocketStage2Image, setting.rocketStage2);
        SettingImageObject(subRocketStage3Image, setting.rocketStage3);
        SettingImageObject(subRocketPairingImage, setting.rocketPairing);

        // 산화제 이미지 세팅
        SettingImageObject(stage1Oxidizer, setting.oxidizers[0]);
        SettingImageObject(stage2Oxidizer, setting.oxidizers[1]);
        SettingImageObject(stage3Oxidizer, setting.oxidizers[2]);
        
        // "산화제" 글자 이미지 세팅
        SettingImageObject(stage1OxidizerTextImage, setting.oxidizerTextImages[0]);
        SettingImageObject(stage2OxidizerTextImage, setting.oxidizerTextImages[1]);
        SettingImageObject(stage3OxidizerTextImage, setting.oxidizerTextImages[2]);

        // 연료 이미지 세팅
        SettingImageObject(stage1Fuel, setting.fuels[0]);
        SettingImageObject(stage2Fuel, setting.fuels[1]);
        SettingImageObject(stage3Fuel, setting.fuels[2]);
        
        // "연료" 글자 이미지 세팅
        SettingImageObject(stage1FuelTextImage, setting.fuelTextImages[0]);
        SettingImageObject(stage2FuelTextImage, setting.fuelTextImages[1]);
        SettingImageObject(stage3FuelTextImage, setting.fuelTextImages[2]);

        // 단계/시퀀스 세팅
        if (setting.stages != null && stages != null)
        {
            int count = Mathf.Min(setting.stages.Length, stages.Length);
            for (int i = 0; i < count; i++)
            {
                if (stages[i] == null) continue;
                SettingImageObject(stages[i].gameObject, setting.stages[i]);
            }
        }

        if (setting.sequence != null && sequences != null)
        {
            int count = Mathf.Min(setting.sequence.Length, sequences.Length);
            for (int i = 0; i < count; i++)
            {
                if (sequences[i] == null) continue;
                SettingImageObject(sequences[i].gameObject, setting.sequence[i]);
            }
        }

        SettingTextObject(textObjective, setting.objectiveText, "로켓 발사를 완료하세요.").Forget();

        // mainImage2: 컨트롤러 UI
        SettingImageObject(controllerBackground, setting.controllerBackground);
        SettingImageObject(buttonLeft, setting.buttonLeft);
        SettingImageObject(buttonMiddle, setting.buttonMiddle);
        SettingImageObject(buttonRight, setting.buttonRight);
        SettingImageObject(throttleBackground, setting.throttleBackground);
        SettingImageObject(throttleButton, setting.throttleButton);
        SettingTextObject(textGuide, setting.guideText, string.Empty).Forget();

        await SetInitialGuideByThrottleAsync();

        // mainImage3: 계기판 텍스트/이미지
        SettingTextObject(textTimeValue, setting.timeValueText, "T - 00:00:10").Forget();
        SettingTextObject(textAltitudeValue, setting.altitudeValueText).Forget();
        SettingTextObject(textVelocityValue, setting.velocityValueText).Forget();
        SettingTextObject(textDistanceValue, setting.distanceValueText).Forget();
        SettingImageObject(imageSlopeBackground, setting.slopeBackgroundImage);
        SettingImageObject(imageSlopePointer, setting.slopePointerImage);

        if (sequences != null) _alphaCts = new CancellationTokenSource[sequences.Length];
        if (stages != null) _stageCts = new CancellationTokenSource[stages.Length];

        // 카운트다운 텍스트 초기화
        _rocketCountdown = Mathf.Max(1, setting.rocketCountdown);
        if (countdownText != null && countdownText.TryGetComponent(out TextMeshProUGUI tmp))
        {
            tmp.text = _rocketCountdown.ToString();
            SetAlpha(tmp, 0f);
        }

        // 시퀀스 핑퐁 애니메이션 (Sequence 2번)
        StartPingPongAt(2, 0.28f, 1.0f, 2.0f);
        StartStagePingPong(1);

        // 첫 페이드
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage2, fadeImage3 });
       
        StopLedEffects();
        ArduinoInputManager.Instance?.SetLedAll(false);
        LedStrip.Range(0, 9, 255, 0, 0);
        
        // 스로틀 내려야 한다면 대기
        if (_needThrottleDown)
        {
            CancellationToken ct = DestroyToken;

            try { ArduinoInputManager.Instance?.Send("THROTTLE ON"); }
            catch (Exception e) { LogUtil.LogWarn(nameof(LaunchManager), nameof(Init), "Send THROTTLE ON failed: " + e.Message); }

            await AwaitThrottleZeroAsync(_throttleZeroDeadband, ct);

            try { ArduinoInputManager.Instance?.Send("THROTTLE OFF"); }
            catch (Exception e) { LogUtil.LogWarn(nameof(LaunchManager),nameof(Init), "Send THROTTLE OFF failed: " + e.Message); }
            
            SettingTextObject(textGuide, setting.guideText, "대기 중.").Forget();
            StopAnimateThrottleY();
        }

        nuriAnimEvent?.StartBottomAndEngineSmoke(); // 로켓 하단 연기 시작
        
        SetButtonsOn(buttonLeft, buttonMiddle, buttonRight);
        SettingTextObject(textGuide, setting.guideText, "아무 버튼을 누르세요.").Forget();
        
        ArduinoInputManager.Instance?.SetLedAll(true);
        StartBlinkGreenAsync(500, 160);

        // 입력 대기 루프
        CancellationToken cancel = DestroyToken;
        while (!cancel.IsCancellationRequested && isActiveAndEnabled)
        {
            bool arduinoPressed = ArduinoInputManager.Instance != null && ArduinoInputManager.Instance.TryConsumeAnyPress(out _);
            if (arduinoPressed || TryConsumeSingleInput())
            {
                SettingTextObject(textGuide, setting.guideText, string.Empty).Forget();
                StopLedEffects();
                ArduinoInputManager.Instance?.SetLedAll(false);
                LedStrip.Range(0, 9, 255, 0, 0);
                SoundManager.Instance?.PlayAnnounceByKey("Countdown");

                SetButtonsOff(buttonLeft, buttonMiddle, buttonRight);

                // Sequence 제어: 2번 고정, 3번 핑퐁
                StopPingPongAndSetAlpha(2, 1.0f);
                StartPingPongAt(3, 0.28f, 1.0f, 2.0f);
                break;
            }

            ArduinoInputManager.Instance?.FlushAll();
            await UniTask.Yield();
        }
        
        // 로켓 런치 시작
        if (rocketVFX != null && rocketVFX.TryGetComponent(out _rocketLaunch))
        {
            _rocketLaunch.Call();
        }
        else
        {
            if (!DestroyToken.IsCancellationRequested)
                LogUtil.LogError(nameof(LaunchManager),nameof(Init), "RocketLaunch component missing or rocketVFX not assigned");
        }

        // 카운트다운 시작
        RunCountdownAsync().Forget();
        CountController.Instance?.RunCountdownAsync().Forget();
        
        // 무입력 복귀 일시 중지
        PauseInactivityTimer();
    }

    #endregion

    #region Countdown

    /// <summary>
    /// 숫자를 갱신하고, 각 숫자마다 알파를 1 -> 0으로 페이드,
    /// 현재는 미사용하고 1초일 때 카메라 러프로 사용 중 
    /// </summary>
    private async UniTask RunCountdownAsync()
    {
        if (countdownText == null || !countdownText.TryGetComponent(out TextMeshProUGUI tmp)) return;

        CancellationToken cancel = DestroyToken;
        float duration = 1.0f;

        for (int n = _rocketCountdown; n > 0; n--)
        {
            if (cancel.IsCancellationRequested) return;

            tmp.text = n.ToString();
            SetAlpha(tmp, 1f);

            float t = 0f;
            while (t < duration)
            {
                if (cancel.IsCancellationRequested) return;
                t += Time.deltaTime;
                float a = 1f - Mathf.Clamp01(t / duration);
                SetAlpha(tmp, a);
                await UniTask.Yield();
            }

            SetAlpha(tmp, 0f);

            if (n == 1)
            {
                LerpCamera3Fov(2, 10).Forget();
            }
        }
    }

    #endregion

    #region Scene transition

    public async UniTask LoadNextSceneAsync()
    {
        int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 0;
        await LoadSceneAsync(target, new[] { fadeImage1, fadeImage2, fadeImage3 });
    }

    #endregion

    #region Sequence alpha helpers

    /// <summary> 인덱스 범위/널 체크 후 Graphic 반환 </summary>
    private bool TryGetChildGraphic(int index, out Graphic g)
    {
        g = null;
        if (sequences == null) return false;
        if (index < 0 || index >= sequences.Length) return false;
        if (sequences[index] == null) return false;
        g = sequences[index];
        return true;
    }

    /// <summary> 특정 시퀀스 인덱스의 핑퐁을 중지하고 알파를 고정 </summary>
    private void StopPingPongAndSetAlpha(int index, float alpha)
    {
        if (_alphaCts != null && index >= 0 && index < _alphaCts.Length)
        {
            CancelAndDispose(ref _alphaCts[index]);
        }

        if (TryGetChildGraphic(index, out Graphic g))
        {
            SetAlpha(g, alpha);
        }
    }

    /// <summary> 특정 시퀀스 인덱스의 핑퐁 시작 </summary>
    private void StartPingPongAt(int index, float minA, float maxA, float periodSec)
    {
        if (sequences == null) return;
        if (index < 0 || index >= sequences.Length) return;
        if (sequences[index] == null) return;

        if (_alphaCts == null || _alphaCts.Length != sequences.Length)
        {
            int len = sequences.Length;
            _alphaCts = new CancellationTokenSource[len];
        }

        if (_alphaCts[index] != null)
        {
            CancelAndDispose(ref _alphaCts[index]);
        }

        StartAlphaPingPong(sequences[index], minA, maxA, periodSec, ref _alphaCts[index]);
    }

    /// <summary> 외부 호출: 3번 알파 1로 고정 -> 4번 핑퐁 </summary>
    public void FocusImage3ThenPingPong4()
    {
        StopPingPongAndSetAlpha(3, 1.0f);
        StartPingPongAt(4, 0.28f, 1.0f, 2.0f);
    }

    /// <summary> 외부 호출: 4번 알파 1로 고정 -> 5번 핑퐁 </summary>
    public void FocusImage4ThenPingPong5()
    {
        StopPingPongAndSetAlpha(4, 1.0f);
        StartPingPongAt(5, 0.28f, 1.0f, 2.0f);
    }

    #endregion

    #region Stage Helpers (New Logic)

    private bool TryGetStageGraphic(int index, out Graphic g)
    {
        g = null;
        if (stages == null) return false;
        if (index < 0 || index >= stages.Length) return false;
        if (!stages[index]) return false;
        g = stages[index];
        return true;
    }

    // [추가] Stage 전용 핑퐁 시작 (Sequence와 별도로 동작)
    public void StartStagePingPong(int index, float minA = 0.28f, float maxA = 1.0f, float periodSec = 2.0f)
    {
        if (!TryGetStageGraphic(index, out Graphic g)) return;

        // 배열/토큰 초기화
        if (_stageCts == null || _stageCts.Length != stages.Length)
        {
            int len = stages != null ? stages.Length : 0;
            _stageCts = (len > 0) ? new CancellationTokenSource[len] : null;
        }

        if (_stageCts == null) return;

        // 기존 동작 취소 후 새로 시작
        if (_stageCts[index] != null) CancelAndDispose(ref _stageCts[index]);

        StartAlphaPingPong(g, minA, maxA, periodSec, ref _stageCts[index]);
    }

    // [추가] Stage 전용 핑퐁 중지 및 알파 1로 고정
    public void FixStageAlpha(int index)
    {
        if (_stageCts != null && index >= 0 && index < _stageCts.Length)
        {
            CancelAndDispose(ref _stageCts[index]);
        }

        if (TryGetStageGraphic(index, out Graphic g))
        {
            SetAlpha(g, 1.0f);
        }
    }

    // [하위 호환용] 트리거 컨트롤러 등에서 FadeInStagePublicAsync 호출 시 Stage 핑퐁 시작으로 연결
    public UniTask FadeInStagePublicAsync(int index, float duration = 0.6f)
    {
        StartStagePingPong(index);
        return UniTask.CompletedTask;
    }

    #endregion

    #region Sub display fades

    public UniTask FadeOutSubRocketStage1Async(float duration = 0.6f)
    {
        return FadeOutRocketImageAsync(subRocketStage1Image, duration);
    }

    public UniTask FadeOutSubRocketStage2Async(float duration = 0.6f)
    {
        return FadeOutRocketImageAsync(subRocketStage2Image, duration);
    }

    public UniTask FadeOutSubRocketStage3Async(float duration = 0.6f)
    {
        return FadeOutRocketImageAsync(subRocketStage3Image, duration);
    }

    public UniTask FadeOutSubRocketPairingAsync(float duration = 0.6f)
    {
        return FadeOutRocketImageAsync(subRocketPairingImage, duration);
    }

    private async UniTask FadeOutRocketImageAsync(GameObject go, float duration)
    {
        if (!go) return;

        if (_rocketFadeCts.TryGetValue(go, out CancellationTokenSource running) && running != null)
        {
            CancelAndDispose(ref running);
        }

        CancellationTokenSource cts = new CancellationTokenSource();
        _rocketFadeCts[go] = cts;

        CancellationToken token = cts.Token;
        float d = Mathf.Max(0.01f, duration);

        try
        {
            if (go.TryGetComponent(out CanvasGroup cg))
            {
                float start = cg.alpha;
                float t = 0f;
                while (t < d)
                {
                    if (token.IsCancellationRequested) return;
                    t += Time.deltaTime;
                    float u = Mathf.Clamp01(t / d);
                    cg.alpha = Mathf.Lerp(start, 0f, u);
                    await UniTask.Yield();
                }

                cg.alpha = 0f;
                return;
            }

            if (go.TryGetComponent(out Graphic g))
            {
                await LerpGraphicAlphaAsync(g, 0f, d, token);
                return;
            }

            Graphic[] gs = go.GetComponentsInChildren<Graphic>(true);
            if (gs != null && gs.Length > 0)
            {
                float[] starts = new float[gs.Length];
                for (int i = 0; i < gs.Length; i++)
                {
                    starts[i] = (gs[i] != null) ? gs[i].color.a : 1f;
                }

                float t = 0f;
                while (t < d)
                {
                    if (token.IsCancellationRequested) return;
                    t += Time.deltaTime;
                    float u = Mathf.Clamp01(t / d);
                    for (int i = 0; i < gs.Length; i++)
                    {
                        Graphic gi = gs[i];
                        if (!gi) continue;
                        float a = Mathf.Lerp(starts[i], 0f, u);
                        SetAlpha(gi, a);
                    }
                    await UniTask.Yield();
                }

                foreach (Graphic t1 in gs)
                {
                    if (t1) SetAlpha(t1, 0f);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_rocketFadeCts.TryGetValue(go, out CancellationTokenSource mine) && mine == cts)
            {
                CancelAndDispose(ref cts);
                _rocketFadeCts.Remove(go);
            }
            else
            {
                CancelAndDispose(ref cts);
            }
        }
    }

    private async UniTask LerpGraphicAlphaAsync(Graphic g, float targetA, float duration, CancellationToken token)
    {
        if (!g) return;

        float startA = g.color.a;
        float t = 0f;
        float d = Mathf.Max(0.01f, duration);

        while (t < d)
        {
            if (token.IsCancellationRequested) return;
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / d);
            float a = Mathf.Lerp(startA, targetA, u);
            SetAlpha(g, a);
            await UniTask.Yield();
        }

        SetAlpha(g, targetA);
    }

    #endregion

    #region Throttle UI helpers

    public void AnimateThrottleY(float yStart, float yEnd, float duration, float waitAtEnd)
    {
        _throttleYEnd = yEnd;
        PlayAnchoredY(throttleButton, yStart, yEnd, duration, waitAtEnd);
    }

    public void StopAnimateThrottleY()
    {
        StopAnchoredY(throttleButton);
        SnapAnchoredY(throttleButton, _throttleYEnd);
    }

    private void SnapAnchoredY(GameObject go, float yTarget)
    {
        if (!go) return;
        RectTransform rt;
        if (go.TryGetComponent(out rt))
        {
            Vector2 pos = rt.anchoredPosition;
            pos.y = yTarget;
            rt.anchoredPosition = pos;
        }
    }

    public void SetGuideText(string text)
    {
        SettingTextObject(textGuide, setting.guideText, text).Forget();
    }

    #endregion

    #region Button helpers (Left/Middle/Right)

    public void SetButtonOn(string whichButton, CancellationToken blinkCts)
    {
        switch (whichButton)
        {
            case "Left":
                StopAllButtonBlinks();
                ArduinoInputManager.Instance?.SetLedAll(false);
                
                StartButtonBlink(buttonLeft);
                BlinkLedAsync(1, 300, 300, blinkCts).Forget();
                break;
            case "Middle":
                StopAllButtonBlinks();
                ArduinoInputManager.Instance?.SetLedAll(false);
                
                StartButtonBlink(buttonMiddle);
                BlinkLedAsync(2, 300, 300, blinkCts).Forget();
                break;
            case "Right":
                StopAllButtonBlinks();
                ArduinoInputManager.Instance?.SetLedAll(false);
                
                StartButtonBlink(buttonRight);
                BlinkLedAsync(3, 300, 300, blinkCts).Forget();
                break;
            default:
                LogUtil.LogWarn(nameof(LaunchManager),nameof(SetButtonOn), "Unknown button name: " + whichButton);
                break;
        }
    }

    public void SetButtonOff(string whichButton)
    {
        switch (whichButton)
        {
            case "Left":
                StopButtonBlink(buttonLeft);
                break;
            case "Middle":
                StopButtonBlink(buttonMiddle);
                break;
            case "Right":
                StopButtonBlink(buttonRight);
                break;
            default:
                LogUtil.LogWarn(nameof(LaunchManager),nameof(SetButtonOff), "Unknown button name: " + whichButton);
                break;
        }
    }

    public async UniTask WaitForButtonAsync(string whichButton, CancellationToken token)
    {
        ArduinoInputManager.Instance?.FlushAll();

        ArduinoInputManager.ButtonId targetId = ArduinoInputManager.ButtonId.None;
        KeyCode targetKey = KeyCode.None;

        switch (whichButton)
        {
            case "Left":
                targetId = ArduinoInputManager.ButtonId.Button1;
                targetKey = KeyCode.LeftArrow;
                break;
            case "Middle":
                targetId = ArduinoInputManager.ButtonId.Button2;
                targetKey = KeyCode.DownArrow;
                break;
            case "Right":
                targetId = ArduinoInputManager.ButtonId.Button3;
                targetKey = KeyCode.RightArrow;
                break;
            default:
                LogUtil.LogWarn(nameof(LaunchManager),nameof(WaitForButtonAsync), "Unknown button name: " + whichButton);
                return;
        }

        while (!token.IsCancellationRequested)
        {
            bool pressed = false;

            if (ArduinoInputManager.Instance != null)
            {
                if (ArduinoInputManager.Instance.TryConsumeAnyPress(out ArduinoInputManager.ButtonId btn) &&
                    btn == targetId)
                {
                    pressed = true;
                }
            }

            if (Input.GetKeyDown(targetKey))
            {
                pressed = true;
            }

            if (pressed)
            {
                if (buttonDelayTime > 0)
                {
                    try
                    {
                        await UniTask.Delay(buttonDelayTime, cancellationToken: token);
                    }
                    catch (OperationCanceledException) { }
                }
                return;
            }

            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }
    }

    public UniTask WaitForLeftButtonAsync(CancellationToken token) => WaitForButtonAsync("Left", token);
    public UniTask WaitForMiddleButtonAsync(CancellationToken token) => WaitForButtonAsync("Middle", token);
    public UniTask WaitForRightButtonAsync(CancellationToken token) => WaitForButtonAsync("Right", token);

    #endregion

    #region Throttle logic

    /// <summary> 시작 시 아두이노의 스로틀 값을 확인해 초기 안내문을 결정 </summary>
    private async UniTask SetInitialGuideByThrottleAsync(int pollMs = 1000, int zeroDeadband = 10)
    {
        CancellationToken token = DestroyToken;

        ArduinoInputManager inst = ArduinoInputManager.Instance;
        if (inst == null)
        {
            await SettingTextObject(textGuide, setting.guideText, "아무 버튼을 누르세요.").AttachExternalCancellation(token);
            return;
        }

        int startVer = inst.ThrottleVersion;

        try { inst.Send("THROTTLE ONCE"); }
        catch (Exception e)
        {
            LogUtil.LogWarn(nameof(LaunchManager),nameof(SetInitialGuideByThrottleAsync), "THROTTLE ONCE send failed: " + e.Message);
        }

        int throttle = 0;
        bool gotNew = false;
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(pollMs);

        while (DateTime.UtcNow < deadline && !token.IsCancellationRequested)
        {
            if (inst.ThrottleVersion != startVer)
            {
                throttle = inst.LastThrottleValue;
                gotNew = true;
                break;
            }
            await UniTask.Delay(20, cancellationToken: token);
        }

        if (gotNew)
        {
            if (Mathf.Abs(throttle) <= zeroDeadband)
            {
                _needThrottleDown = false;
                await SettingTextObject(textGuide, setting.guideText, "대기 중.").AttachExternalCancellation(token);
                SetButtonsOff(buttonLeft, buttonMiddle, buttonRight);
            }
            else
            {
                _needThrottleDown = true;
                await SettingTextObject(textGuide, setting.guideText, "각도 조정기를 내려주세요.").AttachExternalCancellation(token);
                SetButtonsOff(buttonLeft, buttonMiddle, buttonRight);
                AnimateThrottleY(0f, -110f, 0.8f, 0.2f);
            }
        }
        else
        {
            // 응답이 없으면 보수적으로 버튼 입력 대기
            _needThrottleDown = false;
            await SettingTextObject(textGuide, setting.guideText, "대기 중.").AttachExternalCancellation(token);
            SetButtonsOff(buttonLeft, buttonMiddle, buttonRight);
        }
    }

    /// <summary> THROTTLE ON 상태에서 스로틀이 0(±deadband)까지 내려갈 때까지 대기 </summary>
    private async UniTask AwaitThrottleZeroAsync(int deadband, CancellationToken ct)
    {
        ArduinoInputManager inst = ArduinoInputManager.Instance;
        if (inst == null) return;

        int ver = inst.ThrottleVersion;

        while (!ct.IsCancellationRequested)
        {
            if (inst.ThrottleVersion != ver)
            {
                ver = inst.ThrottleVersion;
                int v = inst.LastThrottleValue;
                if (Mathf.Abs(v) <= deadband) return;
            }

            await UniTask.Delay(20, cancellationToken: ct);
        }
    }

    #endregion
    
    public async UniTask FadeAndDeleteBg()
    {
        float newFadeTime = 1.0f;
        await FadeImageAsync(0f, 1f, newFadeTime, new[] { fadeImage3 });
        
        launcherObj?.SetActive(false);
        
        await FadeImageAsync(1f, 0f, newFadeTime, new[] { fadeImage3 });
    }
    
    /// <Summary>현재 FOV에서 매개변수 값만큼 줄어들도록 Lerp</Summary>
    public async UniTask LerpCamera3Fov(float duration, float deltaFov)
    {
        if (verticalCamera == null) return;

        float d = Mathf.Max(0.01f, duration);
        float startFov = verticalCamera.fieldOfView;
    
        // 들어온 값만큼 빼는 방식 (deltaFov가 10이면 startFov - 10 으로)
        float targetFov = startFov - deltaFov;

        float t = 0f;
        CancellationToken token = DestroyToken;

        while (t < d && !token.IsCancellationRequested)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / d);
            float fov = Mathf.Lerp(startFov, targetFov, u);
            verticalCamera.fieldOfView = fov;

            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        if (!token.IsCancellationRequested)
        {
            verticalCamera.fieldOfView = targetFov;
        }
    }

    public async UniTask FadeVerticalAsync(float start, float end)
    {
        await FadeImageAsync(start, end, fadeTime, new[] { fadeImage3 });
    }
    
    public void StartSkyboxCrossFade(float duration = 4.0f)
    {
        if (SkyboxBlender.Instance != null && spaceSkybox != null)
        {
            // SkyboxBlender를 통해 크로스 페이드 시작
            SkyboxBlender.Instance.ChangeSkyboxAsync(spaceSkybox, duration, DestroyToken).Forget();
        }
        else
        {
            Debug.LogWarning("[LaunchManager] SkyboxBlender 인스턴스나 spaceSkybox가 설정되지 않았습니다.");
        }
    }
}