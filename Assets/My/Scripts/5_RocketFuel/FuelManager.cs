using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class FuelSetting
{
    public float popupFadeTime;
    public float fuelFillSpeed;

    public ImageSetting mainBackground;
    public ImageSetting[] sequences;

    public ImageSetting fuelPopup;
    public ImageSetting subBg;
    public ImageSetting subRocket;
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
    public ImageSetting slopeBg;
    public ImageSetting slopePointer;
}

/// <summary> 우주발사체의 연료/산화제 씬 관리 매니저 </summary>
public sealed class FuelManager : SceneManager_Base<FuelSetting>
{
    private static readonly int Trigger = Animator.StringToHash("Trigger");
    public static FuelManager Instance;

    // ===== JSON 경로 =====
    protected override string JsonPath => "JSON/FuelSetting.json";

    #region Serialized Refs

    [Header("UI")]
    [SerializeField] private GameObject backgroundImage;
    [SerializeField] private GameObject popupImage;
    [SerializeField] private GameObject subBgImage;
    [SerializeField] private GameObject subRocketImage;

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

    [Header("Sequences")] 
    [SerializeField] private Image[] sequences; // 메인1 하위 단계별 강조 이미지들
    [SerializeField] private GameObject textObjective; // 목표 텍스트

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
    [SerializeField] private GameObject imageSlopeBg;
    [SerializeField] private GameObject imageSlopePointer;
    
    [Header("Launcher")]
    [SerializeField] private Animator launcherAnimator;

    #endregion

    #region Settings / State

    // private
    private CancellationTokenSource _blinkCts; // LED 블링크 토큰
    private CancellationTokenSource[] _sequenceCts; // 시퀀스 이미지별 핑퐁 제어용 배열 토큰
    private CancellationTokenSource _popupFadeCts; // 팝업 페이드 토큰

    private float _fuelFillSpeed; // 연료 증가 속도 (fillAmount/sec)
    private float _popupFadeTime; // 팝업 사라지는 시간

    private Image _fuel1Image; // 1단계 연료 이미지 (Filled)
    private Image _fuel2Image; // 2단계 연료 이미지 (Filled)
    private Image _fuel3Image; // 3단계 연료 이미지 (Filled)

    private enum Phase { RocketMove, FuelInjection1, FuelInjection2, FuelInjection3, Done }

    private Phase _phase = Phase.RocketMove; // 현재 단계
    
    public bool RocketReady { get; set; }

    #endregion

    #region Unity Life-Cycle
    
