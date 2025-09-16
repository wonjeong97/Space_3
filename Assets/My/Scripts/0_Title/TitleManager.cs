using System;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
public class TitleSetting
{
    public TextSetting titleText;
    public TextSetting infoText;
}

public class TitleManager : SceneManager_Base<TitleSetting>
{
    [Header("UI")]
    [SerializeField] private GameObject titleText; // Display1 Title Text
    [SerializeField] private GameObject infoText; // Display1 Info Text

    protected override string JsonPath => "JSON/TitleSetting.json";

    /// <summary> 씬 초기화 메서드 </summary>
    protected override async Task Init()
    {
        if (!titleText || !infoText)
        {
            Debug.LogError("[TitleManager] Text UI is not assigned");
        }

        // 타이틀 / 안내 문구 세팅
        await SettingTextObject(titleText, setting.titleText);
        await SettingTextObject(infoText, setting.infoText);

        StartCoroutine(TurnCamera3());
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage3 });

        // 입력 대기
        while (!TryConsumeSingleInput())
            await Task.Yield();

        // 입력 후 씬 전환
        int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 1;
        await LoadSceneAsync(target, new[] { fadeImage1, fadeImage3 });
    }
}