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
    public ImageSetting main1;
    public ImageSetting main2;
    public ImageSetting main3;
    public ImageSetting subBg;
    public ImageSetting rocketStage1;
    public ImageSetting rocketStage2;
    public ImageSetting rocketStage3;
    public ImageSetting rocketPairing;

    public ImageSetting[] stages;
    public ImageSetting[] sequence;
    public ImageSetting[] oxidizers;
    public ImageSetting[] fuels;
    
    public TextSetting objectiveText;

    public ImageSetting controllerBackground;
    public ImageSetting buttonLeft;
    public ImageSetting buttonMiddle;
    public ImageSetting buttonRight;
    public ImageSetting throttleBackground;
    public ImageSetting throttleButton;
    
    public TextSetting guideText;
    
    public TextSetting timeText;
    public TextSetting altitudeText;
    public TextSetting velocityText;
    public TextSetting distanceText;
    public TextSetting timeValueText;
    public TextSetting altitudeValueText;
    public TextSetting velocityValueText;
    public TextSetting distanceValueText;

    public ImageSetting slopeBackgroundImage;
    public ImageSetting slopePointerImage;
}

public class LaunchManager : SceneManager_Base<LaunchSetting>
{
    public static LaunchManager Instance;

    [Header("UI")] 
    [SerializeField] private GameObject mainImage1;
    [SerializeField] private GameObject mainImage2;
    [SerializeField] private GameObject mainImage3;
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

    [Header("Fuels")] 
    [SerializeField] private GameObject stage1Fuel;
    [SerializeField] private GameObject stage2Fuel;
    [SerializeField] private GameObject stage3Fuel;

    [Header("MainImage1")] 
    [SerializeField] private Image[] stages;
    [SerializeField] private Image[] sequences;
    [SerializeField] private GameObject textObjective;

    [Header("MainImage2")]
    [SerializeField] private GameObject controllerBackground;
    [SerializeField] private GameObject buttonLeft;
    [SerializeField] private GameObject buttonMiddle;
    [SerializeField] private GameObject buttonRight;
    [SerializeField] private GameObject throttleBackground;
    [SerializeField] private GameObject throttleButton;
    [SerializeField] private GameObject textGuide;
    
    [Header("MainImage3")]
    [SerializeField] private GameObject textTime;
    [SerializeField] private GameObject textAltitude;
    [SerializeField] private GameObject textVelocity;
    [SerializeField] private GameObject textDistance;
    [SerializeField] private GameObject textTimeValue;
    [SerializeField] private GameObject textAltitudeValue;
    [SerializeField] private GameObject textVelocityValue;
    [SerializeField] private GameObject textDistanceValue;
    [SerializeField] private GameObject imageSlopeBackground;
    [SerializeField] private GameObject imageSlopePointer;
    
    [Header("Rocket")] 
    [SerializeField] private GameObject rocketVFX;

    protected override string JsonPath => "JSON/LaunchSetting.json";
    
    private float _throttleYEnd;
    private int _rocketCountdown;
    private RocketLaunch _rocketLaunch;
    private CancellationTokenSource[] _alphaCts;
    private CancellationTokenSource[] _stageCts;
    private readonly Dictionary<GameObject, CancellationTokenSource> _rocketFadeCts = new Dictionary<GameObject, CancellationTokenSource>();
    private bool _needThrottleDown; 
    private int throttleZeroDeadband = 10;

