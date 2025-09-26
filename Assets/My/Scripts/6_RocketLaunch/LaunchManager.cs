using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

[Serializable]
public class LaunchSetting
{
    public int rocketCountdown;
    public ImageSetting main1;
    public ImageSetting main2;
    public ImageSetting main3;
    public ImageSetting sub1;
}

public class LaunchManager : SceneManager_Base<LaunchSetting>
{   
    public static LaunchManager Instance;
    
    [Header("UI")]
    [SerializeField] private GameObject mainImage1;
    [SerializeField] private GameObject mainImage2;
    [SerializeField] private GameObject mainImage3;
    [SerializeField] private GameObject subImage;
    [SerializeField] private GameObject countdownText;

    [Header("Rocket")]
    [SerializeField] private GameObject rocketVFX;

    protected override string JsonPath => "JSON/LaunchSetting.json";

    private int _rocketCountdown;
    private RocketLaunch _rocketLaunch;

    protected override void Awake()
    {
        base.Awake();

        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(this);
    }
    
    protected override async Task Init()
    {
        SettingImageObject(mainImage1, setting.main1);
        SettingImageObject(mainImage2, setting.main2);
        SettingImageObject(mainImage3, setting.main3);
        SettingImageObject(subImage,  setting.sub1);

        _rocketCountdown = Mathf.Max(1, setting.rocketCountdown);
        if (countdownText && countdownText.TryGetComponent(out TextMeshProUGUI tmp))
        {
            tmp.text = _rocketCountdown.ToString();
            SetAlpha(tmp, 0f);
        }
        
        ArduinoInputManager.Instance?.SetLedAll(true);
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage2, fadeImage3 });

        // 입력 대기
        while (true)
        {
            if ((ArduinoInputManager.Instance && ArduinoInputManager.Instance.TryConsumeAnyPress(out _))
                || TryConsumeSingleInput())
            {   
                ArduinoInputManager.Instance?.SetLedAll(false);
                break;
            }
            
            
            await Task.Yield();
        }
       
        if (rocketVFX.TryGetComponent(out _rocketLaunch))
        {
            _rocketLaunch.Call();    
        }
        else
        {
            Debug.LogError("[LaunchManager] Failed to _rocketLaunch.Call");
        }
        
        // 카운트다운 시작
        await RunCountdownAsync();
    }

    /// <summary> 숫자를 갱신하고, 각 숫자마다 알파를 1 -> 0으로 부드럽게 페이드 </summary>
    private async Task RunCountdownAsync()
    {
        if (!countdownText || !countdownText.TryGetComponent(out TextMeshProUGUI tmp)) 
            return;

        // 안전장치
        float duration = Mathf.Max(0.01f, 1.0f);

        for (int n = _rocketCountdown; n > 0; n--)
        {
            // 숫자 갱신 및 완전 표시
            tmp.text = n.ToString();
            SetAlpha(tmp, 1f);

            // 알파 1 -> 0 페이드
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float a = 1f - Mathf.Clamp01(t / duration);
                SetAlpha(tmp, a);
                await Task.Yield();
            }

            // 다음 숫자 전환 직전 완전 투명 보장
            SetAlpha(tmp, 0f);
        }
    }

    public async Task LoadNextSceneAsync()
    {
        int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 0;
        await LoadSceneAsync(target, new[] { fadeImage1, fadeImage2, fadeImage3 });
    }
}
