using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class RecycleSetting
{
    public float popupFadeTime;
    public float gameCloseTime;
    public ImageSetting main1;
    public ImageSetting main2;
    public ImageSetting main3;
    public ImageSetting popup1;
    public ImageSetting endBackground;
    public ImageSetting endImage1;
    public ImageSetting endImage2;
    
    public ImageSetting[] main1Children;
}

/// <summary> 발사체 회수 팝업을 띄우고 체험을 종료한다. </summary>
public class RecycleManager :  SceneManager_Base<RecycleSetting>
{
    [Header("UI")] 
    [SerializeField] private GameObject mainImage1;
    [SerializeField] private GameObject mainImage2;
    [SerializeField] private GameObject mainImage3;
    [SerializeField] private GameObject popupImage1;
    [SerializeField] private GameObject endBackgroundImage;
    [SerializeField] private GameObject endImage1;
    [SerializeField] private GameObject endImage2;

    [Header("mainImage1")]
    [SerializeField] private Image[] main1ChildrenImages;
    
    protected override string JsonPath => "JSON/RecycleSetting.json";

    private float _popupFadeTime;
    private float _gameCloseTime;

    protected override async UniTask Init()
    {
        _popupFadeTime = setting.popupFadeTime;
        _gameCloseTime = setting.gameCloseTime;
        
        // 고정 이미지 세팅
        SettingImageObject(mainImage1, setting.main1);
        SettingImageObject(mainImage2, setting.main2);
        SettingImageObject(mainImage3, setting.main3);
        SettingImageObject(popupImage1, setting.popup1);
        SettingImageObject(endBackgroundImage, setting.endBackground);
        SettingImageObject(endImage1, setting.endImage1);
        SettingImageObject(endImage2, setting.endImage2);
        
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
        
        endBackgroundImage.gameObject.SetActive(false);
        
        ArduinoInputManager.Instance?.SetLedAll(true);
        StartBlinkGreenAsync(500, 160);
        
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage3 });

        // 입력 대기
        while (true)
        {
            if ((ArduinoInputManager.Instance && ArduinoInputManager.Instance.TryConsumeAnyPress(out _))
                || TryConsumeSingleInput())
            {   
                StopLedEffects();
                ArduinoInputManager.Instance?.SetLedAll(false);
                LedStrip.Range(0, 9, 255, 0, 0);
                break;
            }
                
            await UniTask.Yield();
        }
        ArduinoInputManager.Instance?.FlushAll();
        
        // 팝업과 미션 종료 이미지 크로스페이드
        CrossFadeAsync(popupImage1, endBackgroundImage, _popupFadeTime).Forget();
        
        // 설정한 시간이 지난 후 타이틀로 전환
        await UniTask.Delay(TimeSpan.FromSeconds(_gameCloseTime));
        LoadSceneAsync(0, new[] { fadeImage1, fadeImage3 }).Forget();
    }
}
