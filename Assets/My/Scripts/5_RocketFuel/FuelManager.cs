using System;
using System.Threading;
using System.Threading.Tasks;
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
    public ImageSetting fuelPopup;
    public ImageSetting sub1;

    public ImageSetting[] fuelImage; // 3개 예상
}

/// <summary> 우주발사체의 연료/산화제 씬 관리 매니저 </summary>
public class FuelManager : SceneManager_Base<FuelSetting>
{
    [Header("UI")] 
    [SerializeField] private GameObject mainImage1;
    [SerializeField] private GameObject mainImage2;
    [SerializeField] private GameObject mainImage3;
    [SerializeField] private GameObject popupImage;
    [SerializeField] private GameObject subImage;
    [SerializeField] private GameObject fuelImage1;
    [SerializeField] private GameObject fuelImage2;
    [SerializeField] private GameObject fuelImage3;

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

    protected override void Awake()
    {
        Debug.Log("fuelManager Awake");
        base.Awake();
    }
    private void OnEnable()
    {   
        Debug.Log("fuelManager OnEnable");
        // if (ArduinoInputManager.Instance)
        // {
        //     ArduinoInputManager.Instance.SendButtonDelay(buttonDelayTime);
        // }
    }

    protected override void OnDisable()
    {
        try
        {
            _popupFadeCts?.Cancel();
        }
        catch(Exception e)
        {
            Debug.LogError($"[FuelManager] OnDisable error: {e}");
        }

        _popupFadeCts?.Dispose();
        _popupFadeCts = null;
        
        // if (ArduinoInputManager.Instance)
        // {
        //     ArduinoInputManager.Instance.SendButtonDelay(200);
        // }
    }

    protected override async Task Init()
    {
        _popupFadeTime = Mathf.Max(0f, setting.popupFadeTime);
        _fuelFillSpeed = Mathf.Max(0f, setting.fuelFillSpeed);

        // 고정 이미지 세팅
        SettingImageObject(mainImage1, setting.main1);
        SettingImageObject(mainImage2, setting.main2);
        SettingImageObject(mainImage3, setting.main3);
        SettingImageObject(popupImage, setting.fuelPopup);
        SettingImageObject(subImage, setting.sub1);

        // 게이지 이미지 세팅
        SettingImageObject(fuelImage1, setting.fuelImage[0]);
        SettingImageObject(fuelImage2, setting.fuelImage[1]);
        SettingImageObject(fuelImage3, setting.fuelImage[2]);

        InitFuelImage(); // fillAmount 0으로 초기화
        
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage2, fadeImage3 });
        
        _phase = Phase.FuelInjection1;
        await FuelFillAsync(); // 시작
    }

    /// <summary> 단계별 입력/증가 루프를 비동기로 진행 </summary>
    private async Task FuelFillAsync()
    {
        /*if (!ArduinoInputManager.Instance)
        {
            Debug.LogError("[FuelManager] RunFlowAsync: ArduinoInputManager is null");
            return;
        }*/
            
        // 1단계: ← 키
        while (canInput && _phase == Phase.FuelInjection1)
        {
            Debug.Log("Press left");
            // 첫 KeyDown 시 팝업 페이드 아웃
            if (/*(ArduinoInputManager.Instance.TryConsumeAnyPress(out ArduinoInputManager.ButtonId btn) &&
                 btn == ArduinoInputManager.ButtonId.Button1) ||*/
                Input.GetKey(KeyCode.LeftArrow))
            {
                if (_popupFadeCts == null)
                {
                    _popupFadeCts = new CancellationTokenSource();
                    _ = PopupFadeAsync(_popupFadeTime, _popupFadeCts.Token);
                }
            }

            if (/*btn == ArduinoInputManager.ButtonId.Button1 ||*/ Input.GetKeyDown(KeyCode.LeftArrow))
            {
                if (IncreaseFill(_fuel1Image, _fuelFillSpeed * Time.deltaTime))
                {
                    _phase = Phase.FuelInjection2;
                    break;
                }
            }
            
            //ArduinoInputManager.Instance.FlushAll();
            await Task.Yield();
        }

        // 2단계: ↓ 키
        while (canInput && _phase == Phase.FuelInjection2)
        {   
            Debug.Log("Press Down");
            if (/*(ArduinoInputManager.Instance.TryConsumeAnyPress(out ArduinoInputManager.ButtonId btn) &&
                 btn == ArduinoInputManager.ButtonId.Button2) ||*/
                Input.GetKey(KeyCode.DownArrow))
            {
                if (IncreaseFill(_fuel2Image, _fuelFillSpeed * Time.deltaTime))
                {
                    _phase = Phase.FuelInjection3;
                    break;
                }
            }

            await Task.Yield();
        }

        // 3단계: → 키
        while (canInput && _phase == Phase.FuelInjection3)
        {   
            Debug.Log("Press Right");
            if (/*(ArduinoInputManager.Instance.TryConsumeAnyPress(out ArduinoInputManager.ButtonId btn) &&
                 btn == ArduinoInputManager.ButtonId.Button3) ||*/
                Input.GetKey(KeyCode.RightArrow))
            {
                if (IncreaseFill(_fuel3Image, _fuelFillSpeed * Time.deltaTime))
                {
                    _phase = Phase.Done;
                    break;
                }
            }

            await Task.Yield();
        }

        // 완료 처리
        if (_phase == Phase.Done)
        {   
            // 작업 중인 팝업 페이드 태스크 취소
            try
            {
                _popupFadeCts?.Cancel();
            }
            catch(Exception e)
            {
                Debug.LogError($"[FuelManager] PopupFade Cancel error: {e}");
            }
            
            // 토큰 메모리/리소스 정리
            _popupFadeCts?.Dispose();
            _popupFadeCts = null;

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
        if (fuelImage1.TryGetComponent(out _fuel1Image))
        {
            _fuel1Image.type = Image.Type.Filled;
            _fuel1Image.fillMethod = Image.FillMethod.Horizontal;
            _fuel1Image.fillOrigin = 0; // Left
            _fuel1Image.fillAmount = 0f;
        }

        if (fuelImage2.TryGetComponent(out _fuel2Image))
        {
            _fuel2Image.type = Image.Type.Filled;
            _fuel2Image.fillMethod = Image.FillMethod.Horizontal;
            _fuel2Image.fillOrigin = 0;
            _fuel2Image.fillAmount = 0f;
        }

        if (fuelImage3.TryGetComponent(out _fuel3Image))
        {
            _fuel3Image.type = Image.Type.Filled;
            _fuel3Image.fillMethod = Image.FillMethod.Horizontal;
            _fuel3Image.fillOrigin = 0;
            _fuel3Image.fillAmount = 0f;
        }
    }

    /// <summary> 팝업 이미지를 지정 시간 동안 서서히 알파 1->0으로 </summary>
    private async Task PopupFadeAsync(float duration, CancellationToken token)
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
            await Task.Yield();
        }

        SetAlpha(img, 0f);
    }

    /// <summary> 게이지 증가 (delta만큼), 처음 1.0 도달 시 true 반환 </summary>
    private bool IncreaseFill(Image img, float delta)
    {
        if (!img) return false;
        float before = img.fillAmount;
        img.fillAmount = Mathf.Clamp01(before + delta);
        return (before < 1f && img.fillAmount >= 1f);
    }
}