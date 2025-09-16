using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class TutorialSetting
{
    public TextSetting infoText;
    public ImageSetting[] tutorialImages;
}

public class TutorialManager : SceneManager_Base<TutorialSetting>
{
    [Header("UI")]
    [SerializeField] private GameObject infoTextObj; // 안내 텍스트
    [SerializeField] private List<GameObject> tutorialImageObjs; // 튜토리얼 이미지

    protected override string JsonPath => "JSON/TutorialSetting.json";

    private int _step;
    private float CrossFadeTime => fadeTime;

    protected override async Task Init()
    {
        if (!infoTextObj)
            Debug.LogError("[TutorialManager] infoTextObj is not assigned");

        // 설정 개수와 오브젝트 개수 동기화
        int count = Mathf.Min(tutorialImageObjs.Count, setting.tutorialImages.Length);
        for (int i = 0; i < count; i++)
            SettingImageObject(tutorialImageObjs[i], setting.tutorialImages[i]);

        // 1번만 보이게 초기화, 나머지는 알파값 0, 비활성화
        for (int i = 0; i < tutorialImageObjs.Count; i++)
            SetActiveWithAlpha(tutorialImageObjs[i], i == 0, i == 0 ? 1f : 0f);

        _step = 0;

        // 안내 텍스트 세팅
        await SettingTextObject(infoTextObj, setting.infoText);

        // 첫 진입 페이드 인
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage3 });

        // 입력마다 다음 단계 진행
        while (true)
        {
            // 입력 대기
            while (!TryConsumeSingleInput()) await Task.Yield();
            inputReceived = false; // 즉시 초기화

            if (_step < count - 1)
            {
                await CrossFadeAsync(tutorialImageObjs[_step], tutorialImageObjs[_step + 1], CrossFadeTime);
                _step++;
            }
            else
            {
                int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 2;
                await LoadSceneAsync(target, new[] { fadeImage1, fadeImage3 });
                break;
            }
        }
    }

    /// <summary> 게임 오브젝트 이미지의 활성화와 알파값을 설정함 </summary>
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