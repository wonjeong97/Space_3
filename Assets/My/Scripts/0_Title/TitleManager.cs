using System;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
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

    protected override void Awake()
    {   
        base.Awake();
        if (!titleText || !infoText)
        {
            Debug.LogError("[TitleManager] Text UI is not assigned");
        }
    }

    private void Update()
    {
        if (inputReceived || !canInput) return;

        // 키보드, 마우스, 터치 입력 감지
        if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.touchCount > 0)
        {
            inputReceived = true;
            StartCoroutine(FadeAndLoadScene(1, new[] { fadeImage1, fadeImage3 }));
        }
    }

    /// <summary> 초기화 메서드 </summary>
    protected override async Task Init()
    {
        // 타이틀 "우주발사체" 텍스트 설정
        await SettingTextObject(titleText, setting.titleText);

        // 인포 "시작하려면 아무 버튼이나 누르세요" 텍스트 설정
        await SettingTextObject(infoText, setting.infoText);

        StartCoroutine(TurnCamera3());
        StartCoroutine(FadeImage(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage3 }));
    }
}