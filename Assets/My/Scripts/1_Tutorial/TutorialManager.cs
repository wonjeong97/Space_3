using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class TutorialSetting
{
    public ImageSetting background;
    
    public ImageSetting infoImage1;
    public ImageSetting infoImage2;
    public ImageSetting infoImage3;
    
    public TextSetting infoText;
    public ImageSetting[] tutorialImages;
}

/// <summary> 튜토리얼 씬 관리 매니저 </summary>
public class TutorialManager : SceneManager_Base<TutorialSetting>
{
    [Header("UI")]
    [SerializeField] private GameObject backgroundImage; // 배경 팝업 이미지
    [SerializeField] private GameObject infoImage1; // "조작 안내"
    [SerializeField] private GameObject infoImage2; // "모니터에 출력되는 내용을 보고 따라해주세요!"
    [SerializeField] private GameObject infoImage3; // "컨트롤러의 아무 버튼을 누르면 다음 화면으로 진행됩니다."
    [SerializeField] private List<GameObject> tutorialImageObjs; // 튜토리얼 이미지

    protected override string JsonPath => "JSON/TutorialSetting.json";

    private int _step;
    private float CrossFadeTime => fadeTime;

    protected override async Task Init()
    {
        _step = 0;
        
        // 설정 개수와 오브젝트 개수 동기화
        // 오브젝트 or 세팅 중 더 작은 개수를 사용하여 null 에러 방지
        int count = Mathf.Min(tutorialImageObjs.Count, setting.tutorialImages.Length);
        for (int i = 0; i < count; i++)
            SettingImageObject(tutorialImageObjs[i], setting.tutorialImages[i]);

        // 1번만 보이게 초기화, 나머지는 알파값 0, 비활성화
        for (int i = 0; i < tutorialImageObjs.Count; i++)
            SetActiveWithAlpha(tutorialImageObjs[i], i == 0, i == 0 ? 1f : 0f);
        
        SettingImageObject(backgroundImage, setting.background); // 배경 팝업
        SettingImageObject(infoImage1, setting.infoImage1);
        SettingImageObject(infoImage2, setting.infoImage2);
        SettingImageObject(infoImage3, setting.infoImage3);
        
        await Task.Yield(); // 이미지 세팅을 위해 한 프레임 멈춤
        
        StartCoroutine(TurnCamera3());
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage3 });

        // 입력마다 다음 단계 진행
        while (true)
        {
            // 입력 대기
            while (true)
            {
                if ((ArduinoInputManager.Instance && ArduinoInputManager.Instance.TryConsumeAnyPress(out _)) 
                    || TryConsumeSingleInput()) break;
                
                await Task.Yield();
            }
            
            if (_step < count - 1)
            {
                await AdvanceStepAsync(tutorialImageObjs[_step], tutorialImageObjs[_step + 1], CrossFadeTime);
                _step++;
            }
            else
            {   
                canInput = false;
                if (ArduinoInputManager.Instance) ArduinoInputManager.Instance.FlushAll();
                inputReceived = false;
                
                int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 2;
                await LoadSceneAsync(target, new[] { fadeImage1, fadeImage3 });
                break;
            }
  
        }
    }

    /// <summary> 게임 오브젝트의 활성화 여부 및 이미지의 알파 설정 </summary>
    private void SetActiveWithAlpha(GameObject go, bool active, float alpha)
    {
        if (!go) return;
        go.SetActive(active);
        if (go.TryGetComponent(out Image img))
        {
            Color c = img.color;
            c.a = alpha;
            img.color = c;
        }
    }
}