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

    public ImageSetting main1;
    public ImageSetting main2;
    public ImageSetting main3;
    
    public ImageSetting[] main1Children;
    
    public ImageSetting fuelPopup;
    public ImageSetting subBg;
    public ImageSetting subRocket;
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
}

/// <summary> 우주발사체의 연료/산화제 씬 관리 매니저 </summary>
public sealed class FuelManager : SceneManager_Base<FuelSetting>
{
    // ===== JSON 경로 =====
    protected override string JsonPath => "JSON/FuelSetting.json";

    #region Serialized Refs

    [Header("UI")] 
    [SerializeField] private GameObject backgroundImage;
    [SerializeField] private GameObject mainImage1;
    [SerializeField] private GameObject mainImage2;
    [SerializeField] private GameObject mainImage3;
    [SerializeField] private GameObject popupImage;
    [SerializeField] private GameObject subBgImage;
    [SerializeField] private GameObject subRocketImage;
    
    [Header("Oxidizers")]
    [SerializeField] private GameObject stage1Oxidizer;
    [SerializeField] private GameObject stage2Oxidizer;
    [SerializeField] private GameObject stage3Oxidizer;
    
    [Header("Fuels")]
    [SerializeField] private GameObject stage1Fuel;
    [SerializeField] private GameObject stage2Fuel;
    [SerializeField] private GameObject stage3Fuel;
    
    [Header("mainImage1")]
    [SerializeField] private Image[] sequences;              // 메인1 하위 단계별 강조 이미지들
    [SerializeField] private GameObject textObjective;       // 목표 텍스트
    
    [Header("mainImage2")]
    [SerializeField] private GameObject controllerBackground;
    [SerializeField] private GameObject buttonLeft;
    [SerializeField] private GameObject buttonMiddle;
    [SerializeField] private GameObject buttonRight;
    [SerializeField] private GameObject throttleBackground;
    [SerializeField] private GameObject throttleButton;
    [SerializeField] private GameObject textGuide;

    #endregion

    #region Settings / State

    // private
    private CancellationTokenSource _blinkCts;   // LED 블링크 토큰
    private CancellationTokenSource _main1AlphaCts; // 메인1 강조 이미지 알파 핑퐁 토큰
    private CancellationTokenSource _popupFadeCts;  // 팝업 페이드 토큰

    private float _fuelFillSpeed;  // 연료 증가 속도 (fillAmount/sec)
    private float _popupFadeTime;  // 팝업 사라지는 시간

    private Image _fuel1Image;     // 1단계 연료 이미지 (Filled)
    private Image _fuel2Image;     // 2단계 연료 이미지 (Filled)
    private Image _fuel3Image;     // 3단계 연료 이미지 (Filled)

    private enum Phase { RocketMove, FuelInjection1, FuelInjection2, FuelInjection3, Done }
    private Phase _phase = Phase.RocketMove; // 현재 단계

    #endregion

    #region Unity Life-Cycle