    protected override void Awake()
    {
        base.Awake();

        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);
    }

    /// <summary> 비활성화 시 토큰/LED 정리 </summary>
    protected override void OnDisable()
    {
        base.OnDisable();
        try
        {
            CancelAndDispose(ref _blinkCts);
            
            // 시퀀스 토큰 배열 정리
            if (_sequenceCts != null)
            {
                for (int i = 0; i < _sequenceCts.Length; i++)
                {
                    CancelAndDispose(ref _sequenceCts[i]);
                }
            }
            
            CancelAndDispose(ref _popupFadeCts);
        }
        catch (Exception e)
        {
            Debug.LogError($"[FuelManager] OnDisable-> 예외: {e}");
        }
    }

    #endregion

    #region Initialization

    /// <summary> 초기 세팅: 이미지 구성 → 게이지 초기화 → LED/버튼 상태 → 페이드 인 → 주입 루틴 시작 </summary>
    protected override async UniTask Init()
    {
        _popupFadeTime = Mathf.Max(0f, setting.popupFadeTime);
        _fuelFillSpeed = Mathf.Max(0f, setting.fuelFillSpeed);

        // 고정 이미지 세팅
        SettingImageObject(backgroundImage, setting.mainBackground);
        SettingImageObject(popupImage, setting.fuelPopup);
        SettingImageObject(subBgImage, setting.subBg);
        SettingImageObject(subRocketImage, setting.subRocket);

        // ===== mainImage1 =====
        if (setting.sequences != null && sequences != null)
        {
            int count = Mathf.Min(setting.sequences.Length, sequences.Length);
            for (int i = 0; i < count; i++)
            {
                if (sequences[i] == null) continue;
                try
                {
                    SettingImageObject(sequences[i].gameObject, setting.sequences[i]);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[FuelManager] Init-> sequences[{i}] 세팅 중 예외: {e.Message}");
                }
            }
        }

        // 시작 시 0번 인덱스 핑퐁 시작
        StartSequencePingPong(0);

        SettingTextObject(textObjective, setting.objectiveText, "연료를 주입하세요.").Forget();

        // ===== Controllers =====
        SettingImageObject(controllerBackground, setting.controllerBackground);
        SettingImageObject(buttonLeft, setting.buttonLeft);
        SettingImageObject(buttonMiddle, setting.buttonMiddle);
        SettingImageObject(buttonRight, setting.buttonRight);
        SettingImageObject(throttleBackground, setting.throttleBackground);
        SettingImageObject(throttleButton, setting.throttleButton);
        SettingTextObject(textGuide, setting.guideText, "로켓 거치 중.").Forget();

        // ===== Values =====
        SettingTextObject(textTimeValue, setting.timeValueText).Forget();
        SettingTextObject(textAltitudeValue, setting.altitudeValueText).Forget();
        SettingTextObject(textVelocityValue, setting.velocityValueText).Forget();
        SettingTextObject(textDistanceValue, setting.distanceValueText).Forget();
        SettingImageObject(imageSlopeBg, setting.slopeBg);
        SettingImageObject(imageSlopePointer, setting.slopePointer);

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

        InitFuelImage(); // fillAmount 0으로 초기화
        
        StopLedEffects();
        ArduinoInputManager.Instance?.SetLedAll(false);
        SetButtonsOff(buttonLeft, buttonMiddle, buttonRight);
        LedStrip.Range(0, 9, 255, 0, 0);
        
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage2, fadeImage3 });

        launcherAnimator?.SetTrigger(Trigger);
        _phase = Phase.FuelInjection1;
        _blinkCts = new CancellationTokenSource();
        
        // RocketReady 대기
        await UniTask.WaitUntil(() => RocketReady, cancellationToken: DestroyToken);
        
        // RocketReady true 시점: 0번 고정 -> 1번 핑퐁
        StopSequencePingPong(0);
        StartSequencePingPong(1);
        
        _popupFadeCts = new CancellationTokenSource();
        PopupFadeAsync(_popupFadeTime, _popupFadeCts.Token).Forget();
        SoundManager.Instance?.PlayByKey("Popup_Close");
        
        await FuelFillAsync(); // 시작
    }

    #endregion

    #region Fuel Flow

    /// <summary> 단계별 입력/증가 루프를 비동기로 진행 </summary>
    private async UniTask FuelFillAsync()
    {
        // 1단계: 왼쪽 버튼 3번
        while (canInput && _phase == Phase.FuelInjection1)
        {
            ArduinoInputManager.ButtonId btn = ArduinoInputManager.ButtonId.None;
            if (ArduinoInputManager.Instance != null)
            {
                ArduinoInputManager.Instance.TryConsumeAnyPress(out btn);
            }

            // 3번 눌러야 하므로 1회당 0.34f(약 34%)씩 증가 (Input.GetKeyDown 사용)
            if (btn == ArduinoInputManager.ButtonId.Button1 || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                if (IncreaseFill(_fuel1Image, 0.34f))
                {
                    // LED1 블링크 종료
                    CancelAndDispose(ref _blinkCts);
                    try { ArduinoInputManager.Instance?.SetLed(1, false); }
                    catch (Exception e) { Debug.LogWarning($"[FuelManager] FuelFillAsync-> LED1 끄기 중 예외: {e.Message}"); }

                    StopButtonBlink(buttonLeft);

                    // LED2 블링크 시작
                    _blinkCts = new CancellationTokenSource();
                    SettingTextObject(textGuide, setting.guideText, "2단(연료주입) 버튼을\n누르세요.").Forget();
                    BlinkLedAsync(2, 300, 300, _blinkCts.Token).Forget();
                    StartButtonBlink(buttonMiddle);

                    _phase = Phase.FuelInjection2;
                    break;
                }
            }

            await UniTask.Yield();
        }

        // 2단계: 가운데 버튼 2번
        while (canInput && _phase == Phase.FuelInjection2)
        {
            ArduinoInputManager.ButtonId btn = ArduinoInputManager.ButtonId.None;
            if (ArduinoInputManager.Instance != null)
            {
                ArduinoInputManager.Instance.TryConsumeAnyPress(out btn);
            }

            // 2번 눌러야 하므로 1회당 0.51f(약 51%)씩 증가
            if (btn == ArduinoInputManager.ButtonId.Button2 || Input.GetKeyDown(KeyCode.DownArrow))
            {
                if (IncreaseFill(_fuel2Image, 0.51f))
                {
                    // LED2 블링크 종료
                    CancelAndDispose(ref _blinkCts);
                    try { ArduinoInputManager.Instance?.SetLed(2, false); }
                    catch (Exception e) { Debug.LogWarning($"[FuelManager] FuelFillAsync-> LED2 끄기 중 예외: {e.Message}"); }

                    StopButtonBlink(buttonMiddle);

                    // LED3 블링크 시작
                    _blinkCts = new CancellationTokenSource();
                    SettingTextObject(textGuide, setting.guideText, "3단(연료주입) 버튼을\n누르세요.").Forget();
                    BlinkLedAsync(3, 300, 300, _blinkCts.Token).Forget();
                    StartButtonBlink(buttonRight);

                    _phase = Phase.FuelInjection3;
                }
            }

            await UniTask.Yield();
        }

        // 3단계: 오른쪽 버튼 1번
        while (canInput && _phase == Phase.FuelInjection3)
        {
            ArduinoInputManager.ButtonId btn = ArduinoInputManager.ButtonId.None;
            if (ArduinoInputManager.Instance != null)
            {
                ArduinoInputManager.Instance.TryConsumeAnyPress(out btn);
            }

            // 1번 눌러야 하므로 1회당 1.1f(100% 이상) 증가
            if (btn == ArduinoInputManager.ButtonId.Button3 || Input.GetKeyDown(KeyCode.RightArrow))
            {
                if (IncreaseFill(_fuel3Image, 1.1f))
                {
                    // LED3 블링크 종료
                    CancelAndDispose(ref _blinkCts);
                    try { ArduinoInputManager.Instance?.SetLed(3, false); }
                    catch (Exception e) { Debug.LogWarning($"[FuelManager] FuelFillAsync-> LED3 끄기 중 예외: {e.Message}"); }

                    StopButtonBlink(buttonRight);

                    _phase = Phase.Done;
                    break;
                }
            }

            await UniTask.Yield();
        }

        // 완료 처리
        if (_phase == Phase.Done)
        {
            try
            {
                _popupFadeCts?.Cancel();
                _blinkCts?.Cancel();
            }
            catch (Exception e)
            {
                Debug.LogError($"[FuelManager] FuelFillAsync-> 토큰 취소 중 예외: {e}");
            }

            _popupFadeCts?.Dispose();
            _popupFadeCts = null;
            _blinkCts?.Dispose();
            _blinkCts = null;

            if (nextSceneBuildIndex >= 0)
            {
                await LoadSceneAsync(nextSceneBuildIndex, new[] { fadeImage1, fadeImage2, fadeImage3 });
            }
            else
            {
                Debug.Log("[FuelManager] FuelFillAsync-> 연료 주입 완료 (다음 씬 미지정)");
            }
        }
    }

    #endregion

    #region Helpers

    /// <summary> 연료 게이지 이미지 초기화 </summary>
    private void InitFuelImage()
    {
        if (stage1Fuel.TryGetComponent(out _fuel1Image))
        {
            _fuel1Image.type = Image.Type.Filled;
            _fuel1Image.fillMethod = Image.FillMethod.Horizontal;
            _fuel1Image.fillOrigin = 0; // Left
            _fuel1Image.fillAmount = 0f;
        }

        if (stage2Fuel.TryGetComponent(out _fuel2Image))
        {
            _fuel2Image.type = Image.Type.Filled;
            _fuel2Image.fillMethod = Image.FillMethod.Horizontal;
            _fuel2Image.fillOrigin = 0;
            _fuel2Image.fillAmount = 0f;
        }

        if (stage3Fuel.TryGetComponent(out _fuel3Image))
        {
            _fuel3Image.type = Image.Type.Filled;
            _fuel3Image.fillMethod = Image.FillMethod.Horizontal;
            _fuel3Image.fillOrigin = 0;
            _fuel3Image.fillAmount = 0f;
        }
    }

    /// <summary> 팝업 이미지를 지정 시간 동안 알파 1->0으로 페이드 </summary>
    private async UniTask PopupFadeAsync(float duration, CancellationToken token)
    {
        if (!popupImage) return;

        Image img = popupImage.GetComponent<Image>();
        if (!img) return;

        SetAlpha(img, 1f);

        float elapsed = 0f;
        while (elapsed < duration && !token.IsCancellationRequested)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            SetAlpha(img, Mathf.Lerp(1f, 0f, t));
            elapsed += Time.deltaTime;
            await UniTask.Yield();
        }

        SetAlpha(img, 0f);

        // 팝업이 사라진 뒤 첫 단계 안내
        SettingTextObject(textGuide, setting.guideText, "1단(연료주입) 버튼을\n누르세요.").Forget();
        StartButtonBlink(buttonLeft);
        if (_blinkCts == null) _blinkCts = new CancellationTokenSource();
        BlinkLedAsync(ledIndex: 1, onMs: 300, offMs: 300, token: _blinkCts.Token).Forget();
    }

    /// <summary> 게이지 증가 (delta만큼), 처음 1.0 도달 시 true 반환 </summary>
    private bool IncreaseFill(Image img, float delta)
    {
        if (!img) return false;
        float before = img.fillAmount;
        img.fillAmount = Mathf.Clamp01(before + delta);
        return (before < 1f && img.fillAmount >= 1f);
    }


    /// <summary> 디버그 스킵: 모든 주입 과정을 중단하고 즉시 다음 씬으로 이동 </summary>
    protected override void OnDebugSkip()
    {
        try
        {
            CancelAndDispose(ref _popupFadeCts);
            CancelAndDispose(ref _blinkCts);
            
            // 시퀀스 토큰 정리
            if (_sequenceCts != null)
            {
                for (int i = 0; i < _sequenceCts.Length; i++)
                    CancelAndDispose(ref _sequenceCts[i]);
            }

            try
            {
                ArduinoInputManager.Instance?.SetLedAll(false);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FuelManager] OnDebugSkip-> LED 전체 끄기 중 예외: {e.Message}");
            }

            StopLedEffects();

            _phase = Phase.Done;

            if (nextSceneBuildIndex >= 0)
            {
                LoadSceneAsync(nextSceneBuildIndex, new[] { fadeImage1, fadeImage2, fadeImage3 }).Forget();
            }
            else
            {
                Debug.Log("[FuelManager] OnDebugSkip-> 다음 씬이 설정되어 있지 않습니다");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[FuelManager] OnDebugSkip-> 예외: {e}");
        }
    }

    #endregion

    #region Sequence Helpers (PingPong / Fix)

    // 특정 시퀀스 인덱스 핑퐁 시작
    private void StartSequencePingPong(int index)
    {
        if (sequences == null || index < 0 || index >= sequences.Length) return;
        if (!sequences[index]) return;

        // 토큰 배열 초기화
        if (_sequenceCts == null || _sequenceCts.Length != sequences.Length)
        {
            _sequenceCts = new CancellationTokenSource[sequences.Length];
        }

        // 기존 동작 취소 후 새로 시작
        CancelAndDispose(ref _sequenceCts[index]);
        _sequenceCts[index] = new CancellationTokenSource();

        // 파라미터: 최소알파 0.28, 최대 1.0, 주기 2초
        StartAlphaPingPong(sequences[index], 0.28f, 1.0f, 2.0f, ref _sequenceCts[index]);
    }

    // 특정 시퀀스 인덱스 핑퐁 중지 및 알파 1로 고정
    private void StopSequencePingPong(int index)
    {
        if (_sequenceCts != null && index >= 0 && index < _sequenceCts.Length)
        {
            CancelAndDispose(ref _sequenceCts[index]);
        }

        if (sequences != null && index >= 0 && index < sequences.Length && sequences[index] != null)
        {
            SetAlpha(sequences[index], 1.0f);
        }
    }

    #endregion
}