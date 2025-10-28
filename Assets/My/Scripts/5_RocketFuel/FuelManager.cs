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
public class FuelManager : SceneManager_Base<FuelSetting>
{
    [Header("UI")] 
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
    [SerializeField] private Image[] sequences;
    [SerializeField] private GameObject textObjective;
    
    [Header("mainImage2")]
    [SerializeField] private GameObject controllerBackground;
    [SerializeField] private GameObject buttonLeft;
    [SerializeField] private GameObject buttonMiddle;
    [SerializeField] private GameObject buttonRight;
    [SerializeField] private GameObject throttleBackground;
    [SerializeField] private GameObject throttleButton;
    [SerializeField] private GameObject textGuide;


    protected override string JsonPath => "JSON/FuelSetting.json";

    private float _popupFadeTime, _fuelFillSpeed;
    private Image _fuel1Image, _fuel2Image, _fuel3Image;

    private enum Phase
    {
        RocketMove,
        FuelInjection1,
        FuelInjection2,
        FuelInjection3,
        Done
    }

    private Phase _phase = Phase.RocketMove;
    private CancellationTokenSource _popupFadeCts;
    private CancellationTokenSource _blinkCts;
    private CancellationTokenSource _main1AlphaCts;

    protected override void OnDisable()
    {
        try
        {
            _popupFadeCts?.Cancel();
            _blinkCts?.Cancel();
            _main1AlphaCts?.Cancel();
            CancelAndDispose(ref _popupFadeCts);
            
            ArduinoInputManager.Instance?.SetLedAll(false);
        }
        catch (Exception e)
        {
            Debug.LogError($"[FuelManager] OnDisable error: {e}");
        }

        _popupFadeCts?.Dispose();
        _popupFadeCts = null;
        _blinkCts?.Dispose();
        _blinkCts = null;
        _main1AlphaCts?.Dispose();
        _main1AlphaCts = null;
        
    }