    protected override void Awake()
    {
        base.Awake();

        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);
    }

    protected override void OnDisable()
    {
        base.OnDisable();

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

    protected override async UniTask Init()
    {
        SettingImageObject(mainImage1, setting.main1);
        SettingImageObject(mainImage2, setting.main2);
        SettingImageObject(mainImage3, setting.main3);
        SettingImageObject(subBgImage, setting.subBg);
        SettingImageObject(subRocketStage1Image, setting.rocketStage1);
        SettingImageObject(subRocketStage2Image, setting.rocketStage2);
        SettingImageObject(subRocketStage3Image, setting.rocketStage3);
        SettingImageObject(subRocketPairingImage, setting.rocketPairing);

        // 산화제 이미지 세팅
        SettingImageObject(stage1Oxidizer, setting.oxidizers[0]);
        SettingImageObject(stage2Oxidizer, setting.oxidizers[1]);
        SettingImageObject(stage3Oxidizer, setting.oxidizers[2]);

        // 연료 이미지 세팅
        SettingImageObject(stage1Fuel, setting.fuels[0]);
        SettingImageObject(stage2Fuel, setting.fuels[1]);
        SettingImageObject(stage3Fuel, setting.fuels[2]);

        // ===== mainImage1 =====
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
        // ======================
        
        // ===== mainImage2 =====
        SettingImageObject(controllerBackground, setting.controllerBackground);
        SettingImageObject(buttonLeft, setting.buttonLeft);
        SettingImageObject(buttonMiddle, setting.buttonMiddle);
        SettingImageObject(buttonRight, setting.buttonRight);
        SettingImageObject(throttleBackground, setting.throttleBackground);
        SettingImageObject(throttleButton, setting.throttleButton);
        SettingTextObject(textGuide, setting.guideText, "").Forget();
        await SetInitialGuideByThrottleAsync();
        // ======================
        
        // ===== mainImage3 =====
        SettingTextObject(textTime, setting.timeText).Forget();
        SettingTextObject(textAltitude, setting.altitudeText).Forget();
        SettingTextObject(textVelocity, setting.velocityText).Forget();
        SettingTextObject(textDistance, setting.distanceText).Forget();
        SettingTextObject(textTimeValue, setting.timeValueText).Forget();
        SettingTextObject(textAltitudeValue, setting.altitudeValueText).Forget();
        SettingTextObject(textVelocityValue, setting.velocityValueText).Forget();
        SettingTextObject(textDistanceValue, setting.distanceValueText).Forget();
        SettingImageObject(imageSlopeBackground, setting.slopeBackgroundImage);
        SettingImageObject(imageSlopePointer, setting.slopePointerImage);
        // ======================
        
        if (sequences != null) _alphaCts = new CancellationTokenSource[sequences.Length];
        if (stages != null) _stageCts = new CancellationTokenSource[stages.Length];
        
        _rocketCountdown = Mathf.Max(1, setting.rocketCountdown);
        if (countdownText && countdownText.TryGetComponent(out TextMeshProUGUI tmp))
        {
            tmp.text = _rocketCountdown.ToString();
            SetAlpha(tmp, 0f);
        }

        StartPingPongAt(2, 0.28f, 1.0f, 2.0f);
        ArduinoInputManager.Instance?.SetLedAll(true);
        StartBlinkGreenAsync(500, 160);

        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage2, fadeImage3 });

        if (_needThrottleDown)
        {
            CancellationToken ct = DestroyToken;
            
            ArduinoInputManager.Instance?.Send("THROTTLE ON");  // 스로틀 스트림 시작
            await AwaitThrottleZeroAsync(throttleZeroDeadband, ct); // 0(데드밴드)로 들어올 때까지 블로킹 대기
            ArduinoInputManager.Instance?.Send("THROTTLE OFF"); // 스트림 중지

            // UI 정리
            SetButtonsOn(buttonLeft, buttonMiddle, buttonRight);
            SettingTextObject(textGuide, setting.guideText, "아무 버튼을 누르세요.").Forget();
            StopAnimateThrottleY();
        }
        
        // 입력 대기
        CancellationToken cancel = DestroyToken;
        while (!cancel.IsCancellationRequested && isActiveAndEnabled)
        {
            if ((ArduinoInputManager.Instance && ArduinoInputManager.Instance.TryConsumeAnyPress(out _)) || TryConsumeSingleInput())
            {   
                SettingTextObject(textGuide, setting.guideText, "").Forget();
                StopLedEffects();
                ArduinoInputManager.Instance?.SetLedAll(false);
                LedStrip.Range(0, 9, 255, 0, 0);
                SetButtonsOff(buttonLeft, buttonMiddle, buttonRight);

                StopPingPongAndSetAlpha(2, 1.0f); // 2번 고정 1
                StartPingPongAt(3, 0.28f, 1.0f, 2.0f); // 3번 핑퐁

                break;
            }

            await UniTask.Yield();
        }

        if (rocketVFX != null && rocketVFX.TryGetComponent(out _rocketLaunch))
        {
            _rocketLaunch.Call();
        }
        else
        {   
            if (!DestroyToken.IsCancellationRequested)
                Debug.LogError("[LaunchManager] RocketLaunch 컴포넌트에서 rocketVFX이 할당되지 않거나 Missing 됨");
        }

        // 카운트다운 시작
        RunCountdownAsync().Forget();
        CountController.Instance?.RunCountdownAsync().Forget();
    }

    /// <summary> 숫자를 갱신하고, 각 숫자마다 알파를 1 -> 0으로 부드럽게 페이드 </summary>
    private async UniTask RunCountdownAsync()
    {
        if (!countdownText || !countdownText.TryGetComponent(out TextMeshProUGUI tmp)) return;

        CancellationToken cancel = DestroyToken;
        float duration = Mathf.Max(0.01f, 1.0f);

        for (int n = _rocketCountdown; n > 0; n--)
        {
            if (cancel.IsCancellationRequested) return;

            tmp.text = n.ToString(); // 숫자 갱신 및 완전 표시
            SetAlpha(tmp, 1f);

            float t = 0f; // 알파 1 -> 0 페이드
            while (t < duration)
            {
                if (cancel.IsCancellationRequested) return;
                t += Time.deltaTime;
                float a = 1f - Mathf.Clamp01(t / duration);
                SetAlpha(tmp, a);
                await UniTask.Yield();
            }

            // 다음 숫자 전환 직전 완전 투명 보장
            SetAlpha(tmp, 0f);
        }
    }

    public async UniTask LoadNextSceneAsync()
    {
        int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 0;
        await LoadSceneAsync(target, new[] { fadeImage1, fadeImage2, fadeImage3 });
    }

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

    /// <summary> 특정 시퀀스 인덱스의 핑퐁을 중지하고 알파를 고정값으로 설정 </summary>
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

    /// <summary> 특정 시퀀스 인덱스의 핑퐁 시작(기존 실행 중이면 교체) </summary>
    private void StartPingPongAt(int index, float minA, float maxA, float periodSec)
    {
        if (sequences == null) return;
        if (index < 0 || index >= sequences.Length) return;
        if (sequences[index] == null) return;

        // 배열 초기화
        if (_alphaCts == null || _alphaCts.Length != sequences.Length)
        {
            int len = sequences.Length;
            _alphaCts = new CancellationTokenSource[len];
        }

        if (_alphaCts[index] != null)
        {
            CancelAndDispose(ref _alphaCts[index]);
        }

        // 시작
        StartAlphaPingPong(sequences[index], minA, maxA, periodSec, ref _alphaCts[index]);
    }

    /// <summary> 외부 호출: 3번 알파 1로 고정, 4번 핑퐁 시작 </summary>
    public void FocusImage3ThenPingPong4()
    {
        StopPingPongAndSetAlpha(3, 1.0f);
        StartPingPongAt(4, 0.28f, 1.0f, 2.0f);
    }

    /// <summary> 외부 호출: 4번 알파 1로 고정, 5번 핑퐁 시작 </summary>
    public void FocusImage4ThenPingPong5()
    {
        StopPingPongAndSetAlpha(4, 1.0f);
        StartPingPongAt(5, 0.28f, 1.0f, 2.0f);
    }

    /// <summary> 로켓 발사 중 스테이지 이미지를 페이드인 함 </summary>
    private async UniTask FadeInStageAsync(int index, float duration = 0.6f)
    {
        if (!TryGetStageGraphic(index, out Graphic g)) return;

        // 기존 진행 중이면 취소
        if (_stageCts == null || (stages != null && _stageCts.Length != stages.Length))
        {
            int len = stages?.Length ?? 0;
            _stageCts = (len > 0) ? new CancellationTokenSource[len] : null;
        }

        if (_stageCts != null && index >= 0 && index < _stageCts.Length)
        {
            CancelAndDispose(ref _stageCts[index]);
            _stageCts[index] = new CancellationTokenSource();
        }

        CancellationToken token = (_stageCts != null && index >= 0 && index < _stageCts.Length)
            ? _stageCts[index].Token : DestroyToken;

        float d = Mathf.Max(0.01f, duration);
        float t = 0f;

        // 시작 알파 보정
        float startA = g.color.a;
        while (t < d)
        {
            if (token.IsCancellationRequested) return;
            t += Time.deltaTime;
            float a = Mathf.Lerp(startA, 1f, t / d);
            SetAlpha(g, a);
            await UniTask.Yield();
        }

        SetAlpha(g, 1f);
    }

    public UniTask FadeInStagePublicAsync(int index, float duration = 0.6f)
    {
        return FadeInStageAsync(index, duration);
    }

    private bool TryGetStageGraphic(int index, out Graphic g)
    {
        g = null;
        if (stages == null) return false;
        if (index < 0 || index >= stages.Length) return false;
        if (!stages[index]) return false;
        g = stages[index];
        return true;
    }

    /// <summary> 서브 화면: 1단 이미지 페이드아웃 </summary>
    public UniTask FadeOutSubRocketStage1Async(float duration = 0.6f)
    {
        return FadeOutRocketImageAsync(subRocketStage1Image, duration);
    }

    /// <summary> 서브 화면: 2단 이미지 페이드아웃 </summary>
    public UniTask FadeOutSubRocketStage2Async(float duration = 0.6f)
    {
        return FadeOutRocketImageAsync(subRocketStage2Image, duration);
    }

    /// <summary> 서브 화면: 3단 이미지 페이드아웃 </summary>
    public UniTask FadeOutSubRocketStage3Async(float duration = 0.6f)
    {
        return FadeOutRocketImageAsync(subRocketStage3Image, duration);
    }

    /// <summary> 서브 화면: 페어링 이미지 페이드아웃 </summary>
    public UniTask FadeOutSubRocketPairingAsync(float duration = 0.6f)
    {
        return FadeOutRocketImageAsync(subRocketPairingImage, duration);
    }

    private async UniTask FadeOutRocketImageAsync(GameObject go, float duration)
    {
        if (!go) return;

        // 중복 실행 취소
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
            // 1) CanvasGroup 우선
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

            // 2) 단일 Graphic
            if (go.TryGetComponent(out Graphic g))
            {
                await LerpGraphicAlphaAsync(g, 0f, d, token);
                return;
            }

            // 3) 자식 Graphics 전체
            Graphic[] gs = go.GetComponentsInChildren<Graphic>(true);
            if (gs != null && gs.Length > 0)
            {
                // 시작 알파 스냅샷
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
            // 정리
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

    /// <summary> 단일 Graphic 알파를 목표값으로 보간 </summary>
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

    public void SetButtonOn(string whichButton)
    {
        switch (whichButton)
        {
            case "Left":
                 SetButtonOn(buttonLeft);
                 break;
            case "Middle":
                SetButtonOn(buttonMiddle);
                break;
            case "Right":
                SetButtonOn(buttonRight);
                break;
            default:
                Debug.LogWarning($"[LaunchManager] Unknown button name: {whichButton}");
                break;
        }
    }
    
    public void SetButtonOff(string whichButton)
    {
        switch (whichButton)
        {
            case "Left":
                SetButtonOff(buttonLeft);
                break;
            case "Middle":
                SetButtonOff(buttonMiddle);
                break;
            case "Right":
                SetButtonOff(buttonRight);
                break;
            default:
                Debug.LogWarning($"[LaunchManager] Unknown button name: {whichButton}");
                break;
        }
    }
    
    public async UniTask WaitForButtonAsync(string whichButton, CancellationToken token)
    {
        // 이전 입력 큐 비우기
        ArduinoInputManager.Instance?.FlushAll();

        // 대상 버튼 ID / KeyCode 결정
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
                Debug.LogWarning($"[LaunchManager] Unknown button name for WaitForButtonAsync: {whichButton}");
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
                    try { await UniTask.Delay(buttonDelayTime, cancellationToken: token); } catch { }
                }
                return;
            }

            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }
    }
    
    // ============================
    // 별칭(Left, Middle, Right 전용) 함수들
    // ============================
    public UniTask WaitForLeftButtonAsync(CancellationToken token) => WaitForButtonAsync("Left", token);
    public UniTask WaitForMiddleButtonAsync(CancellationToken token) => WaitForButtonAsync("Middle", token);
    public UniTask WaitForRightButtonAsync(CancellationToken token) => WaitForButtonAsync("Right", token);
    
    /// <summary> 시작 시 아두이노에 THROTTLE 명령을 보내서 안내문 결정 </summary>
    private async UniTask SetInitialGuideByThrottleAsync(int pollMs = 1000, int zeroDeadband = 10)
    {
        CancellationToken token = DestroyToken;

        ArduinoInputManager inst = ArduinoInputManager.Instance;
        if (inst == null)
        {
            await SettingTextObject(textGuide, setting.guideText, "아무 버튼을 누르세요.").AttachExternalCancellation(token);
            return;
        }

        // 1) 현재 버전 스냅샷
        int startVer = inst.ThrottleVersion;

        // 2) 1회 요청
        try { inst.Send("THROTTLE ONCE"); }
        catch (Exception e) { Debug.LogWarning($"[LaunchManager] THROTTLE ONCE 전송 실패: {e.Message}"); }

        // 3) 새 값이 도착할 때까지 대기
        int throttle = 0;
        bool gotNew = false;
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(pollMs);

        while (DateTime.UtcNow < deadline && !token.IsCancellationRequested)
        {
            if (inst.ThrottleVersion != startVer)
            {
                throttle = inst.LastThrottleValue; // 최신 값
                gotNew = true;
                break;
            }
            await UniTask.Delay(20, cancellationToken: token);
        }

        // 4) 판정
        if (gotNew)
        {
            if (Mathf.Abs(throttle) <= zeroDeadband)
            {   
                _needThrottleDown = false;
                await SettingTextObject(textGuide, setting.guideText, "아무 버튼을 누르세요.").AttachExternalCancellation(token);
                SetButtonsOn(buttonLeft, buttonMiddle, buttonRight);
            }
            else
            {   
                _needThrottleDown = true;
                await SettingTextObject(textGuide, setting.guideText, "스로틀을 내려주세요.").AttachExternalCancellation(token);
                SetButtonsOff(buttonLeft, buttonMiddle, buttonRight);
                AnimateThrottleY(0f, -110f, 0.8f, 0.2f);
            }
        }
    }
    
    /// <summary> THROTTLE ON 상태에서 스로틀이 0(±deadband)까지 내려갈 때까지 대기. </summary>
    private async UniTask AwaitThrottleZeroAsync(int deadband, CancellationToken ct)
    {
        ArduinoInputManager inst = ArduinoInputManager.Instance;
        if (inst == null) return;

        // 최신 값 변화 감지용 버전 스냅샷
        int ver = inst.ThrottleVersion;

        while (!ct.IsCancellationRequested)
        {
            // 새 값이 들어오면 판정
            if (inst.ThrottleVersion != ver)
            {
                ver = inst.ThrottleVersion;
                int v = inst.LastThrottleValue;
                if (Mathf.Abs(v) <= deadband) return;
            }

            // 너무 바쁘지 않게 20ms 간격 폴링
            await UniTask.Delay(20, cancellationToken: ct);
        }
    }
}
