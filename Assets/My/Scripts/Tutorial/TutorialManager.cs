using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[Serializable]
public class TutorialSetting
{
    public float camera3TurnSpeed;
    public TextSetting infoText;
    public ImageSetting[] tutorialImages;
}

public class TutorialManager : MonoBehaviour
{
    [Header("Camera")] 
    [SerializeField] private Camera mainCamera; // Display1 Camera
    [SerializeField] private Camera camera2; // Display2 Camera
    [SerializeField] private Camera camera3; // Display3 Camera

    [Header("FadeImage")]
    [SerializeField] private Image fadeImage1; // Display1 Fade
    [SerializeField] private Image fadeImage3; // Display3 Fade

    [Header("UI")] 
    [SerializeField] private GameObject infoText; // Display1 Info Text

    [Header("TutorialImage")] 
    [SerializeField] private GameObject tutorialImage1;
    [SerializeField] private GameObject tutorialImage2;
    [SerializeField] private GameObject tutorialImage3;

    private readonly List<GameObject> _tutorialImages = new List<GameObject>();

    private TutorialSetting _tutorialSetting;
    private float _camera3TurnSpeed;
    private float _fadeTime;
    private bool _canInput;         // 페이드 중 입력 방지
    private bool _transitioning;    // 튜토리얼 전환 중 입력 방지
    private int _step;              

    private void Awake()
    {
        if (!mainCamera || !camera2 || !camera3)
        {
            Debug.LogError("[TutorialManager] camera is not assigned");
        }

        if (!fadeImage1 || !fadeImage3)
        {
            Debug.LogError("[TutorialManager] fadeImage is not assigned");
        }

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

    private async void Start()
    {
        try
        {
            await Init();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private void Update()
    {
        if (!_canInput || _transitioning) return;

        if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.touchCount > 0)
        {
            StartCoroutine(AdvanceTutorial());
        }
    }

    private async Task Init()
    {
        _tutorialSetting ??= JsonLoader.Instance.LoadJsonData<TutorialSetting>("JSON/TutorialSetting.json");
        _camera3TurnSpeed = _tutorialSetting.camera3TurnSpeed;
        _fadeTime = JsonLoader.Instance.settings.fadeTime;

        // 인포 "컨트롤러의 아무 버튼을 누르면 다음 화면으로 진행됩니다." 텍스트 설정
        if (infoText.TryGetComponent(out TextMeshProUGUI uiTextInfo) &&
            infoText.TryGetComponent(out RectTransform rt))
        {
            await UICreator.Instance.ApplyFontAsync(
                uiTextInfo,
                _tutorialSetting.infoText.fontName,
                _tutorialSetting.infoText.text,
                _tutorialSetting.infoText.fontSize,
                _tutorialSetting.infoText.fontColor,
                _tutorialSetting.infoText.alignment,
                CancellationToken.None
            );

            UIUtility.ApplyRect(rt,
                size: null,
                anchoredPos: new Vector2(_tutorialSetting.infoText.position.x, -_tutorialSetting.infoText.position.y),
                rotation: _tutorialSetting.infoText.rotation);
        }

        // 튜토리얼 이미지들의 텍스쳐 및 위치, 크기 설장
        for (int i = 0; i < _tutorialImages.Count; i++)
        {
            if (_tutorialImages[i].TryGetComponent(out Image image) &&
                _tutorialImages[i].TryGetComponent(out RectTransform rect))
            {
                ImageSetting imageSetting = _tutorialSetting.tutorialImages[i];
                Texture2D texture = UIUtility.LoadTextureFromStreamingAssets(imageSetting.sourceImage);
                if (texture != null)
                {
                    image.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    image.color = imageSetting.color;
                    image.type = (Image.Type)imageSetting.type;
                }

                UIUtility.ApplyRect(rect,
                    size: imageSetting.size,
                    anchoredPos: new Vector2(imageSetting.position.x, -imageSetting.position.y),
                    rotation: imageSetting.rotation);
            }
        }

        // 1번을 제외하고 비활성화
        SetActiveWithAlpha(tutorialImage1, true, 1f);
        SetActiveWithAlpha(tutorialImage2, false, 0f);
        SetActiveWithAlpha(tutorialImage3, false, 0f);
        _step = 0;

        StartCoroutine(TurnCamera3());
        StartCoroutine(FadeImage(1f, 0f, _fadeTime));
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

    private IEnumerator TurnCamera3()
    {
        if (!camera3)
        {
            Debug.LogError("[TitleManager] camera3 is not assigned");
            yield break;
        }

        while (true)
        {
            camera3.transform.Rotate(Vector3.up, _camera3TurnSpeed * Time.deltaTime, Space.World);
            yield return null;
        }
    }

    /// <summary> 씬 시작, 다음 씬 넘어가기 전 화면 페이드를 설정함 </summary>
    private IEnumerator FadeImage(float start, float end, float duration)
    {
        _canInput = false;
        float elapsed = 0f;
        float alpha = 0f;

        while (elapsed < duration)
        {
            alpha = Mathf.Lerp(start, end, elapsed / duration);
            fadeImage1.color = fadeImage3.color = new Color(0f, 0f, 0f, alpha);

            elapsed += Time.deltaTime;
            yield return null;
        }

        fadeImage1.color = fadeImage3.color = new Color(0f, 0f, 0f, alpha);
        _canInput = true;
    }

    /// <summary> 단계 진행: 1 - > 2 -> 3은 크로스페이드, 3에서 입력 시 페이드아웃 후 다음 씬 </summary>
    private IEnumerator AdvanceTutorial()
    {
        _transitioning = true;

        if (_step == 0)
        {
            yield return CrossFade(tutorialImage1, tutorialImage2, _fadeTime);
            _step = 1;
        }
        else if (_step == 1)
        {
            yield return CrossFade(tutorialImage2, tutorialImage3, _fadeTime);
            _step = 2;
        }
        else // _step == 2
        {
            yield return FadeImage(0f, 1f, _fadeTime);
            SceneManager.LoadScene("IntroScene");
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

    private void SetAlpha(Image img, float a)
    {
        Color c = img.color;
        c.a = a;
        img.color = c;
    }
}