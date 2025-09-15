using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

public abstract class SceneManager_Base<T> : MonoBehaviour
{
    [Header("Camera")] 
    [SerializeField] protected Camera mainCamera; // Display1 Camera
    [SerializeField] protected Camera camera2; // Display2 Camera
    [SerializeField] protected Camera camera3; // Display3 Camera

    [Header("Canvas")]
    [SerializeField] protected Canvas mainCanvas;
    [SerializeField] protected Canvas subCanvas;
    [SerializeField] protected Canvas verticalCanvas;

    [Header("FadeImage")]
    [SerializeField] protected Image fadeImage1; // Display1 Fade
    [SerializeField] protected Image fadeImage2; // Display2 Fade
    [SerializeField] protected Image fadeImage3; // Display3 Fade

    [NonSerialized] protected T setting;
    private Settings jsonSetting;
    protected abstract string JsonPath { get; }

    private float _camera3TurnSpeed;
    protected float fadeTime;
    protected bool inputReceived; // 중복 입력 방지
    protected bool canInput; // 페이드 중 입력 방지

    protected virtual void Awake()
    {
        if (!mainCamera || !camera2 || !camera3)
        {
            Debug.LogError("[SceneManager] camera is not assigned");
        }

        if (!mainCanvas || !subCanvas || !verticalCanvas)
        {
            Debug.LogError("[SceneManager] canvas is not assigned");
        }

        if (!fadeImage1 || !fadeImage2 || !fadeImage3)
        {
            Debug.LogError("[SceneManager]] fadeImage is not assigned");
        }
    }

    protected virtual async void Start()
    {
        try
        {
            jsonSetting ??= JsonLoader.Instance.settings;
            setting = JsonLoader.Instance.LoadJsonData<T>(JsonPath);
            
            _camera3TurnSpeed = jsonSetting.camera3TurnSpeed;
            fadeTime = jsonSetting.fadeTime;

            // 윈도우에서 디스플레이 번호가 바꼈을 때를 대비한 캔버스, 카메라 타깃 디스플레이 설정            
            mainCamera.targetDisplay = jsonSetting.canvas1TargetMonitorIndex;
            mainCanvas.targetDisplay = jsonSetting.canvas1TargetMonitorIndex;
            
            camera2.targetDisplay = jsonSetting.canvas2TargetMonitorIndex;
            subCanvas.targetDisplay = jsonSetting.canvas2TargetMonitorIndex;
            
            camera3.targetDisplay = jsonSetting.canvas3TargetMonitorIndex;
            verticalCanvas.targetDisplay = jsonSetting.canvas3TargetMonitorIndex;
            
            await Init();
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }

    protected abstract Task Init();

    /// <summary> Display3 카메라를 일정 속도로 계속 회전시킴 </summary>
    protected IEnumerator TurnCamera3()
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
    protected IEnumerator FadeImage(float start, float end, float duration, Image[] targets)
    {
        canInput = false;
        float elapsed = 0f;
        float alpha = 0f;

        while (elapsed < duration)
        {
            alpha = Mathf.Lerp(start, end, elapsed / duration);

            foreach (Image image in targets)
            {
                SetAlpha(image, alpha);
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        foreach (Image image in targets)
        {
            SetAlpha(image, alpha);
        }

        canInput = true;
    }
    
    protected async Task FadeImageAsync(float start, float end, float duration, Image[] targets)
    {
        canInput = false;
        float elapsed = 0f;
        float alpha = start;

        while (elapsed < duration)
        {
            alpha = Mathf.Lerp(start, end, elapsed / duration);
            foreach (var image in targets) SetAlpha(image, alpha);

            elapsed += Time.deltaTime;
            await Task.Yield(); // 다음 프레임까지 양보
        }

        foreach (var image in targets) SetAlpha(image, end);
        canInput = true;
    }
    
    /// <summary> 두 게임오브젝트의 이미지를 크로스 페이드 함 </summary>
    protected IEnumerator CrossFade(GameObject fromGo, GameObject toGo, float duration)
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

    /// <summary> 입력한 씬으로 전환 </summary>
    protected IEnumerator FadeAndLoadScene(int sceneBuildIndex, Image[] fadeImage)
    {
        // 페이드 아웃 
        yield return FadeImage(0f, 1f, fadeTime, fadeImage);
        SceneManager.LoadScene(sceneBuildIndex);
    }
    
    protected void SetAlpha(Image img, float alpha)
    {
        Color c = img.color;
        c.a = alpha;
        img.color = c;
    }

    protected async Task SettingTextObject(GameObject textObject, TextSetting textSetting)
    {
        if (textObject.TryGetComponent(out TextMeshProUGUI text) &&
            textObject.TryGetComponent(out RectTransform rt))
        {
            await UICreator.Instance.ApplyFontAsync(
                text,
                textSetting.fontName,
                textSetting.text,
                textSetting.fontSize,
                textSetting.fontColor,
                textSetting.alignment,
                CancellationToken.None
            );
            
            UIUtility.ApplyRect(rt,
                size: null,
                anchoredPos: new Vector2(textSetting.position.x, -textSetting.position.y),
                rotation: textSetting.rotation);
        }
    }

    protected void SettingImageObject(GameObject imageObject, ImageSetting imageSetting)
    {
        if (imageObject.TryGetComponent(out Image image)&&
            imageObject.TryGetComponent(out RectTransform rt))
        {
            Texture2D texture = UIUtility.LoadTextureFromStreamingAssets(imageSetting.sourceImage);
            if (texture != null)
            {
                image.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                image.color = imageSetting.color;
                image.type = (Image.Type)imageSetting.type;
            }

            UIUtility.ApplyRect(rt,
                size: imageSetting.size,
                anchoredPos: new Vector2(imageSetting.position.x, -imageSetting.position.y),
                rotation: imageSetting.rotation);
        }
    }
    
    protected async Task SettingVideoObject(GameObject vpObject, VideoSetting videoSetting, VideoPlayer vp, RawImage raw, AudioSource audioSource)
    {
        if (vpObject.TryGetComponent(out RectTransform rt))
        {
            UIUtility.ApplyRect(
                rt,
                size: videoSetting.size,
                anchoredPos: new Vector2(videoSetting.position.x, -videoSetting.position.y),
                rotation: Vector3.zero
            );
        }

        VideoManager.Instance.WireRawImageAndRenderTexture(
            vp, raw, new Vector2Int(Mathf.RoundToInt(videoSetting.size.x), Mathf.RoundToInt(videoSetting.size.y)));

        string url = VideoManager.Instance.ResolvePlayableUrl(videoSetting.fileName);
        await VideoManager.Instance.PrepareAndPlayAsync(vp, url, audioSource, videoSetting.volume, CancellationToken.None);
    }
}