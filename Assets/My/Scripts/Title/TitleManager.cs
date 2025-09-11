using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

[Serializable]
public class TitleSetting
{
    public float camera3TurnSpeed;
    public TextSetting titleText;
    public TextSetting infoText;
}

public class TitleManager : MonoBehaviour
{
    public static TitleManager Instance { get; private set; }

    [Header("Camera")] 
    [SerializeField] private Camera mainCamera;     // Display1 Camera
    [SerializeField] private Camera camera2;        // Display2 Camera
    [SerializeField] private Camera camera3;        // Display3 Camera

    [Header("FadeImage")] 
    [SerializeField] private Image fadeImage1;      // Display1 Fade
    [SerializeField] private Image fadeImage3;      // Display3 Fade

    [Header("UI")] 
    [SerializeField] private GameObject titleText;  // Display1 Title Text
    [SerializeField] private GameObject infoText;   // Display1 Info Text

    private TitleSetting _titleSetting;
    private float _camera3TurnSpeed;
    private float _fadeTime;
    private bool _inputReceived;    // 중복 입력 방지
    private bool _canInput;     // 페이드 중 입력 방지

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }

        if (!mainCamera || !camera2 || !camera3)
        {
            Debug.LogError("[TitleManager] camera is not assigned");
        }

        if (!fadeImage1 || !fadeImage3)
        {
            Debug.LogError("[TitleManager] fadeImage is not assigned");
        }

        if (!titleText || !infoText)
        {
            Debug.LogError("[TitleManager] Text UI is not assigned");
        }
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
        if (_inputReceived || !_canInput) return;

        // 키보드, 마우스, 터치 입력 감지
        if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.touchCount > 0)
        {
            _inputReceived = true;
            StartCoroutine(LoadTutorialScene());
        }
    }

    /// <summary> 초기화 메서드 </summary>
    private async Task Init()
    {
        // 설정 값 가져옴
        _titleSetting ??= JsonLoader.Instance.LoadJsonData<TitleSetting>("JSON/TitleSetting.json");
        _camera3TurnSpeed = _titleSetting.camera3TurnSpeed;
        _fadeTime = JsonLoader.Instance.settings.fadeTime;
        
        // 타이틀 "우주발사체" 텍스트 설정
        if (titleText.TryGetComponent(out TextMeshProUGUI uiTextTitle) &&
            titleText.TryGetComponent(out RectTransform rt1))
        {
            await UICreator.Instance.ApplyFontAsync(
                uiTextTitle,
                _titleSetting.titleText.fontName,
                _titleSetting.titleText.text,
                _titleSetting.titleText.fontSize,
                _titleSetting.titleText.fontColor,
                _titleSetting.titleText.alignment,
                CancellationToken.None
            );

            UIUtility.ApplyRect(rt1,
                size: null,
                anchoredPos: new Vector2(_titleSetting.titleText.position.x, -_titleSetting.titleText.position.y),
                rotation: _titleSetting.titleText.rotation);
        }
        
        // 인포 "시작하려면 아무 버튼이나 누르세요" 텍스트 설정
        if (infoText.TryGetComponent(out TextMeshProUGUI uiTextInfo) &&
            infoText.TryGetComponent(out RectTransform rt2))
        {
            await UICreator.Instance.ApplyFontAsync(
                uiTextInfo,
                _titleSetting.infoText.fontName,
                _titleSetting.infoText.text,
                _titleSetting.infoText.fontSize,
                _titleSetting.infoText.fontColor,
                _titleSetting.infoText.alignment,
                CancellationToken.None
            );
            
            UIUtility.ApplyRect(rt2,
                size: null,
                anchoredPos: new Vector2(_titleSetting.infoText.position.x, -_titleSetting.infoText.position.y),
                rotation: _titleSetting.infoText.rotation);
        }
        
        StartCoroutine(TurnCamera3());
        StartCoroutine(FadeImage(1f, 0f, _fadeTime));
    }

    /// <summary> Display3 카메라를 일정 속도로 계속 회전시킴 </summary>
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
    
    /// <summary> TutorialScene으로 전환 </summary>
    private IEnumerator LoadTutorialScene()
    {
        // 페이드 아웃 
        yield return FadeImage(0f, 1f, _fadeTime);
        SceneManager.LoadScene("TutorialScene");
    }
}