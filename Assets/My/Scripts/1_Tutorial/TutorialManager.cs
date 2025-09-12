using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class TutorialSetting
{
    public float camera3TurnSpeed;
    public TextSetting infoText;
    public ImageSetting[] tutorialImages;
}

public class TutorialManager : SceneManager_Base<TutorialSetting>
{
    [Header("UI")] 
    [SerializeField] private GameObject infoText; // Display1 Info Text

    [Header("TutorialImage")] 
    [SerializeField] private GameObject tutorialImage1;
    [SerializeField] private GameObject tutorialImage2;
    [SerializeField] private GameObject tutorialImage3;

    protected override string JsonPath => "JSON/TutorialSetting.json";

    private readonly List<GameObject> _tutorialImages = new List<GameObject>();
    private bool _transitioning; // 튜토리얼 전환 중 입력 방지
    private int _step;

    protected override void Awake()
    {
        if (!infoText)
        {
            Debug.LogError("[TutorialManager] infoText is not assigned");
        }

        if (!tutorialImage1 || !tutorialImage2 || !tutorialImage3)
        {
            Debug.LogError("[TutorialManager] Some tutorialImages are not assigned");
        }

        // 튜토리얼 이미지들을 리스트에 넣어 관리
        _tutorialImages.Add(tutorialImage1);
        _tutorialImages.Add(tutorialImage2);
        _tutorialImages.Add(tutorialImage3);
    }

    private void Update()
    {
        if (!canInput || _transitioning) return;

        if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.touchCount > 0)
        {
            StartCoroutine(AdvanceTutorial());
        }
    }

    protected override async Task Init()
    {
        // 인포 "컨트롤러의 아무 버튼을 누르면 다음 화면으로 진행됩니다." 텍스트 설정
        await SettingTextObject(infoText, setting.infoText);

        // 튜토리얼 이미지들의 텍스쳐 및 위치, 크기 설장
        for (int i = 0; i < _tutorialImages.Count; i++)
        {
            SettingImageObject(_tutorialImages[i], setting.tutorialImages[i]);
        }

        // 1번을 제외하고 비활성화
        SetActiveWithAlpha(tutorialImage1, true, 1f);
        SetActiveWithAlpha(tutorialImage2, false, 0f);
        SetActiveWithAlpha(tutorialImage3, false, 0f);
        _step = 0;

        StartCoroutine(TurnCamera3());
        StartCoroutine(FadeImage(1f, 0f, fadeTime, new []{ fadeImage1, fadeImage3 }));
    }

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

    /// <summary> 단계 진행: 1 - > 2 -> 3은 크로스페이드, 3에서 입력 시 페이드아웃 후 다음 씬 </summary>
    private IEnumerator AdvanceTutorial()
    {
        _transitioning = true;

        if (_step == 0)
        {
            yield return CrossFade(tutorialImage1, tutorialImage2, fadeTime);
            _step = 1;
        }
        else if (_step == 1)
        {
            yield return CrossFade(tutorialImage2, tutorialImage3, fadeTime);
            _step = 2;
        }
        else // _step == 2
        {
            StartCoroutine(FadeAndLoadScene(2, new[] { fadeImage1, fadeImage3 }));
        }

        _transitioning = false;
    }

    private IEnumerator CrossFade(GameObject fromGo, GameObject toGo, float duration)
    {
        if (!fromGo || !toGo) yield break;
        Image from = fromGo.GetComponent<Image>();
        Image to = toGo.GetComponent<Image>();
        if (!from || !to) yield break;

        toGo.SetActive(true);
        SetAlpha(to, 0f);

        float t = 0f;
        while (t < duration)
        {
            float a = t / duration;
            SetAlpha(from, 1f - a);
            SetAlpha(to, a);
            t += Time.deltaTime;
            yield return null;
        }

        SetAlpha(from, 0f);
        fromGo.SetActive(false);
        SetAlpha(to, 1f);
    }
}