    /// <summary> 비활성화 시 토큰/LED 정리 </summary>
    protected override void OnDisable()
    {
        try
        {   
            CancelAndDispose(ref _blinkCts);
            CancelAndDispose(ref _main1AlphaCts);
            CancelAndDispose(ref _popupFadeCts);
            
            ArduinoInputManager.Instance?.SetLedAll(false);
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
        try { SettingImageObject(backgroundImage, setting.main1); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> backgroundImage 세팅 중 예외: {e.Message}"); }
        try { SettingImageObject(mainImage1, setting.main1); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> mainImage1 세팅 중 예외: {e.Message}"); }
        try { SettingImageObject(mainImage2, setting.main2); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> mainImage2 세팅 중 예외: {e.Message}"); }
        try { SettingImageObject(mainImage3, setting.main3); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> mainImage3 세팅 중 예외: {e.Message}"); }
        try { SettingImageObject(popupImage, setting.fuelPopup); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> popupImage 세팅 중 예외: {e.Message}"); }
        try { SettingImageObject(subBgImage, setting.subBg); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> subBgImage 세팅 중 예외: {e.Message}"); }
        try { SettingImageObject(subRocketImage, setting.subRocket); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> subRocketImage 세팅 중 예외: {e.Message}"); }
        
        // ===== mainImage1 =====
        if (setting.main1Children != null && sequences != null)
        {
            int count = Mathf.Min(setting.main1Children.Length, sequences.Length);
            for (int i = 0; i < count; i++)
            {
                if (sequences[i] == null) continue;
                try { SettingImageObject(sequences[i].gameObject, setting.main1Children[i]); }
                catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> sequences[{i}] 세팅 중 예외: {e.Message}"); }
            }
        }
        if (sequences != null && sequences.Length > 1 && sequences[1] != null)
        {
            StartAlphaPingPong(sequences[1], 0.28f, 1.0f, 2.0f, ref _main1AlphaCts);
        }
        SettingTextObject(textObjective, setting.objectiveText, "연료를 주입하세요").Forget();
        
        // ===== mainImage2 =====
        try { SettingImageObject(controllerBackground, setting.controllerBackground); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> controllerBackground 세팅 중 예외: {e.Message}"); }
        try { SettingImageObject(buttonLeft, setting.buttonLeft); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> buttonLeft 세팅 중 예외: {e.Message}"); }
        try { SettingImageObject(buttonMiddle, setting.buttonMiddle); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> buttonMiddle 세팅 중 예외: {e.Message}"); }
        try { SettingImageObject(buttonRight, setting.buttonRight); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> buttonRight 세팅 중 예외: {e.Message}"); }
        try { SettingImageObject(throttleBackground, setting.throttleBackground); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> throttleBackground 세팅 중 예외: {e.Message}"); }
        try { SettingImageObject(throttleButton, setting.throttleButton); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> throttleButton 세팅 중 예외: {e.Message}"); }
        SettingTextObject(textGuide, setting.guideText, "아무 버튼을 누르세요").Forget();
        
        // 산화제 이미지 세팅
        try { SettingImageObject(stage1Oxidizer, setting.oxidizers[0]); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> stage1Oxidizer 세팅 중 예외: {e.Message}"); }
        try { SettingImageObject(stage2Oxidizer, setting.oxidizers[1]); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> stage2Oxidizer 세팅 중 예외: {e.Message}"); }
        try { SettingImageObject(stage3Oxidizer, setting.oxidizers[2]); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> stage3Oxidizer 세팅 중 예외: {e.Message}"); }
        
        // 연료 이미지 세팅
        try { SettingImageObject(stage1Fuel, setting.fuels[0]); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> stage1Fuel 세팅 중 예외: {e.Message}"); }
        try { SettingImageObject(stage2Fuel, setting.fuels[1]); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> stage2Fuel 세팅 중 예외: {e.Message}"); }
        try { SettingImageObject(stage3Fuel, setting.fuels[2]); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> stage3Fuel 세팅 중 예외: {e.Message}"); }

        InitFuelImage(); // fillAmount 0으로 초기화

        try { ArduinoInputManager.Instance?.SetLedAll(true); } catch (Exception e) { Debug.LogWarning($"[FuelManager] Init-> LED 전체 켜기 중 예외: {e.Message}"); }
        SetButtonsOn(buttonLeft, buttonMiddle, buttonRight);

        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage2, fadeImage3 });
        StartBlinkGreenAsync(500, 160);
        
        _phase = Phase.FuelInjection1;
        _blinkCts = new CancellationTokenSource();

        await FuelFillAsync(); // 시작
    }

    #endregion

    #region Fuel Flow

    /// <summary> 단계별 입력/증가 루프를 비동기로 진행 </summary>
    private async UniTask FuelFillAsync()
    {
        // 1단계: 왼쪽 버튼/LeftArrow
        while (canInput && _phase == Phase.FuelInjection1)
        {
            ArduinoInputManager.ButtonId btn = ArduinoInputManager.ButtonId.None;
            if (ArduinoInputManager.Instance != null)
            {
                ArduinoInputManager.Instance.TryConsumeAnyPress(out btn);
            }

            // 첫 입력 시 팝업 페이드 아웃 시작
            if (btn != ArduinoInputManager.ButtonId.None || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                if (_popupFadeCts == null)
                {
                    _popupFadeCts = new CancellationTokenSource();
                    try { ArduinoInputManager.Instance?.SetLedAll(false); } catch (Exception e) { Debug.LogWarning($"[FuelManager] FuelFillAsync-> LED 전체 끄기 중 예외: {e.Message}"); }
                    SetButtonsOff(buttonLeft, buttonMiddle, buttonRight);
                    PopupFadeAsync(_popupFadeTime, _popupFadeCts.Token).Forget();
                }
            }

            if (btn == ArduinoInputManager.ButtonId.Button1 || Input.GetKey(KeyCode.LeftArrow))
            {
                if (IncreaseFill(_fuel1Image, _fuelFillSpeed * Time.deltaTime))
                {
                    // LED1 블링크 종료
                    CancelAndDispose(ref _blinkCts);
                    try { ArduinoInputManager.Instance?.SetLed(1, false); } catch (Exception e) { Debug.LogWarning($"[FuelManager] FuelFillAsync-> LED1 끄기 중 예외: {e.Message}"); }
                    SetButtonOff(buttonLeft);

                    // LED2 블링크 시작
                    _blinkCts = new CancellationTokenSource();
                    SettingTextObject(textGuide, setting.guideText, "가운데 버튼을 누르세요").Forget();
                    BlinkLedAsync(2, 300, 300, _blinkCts.Token).Forget();
                    SetButtonOn(buttonMiddle);
                    
                    _phase = Phase.FuelInjection2;
                    break;
                }
            }

            await UniTask.Yield();
        }

        // 2단계: 가운데 버튼/DownArrow
        while (canInput && _phase == Phase.FuelInjection2)
        {
            ArduinoInputManager.ButtonId btn = ArduinoInputManager.ButtonId.None;
            if (ArduinoInputManager.Instance != null)
            {
                ArduinoInputManager.Instance.TryConsumeAnyPress(out btn);
            }

            if (btn == ArduinoInputManager.ButtonId.Button2 || Input.GetKey(KeyCode.DownArrow))
            {
                if (IncreaseFill(_fuel2Image, _fuelFillSpeed * Time.deltaTime))
                {
                    // LED2 블링크 종료
                    CancelAndDispose(ref _blinkCts);
                    try { ArduinoInputManager.Instance?.SetLed(2, false); } catch (Exception e) { Debug.LogWarning($"[FuelManager] FuelFillAsync-> LED2 끄기 중 예외: {e.Message}"); }
                    SetButtonOff(buttonMiddle);

                    // LED3 블링크 시작
                    _blinkCts = new CancellationTokenSource();
                    SettingTextObject(textGuide, setting.guideText, "오른쪽 버튼을 누르세요").Forget();
                    BlinkLedAsync(3, 300, 300, _blinkCts.Token).Forget();
                    SetButtonOn(buttonRight);
                    
                    _phase = Phase.FuelInjection3;
                }
            }

            await UniTask.Yield();
        }

        // 3단계: 오른쪽 버튼/RightArrow
        while (canInput && _phase == Phase.FuelInjection3)
        {
            ArduinoInputManager.ButtonId btn = ArduinoInputManager.ButtonId.None;
            if (ArduinoInputManager.Instance != null)
            {
                ArduinoInputManager.Instance.TryConsumeAnyPress(out btn);
            }

            if (btn == ArduinoInputManager.ButtonId.Button3 || Input.GetKey(KeyCode.RightArrow))
            {
                if (IncreaseFill(_fuel3Image, _fuelFillSpeed * Time.deltaTime))
                {   
                    // LED3 블링크 종료
                    CancelAndDispose(ref _blinkCts);
                    try { ArduinoInputManager.Instance?.SetLed(3, false); } catch (Exception e) { Debug.LogWarning($"[FuelManager] FuelFillAsync-> LED3 끄기 중 예외: {e.Message}"); }
                    SetButtonOff(buttonRight);
                    
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

            _popupFadeCts?.Dispose(); _popupFadeCts = null;
            _blinkCts?.Dispose(); _blinkCts = null;

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
        SettingTextObject(textGuide, setting.guideText, "왼쪽 버튼을 누르세요").Forget();
        SetButtonOn(buttonLeft);
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
    
    /// <summary> LED 블링크 </summary>
    private async UniTask BlinkLedAsync(int ledIndex, int onMs, int offMs, CancellationToken token)
    {
        ArduinoInputManager mgr = ArduinoInputManager.Instance;
        if (mgr == null) return;

        try
        {
            while (!token.IsCancellationRequested)
            {
                mgr.SetLed(ledIndex, true);
                try { await UniTask.Delay(onMs, cancellationToken: token); }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    Debug.LogWarning($"[FuelManager] BlinkLedAsync-> 대기 중 예외(켜짐 구간): {e.Message}");
                }

                mgr.SetLed(ledIndex, false);
                try { await UniTask.Delay(offMs, cancellationToken: token); }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    Debug.LogWarning($"[FuelManager] BlinkLedAsync-> 대기 중 예외(꺼짐 구간): {e.Message}");
                }
            }
        }
        finally
        {
            try { mgr.SetLed(ledIndex, false); }
            catch (Exception e) { Debug.LogWarning($"[FuelManager] BlinkLedAsync-> LED 종료 처리 중 예외: {e.Message}"); }
        }
    }
    
    /// <summary> 디버그 스킵: 모든 주입 과정을 중단하고 즉시 다음 씬으로 이동 </summary>
    protected override void OnDebugSkip()
    {
        try
        {
            CancelAndDispose(ref _popupFadeCts);
            CancelAndDispose(ref _blinkCts);
            CancelAndDispose(ref _main1AlphaCts);

            try { ArduinoInputManager.Instance?.SetLedAll(false); } catch (Exception e) { Debug.LogWarning($"[FuelManager] OnDebugSkip-> LED 전체 끄기 중 예외: {e.Message}"); }
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
}