    protected override async UniTask Init()
    {
        _popupFadeTime = Mathf.Max(0f, setting.popupFadeTime);
        _fuelFillSpeed = Mathf.Max(0f, setting.fuelFillSpeed);

        // 고정 이미지 세팅
        SettingImageObject(mainImage1, setting.main1);
        SettingImageObject(mainImage2, setting.main2);
        SettingImageObject(mainImage3, setting.main3);
        SettingImageObject(popupImage, setting.fuelPopup);
        SettingImageObject(subBgImage, setting.subBg);
        SettingImageObject(subRocketImage, setting.subRocket);
        
        // ===== mainImage1 =====
        if (setting.main1Children != null && sequences != null)
        {
            int count = Mathf.Min(setting.main1Children.Length, sequences.Length);
            for (int i = 0; i < count; i++)
            {
                if (sequences[i] == null) continue;
                SettingImageObject(sequences[i].gameObject, setting.main1Children[i]);
            }
        }
        
        if (sequences != null && sequences.Length > 1 && sequences[1] != null)
        {
            StartAlphaPingPong(sequences[1], 0.28f, 1.0f, 2.0f, ref _main1AlphaCts);
        }
        SettingTextObject(textObjective, setting.objectiveText, "연료를 주입하세요").Forget();
        // ======================
        // ===== mainImage2 =====
        SettingImageObject(controllerBackground, setting.controllerBackground);
        SettingImageObject(buttonLeft, setting.buttonLeft);
        SettingImageObject(buttonMiddle, setting.buttonMiddle);
        SettingImageObject(buttonRight, setting.buttonRight);
        SettingImageObject(throttleBackground, setting.throttleBackground);
        SettingImageObject(throttleButton, setting.throttleButton);
        SettingTextObject(textGuide, setting.guideText, "아무 버튼을 누르세요").Forget();
        // ======================
        
        // 산화제 이미지 세팅
        SettingImageObject(stage1Oxidizer, setting.oxidizers[0]);
        SettingImageObject(stage2Oxidizer, setting.oxidizers[1]);
        SettingImageObject(stage3Oxidizer, setting.oxidizers[2]);
        
        // 연료 이미지 세팅
        SettingImageObject(stage1Fuel, setting.fuels[0]);
        SettingImageObject(stage2Fuel, setting.fuels[1]);
        SettingImageObject(stage3Fuel, setting.fuels[2]);

        InitFuelImage(); // fillAmount 0으로 초기화
        ArduinoInputManager.Instance?.SetLedAll(true);
        SetButtonsOn(buttonLeft, buttonMiddle, buttonRight);

        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage2, fadeImage3 });
        StartBlinkGreenAsync(500, 160);
        
        _phase = Phase.FuelInjection1;
        _blinkCts = new CancellationTokenSource();

        await FuelFillAsync(); // 시작
    }

    /// <summary> 단계별 입력/증가 루프를 비동기로 진행 </summary>
    private async UniTask FuelFillAsync()
    {
        // 1단계: ← 키
        while (canInput && _phase == Phase.FuelInjection1)
        {
            // 첫 KeyDown 시 팝업 페이드 아웃
            if (ArduinoInputManager.Instance.TryConsumeAnyPress(out ArduinoInputManager.ButtonId btn)  || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                if (_popupFadeCts == null)
                {
                    _popupFadeCts = new CancellationTokenSource();
                    ArduinoInputManager.Instance.SetLedAll(false);
                    SetButtonsOff(buttonLeft, buttonMiddle, buttonRight);
                    PopupFadeAsync(_popupFadeTime, _popupFadeCts.Token).Forget();
                }
            }

            if (btn == ArduinoInputManager.ButtonId.Button1 || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                if (IncreaseFill(_fuel1Image, _fuelFillSpeed * Time.deltaTime))
                {
                    // LED1 블링크 종료
                    _blinkCts?.Cancel(); _blinkCts?.Dispose(); _blinkCts = null;
                    ArduinoInputManager.Instance.SetLed(1, false);
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

        // 2단계: ↓ 키
        while (canInput && _phase == Phase.FuelInjection2)
        {
            if ((ArduinoInputManager.Instance.TryConsumeAnyPress(out ArduinoInputManager.ButtonId btn) &&
                 btn == ArduinoInputManager.ButtonId.Button2) ||
                Input.GetKeyDown(KeyCode.DownArrow))
            {
                if (IncreaseFill(_fuel2Image, _fuelFillSpeed * Time.deltaTime))
                {
                    // LED2 블링크 종료
                    _blinkCts?.Cancel(); _blinkCts?.Dispose(); _blinkCts = null;
                    ArduinoInputManager.Instance.SetLed(2, false);
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

        // 3단계: → 키
        while (canInput && _phase == Phase.FuelInjection3)
        {
            if ((ArduinoInputManager.Instance.TryConsumeAnyPress(out ArduinoInputManager.ButtonId btn) &&
                 btn == ArduinoInputManager.ButtonId.Button3) ||
                Input.GetKeyDown(KeyCode.RightArrow))
            {
                if (IncreaseFill(_fuel3Image, _fuelFillSpeed * Time.deltaTime))
                {   
                    // LED3 블링크 종료
                    _blinkCts?.Cancel(); _blinkCts?.Dispose(); _blinkCts = null;
                    ArduinoInputManager.Instance.SetLed(3, false);
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
            // 작업 중인 팝업 페이드 태스크 취소
            try
            {
                _popupFadeCts?.Cancel();
                _blinkCts?.Cancel();
            }
            catch (Exception e)
            {
                Debug.LogError($"[FuelManager] PopupFade Cancel error: {e}");
            }

            // 토큰 메모리/리소스 정리
            _popupFadeCts?.Dispose();
            _popupFadeCts = null;
            _blinkCts?.Dispose();
            _blinkCts = null;

            // 다음 씬 지정 시 전환
            if (nextSceneBuildIndex >= 0)
            {
                await LoadSceneAsync(nextSceneBuildIndex, new[] { fadeImage1, fadeImage2, fadeImage3 });
            }
            else
            {
                Debug.Log("[FuelManager] Fuel fill completed (no next scene set).");
            }
        }
    }

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

    /// <summary> 팝업 이미지를 지정 시간 동안 서서히 알파 1->0으로 </summary>
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

        // 팝업이 모두 사라지고 버튼 1 블링크 and 왼쪽 버튼 이미지 ON
        SettingTextObject(textGuide, setting.guideText, "왼쪽 버튼을 누르세요").Forget();
        SetButtonOn(buttonLeft);
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
    
    /// <summary> LED 블링크 메서드 </summary>
    private async UniTask BlinkLedAsync(int ledIndex, int onMs, int offMs, CancellationToken token)
    {
        var mgr = ArduinoInputManager.Instance;
        if (mgr == null) return;

        try
        {
            while (!token.IsCancellationRequested)
            {
                mgr.SetLed(ledIndex, true);
                try { await UniTask.Delay(onMs, cancellationToken: token); } catch { break; }

                mgr.SetLed(ledIndex, false);
                try { await UniTask.Delay(offMs, cancellationToken: token); } catch { break; }
            }
        }
        finally
        {
            mgr.SetLed(ledIndex, false); // 종료 시 확실히 꺼줌
        }
    }
    
    /// <summary>
    /// 디버그 스킵 입력 처리
    /// - 모든 연료 주입 과정을 중단하고 즉시 다음 씬으로 이동
    /// </summary>
    protected override void OnDebugSkip()
    {
        try
        {
            // 진행 중 태스크 모두 취소
            _popupFadeCts?.Cancel();
            _blinkCts?.Cancel();
            _main1AlphaCts?.Cancel();

            // LED 및 효과 정리
            ArduinoInputManager.Instance.SetLedAll(false);
            StopLedEffects();

            // 상태 정리
            _phase = Phase.Done;

            // 다음 씬 전환
            if (nextSceneBuildIndex >= 0)
            {
                LoadSceneAsync(nextSceneBuildIndex, new[] { fadeImage1, fadeImage2, fadeImage3 }).Forget();
            }
            else
            {
                Debug.Log("[FuelManager] Debug skip pressed, but nextSceneBuildIndex is not set.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[FuelManager] OnDebugSkip Exception: {e}");
        }
    }
}