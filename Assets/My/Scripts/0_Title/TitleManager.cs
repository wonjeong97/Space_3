using System;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;

[Serializable]
public class TitleSetting
{
    public ImageSetting titleImage;
    public ImageSetting infoImage;
}

/// <summary> 타이틀 씬 관리 클래스 </summary>
public class TitleManager : SceneManager_Base<TitleSetting>
{
    [Header("UI")]
    [SerializeField] private GameObject titleImage; // 우주발사체 타이틀 이미지
    [SerializeField] private GameObject infoImage;  // 시작하려면 아무 버튼이나 누르세요 이미지

    protected override string JsonPath => "JSON/TitleSetting.json";

    /// <summary> 씬 초기화 메서드 </summary>
    protected override async Task Init()
    {
        if (!titleImage)
        {
            Debug.LogError("[TitleManager] titleImage is not assigned");
        }
        inputReceived = false;

        // 타이틀 이미지 세팅
        SettingImageObject(titleImage, setting.titleImage); 
        SettingImageObject(infoImage, setting.infoImage);

        // 이미지 세팅까지 한 프레임 늦춤
        await Task.Yield();
        ArduinoInputManager.Instance?.SetLedAll(true);    
        
        // 연출
        StartCoroutine(TurnCamera3());
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage3 });
        
        while (true)
        {       
            if (ArduinoInputManager.Instance && ArduinoInputManager.Instance.TryConsumeAnyPress(out _)) break;
            if (TryConsumeSingleInput()) break;
            
            await Task.Yield();
        }

        // 씬 전환
        int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 1;
        await LoadSceneAsync(target, new[] { fadeImage1, fadeImage3 });
    }
}