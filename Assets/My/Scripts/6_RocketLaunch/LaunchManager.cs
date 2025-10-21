using System;
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
    public ImageSetting sub1;
    
    public ImageSetting[] stages;
    public ImageSetting[] main1Children;
    public ImageSetting[] fuelImage;
}

public class LaunchManager : SceneManager_Base<LaunchSetting>
{   
    public static LaunchManager Instance;
    
    [Header("UI")]
    [SerializeField] private GameObject mainImage1;
    [SerializeField] private GameObject mainImage2;
    [SerializeField] private GameObject mainImage3;
    [SerializeField] private GameObject subImage;
    [SerializeField] private GameObject fuelImage1;
    [SerializeField] private GameObject fuelImage2;
    [SerializeField] private GameObject fuelImage3;
    [SerializeField] private GameObject countdownText;
    
    [Header("mainImage1")]
    [SerializeField] private Image[] stages;
    [SerializeField] private Image[] main1ChildrenImages;

    [Header("Rocket")]
    [SerializeField] private GameObject rocketVFX;

    protected override string JsonPath => "JSON/LaunchSetting.json";

    private int _rocketCountdown;
    private RocketLaunch _rocketLaunch;
    private CancellationTokenSource[] _alphaCts;
    private CancellationTokenSource[] _stageCts;

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
    }
    
    protected override async UniTask Init()
    {
        SettingImageObject(mainImage1, setting.main1);
        SettingImageObject(mainImage2, setting.main2);
        SettingImageObject(mainImage3, setting.main3);
        SettingImageObject(subImage,  setting.sub1);
        
        // stage 이미지 세팅
        if (setting.stages != null && stages != null)
        {
            int count = Mathf.Min(setting.stages.Length, stages.Length);
            for (int i = 0; i < count; i++)
            {
                if (stages[i] == null) continue;
                SettingImageObject(stages[i].gameObject, setting.stages[i]);
            }
        }
        
        // mainImage1의 자식 이미지들 세팅
        if (setting.main1Children != null && main1ChildrenImages != null)
        {
            int count = Mathf.Min(setting.main1Children.Length, main1ChildrenImages.Length);
            for (int i = 0; i < count; i++)
            {
                if (main1ChildrenImages[i] == null) continue;
                SettingImageObject(main1ChildrenImages[i].gameObject, setting.main1Children[i]);
            }
        }
        
        if (main1ChildrenImages != null)
        {
            _alphaCts = new CancellationTokenSource[main1ChildrenImages.Length];
        }
        
        if (stages != null)
        {
            _stageCts = new CancellationTokenSource[stages.Length];
        }

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

        // 입력 대기
        CancellationToken cancel = this.GetCancellationTokenOnDestroy();
        while (!cancel.IsCancellationRequested && isActiveAndEnabled)
        {
            if ((ArduinoInputManager.Instance && ArduinoInputManager.Instance.TryConsumeAnyPress(out _))
                || TryConsumeSingleInput())
            {   
                StopLedEffects();
                ArduinoInputManager.Instance?.SetLedAll(false);
                LedStrip.Range(0, 9, 255, 0, 0);
        
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
            Debug.LogError("[LaunchManager] rocketVFX not assigned or missing RocketLaunch component");
        }
        
        // 카운트다운 시작
        RunCountdownAsync().Forget();
        CountController.Instance?.RunCountdownAsync().Forget();
    }

    /// <summary> 숫자를 갱신하고, 각 숫자마다 알파를 1 -> 0으로 부드럽게 페이드 </summary>
    private async UniTask RunCountdownAsync()
    {
        if (!countdownText || !countdownText.TryGetComponent(out TextMeshProUGUI tmp)) return;

        CancellationToken cancel = this.GetCancellationTokenOnDestroy();
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
        if (main1ChildrenImages == null) return false;
        if (index < 0 || index >= main1ChildrenImages.Length) return false;
        if (main1ChildrenImages[index] == null) return false;
        g = main1ChildrenImages[index];
        return true;
    }

    /// <summary> 특정 인덱스의 핑퐁을 중지하고 알파를 고정값으로 설정 </summary>
    private void StopPingPongAndSetAlpha(int index, float alpha)
    {
        if (_alphaCts != null && index >= 0 && index < _alphaCts.Length)
        {
            CancelAndDispose(ref _alphaCts[index]);
        }

        Graphic g;
        if (TryGetChildGraphic(index, out g))
        {
            SetAlpha(g, alpha);
        }
    }

    /// <summary> 특정 인덱스의 핑퐁 시작(기존 실행 중이면 교체) </summary>
    private void StartPingPongAt(int index, float minA, float maxA, float periodSec)
    {
        if (main1ChildrenImages == null) return;
        if (index < 0 || index >= main1ChildrenImages.Length) return;
        if (main1ChildrenImages[index] == null) return;

        // 배열 초기화
        if (_alphaCts == null || _alphaCts.Length != main1ChildrenImages.Length)
        {
            int len = main1ChildrenImages.Length;
            _alphaCts = new CancellationTokenSource[len];
        }
        if (_alphaCts[index] != null)
        {
            CancelAndDispose(ref _alphaCts[index]);
        }
        
        // 시작
        StartAlphaPingPong(main1ChildrenImages[index], minA, maxA, periodSec, ref _alphaCts[index]);
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
            ? _stageCts[index].Token
            : this.GetCancellationTokenOnDestroy();

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
}
