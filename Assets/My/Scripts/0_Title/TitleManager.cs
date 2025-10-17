using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

[Serializable]
public class TitleSetting
{
    public ImageSetting background;
    public ImageSetting titleImage;
    public ImageSetting infoImage;
}

/// <summary> 타이틀 씬 관리 클래스 </summary>
public class TitleManager : SceneManager_Base<TitleSetting>
{
    [Header("UI")]
    [SerializeField] private GameObject backgroundImage;
    [SerializeField] private GameObject titleImage; // 우주발사체 타이틀 이미지
    [SerializeField] private GameObject infoImage;  // 시작하려면 아무 버튼이나 누르세요 이미지

    protected override string JsonPath => "JSON/TitleSetting.json";
    
    /// <summary> 씬 초기화 메서드 </summary>
    protected override async UniTask Init()
    {
        if (!titleImage)
        {
            Debug.LogError("[TitleManager] titleImage is not assigned");
        }
        inputReceived = false;

        // 타이틀 이미지 세팅
        SettingImageObject(backgroundImage, setting.background);
        SettingImageObject(titleImage, setting.titleImage); 
        SettingImageObject(infoImage, setting.infoImage);

        // 이미지 세팅까지 한 프레임 늦춤
        await UniTask.Yield();
        
        ArduinoInputManager.Instance?.SetLedAll(true);   
        StartBlinkGreenAsync(500, 160);
        
        // 연출
        TurnCamera3Async(this.GetCancellationTokenOnDestroy()).Forget();
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage3 });
        
        CancellationToken cancel = this.GetCancellationTokenOnDestroy();
        while (!cancel.IsCancellationRequested && isActiveAndEnabled)
        {
            if (ArduinoInputManager.Instance && ArduinoInputManager.Instance.TryConsumeAnyPress(out _)) break;
            if (TryConsumeSingleInput()) break;
            await UniTask.Yield();
        }

        // 씬 전환
        int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 1;
        await LoadSceneAsync(target, new[] { fadeImage1, fadeImage3 });
    }
}