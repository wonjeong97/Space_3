using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

[Serializable]
public class IntroSetting
{
    public float camera3TurnSpeed;
    public VideoSetting introVideo;
}

public class IntroManager : MonoBehaviour
{
    [Header("Camera")] 
    [SerializeField] private Camera mainCamera; // Display1 Camera
    [SerializeField] private Camera camera2; // Display2 Camera
    [SerializeField] private Camera camera3; // Display3 Camera

    [Header("FadeImage")]
    [SerializeField] private Image fadeImage1; // Display1 Fade
    [SerializeField] private Image fadeImage3; // Display3 Fade

    [Header("Video")]
    [SerializeField] private GameObject videoPlayerObject;

    private IntroSetting _introSetting;
    private float _camera3TurnSpeed;
    private float _fadeTime;
    private bool _canInput;

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

    private void OnDisable()
    {
        StopAllCoroutines();
        if (videoPlayerObject.TryGetComponent(out VideoPlayer videoPlayer))
        {
            videoPlayer.loopPointReached -= OnVideoEnded;
        }
    }

    private async Task Init()
    {
        _introSetting ??= JsonLoader.Instance.LoadJsonData<IntroSetting>("JSON/IntroSetting.json");
        _camera3TurnSpeed = _introSetting.camera3TurnSpeed;
        _fadeTime = JsonLoader.Instance.settings.fadeTime;

        VideoPlayer vp = videoPlayerObject.GetComponent<VideoPlayer>();
        RawImage raw = videoPlayerObject.GetComponent<RawImage>();
        AudioSource audioSource = UIUtility.GetOrAdd<AudioSource>(videoPlayerObject);

        VideoSetting setting = _introSetting.introVideo;
        if (videoPlayerObject.TryGetComponent(out RectTransform rt))
        {
            UIUtility.ApplyRect(
                rt,
                size: setting.size,
                anchoredPos: new Vector2(setting.position.x, -setting.position.y),
                rotation: Vector3.zero
            );
        }

        VideoManager.Instance.WireRawImageAndRenderTexture(
            vp, raw, new Vector2Int(Mathf.RoundToInt(setting.size.x), Mathf.RoundToInt(setting.size.y)));

        string url = VideoManager.Instance.ResolvePlayableUrl(setting.fileName);
        await VideoManager.Instance.PrepareAndPlayAsync(vp, url, audioSource, setting.volume, CancellationToken.None);

        vp.loopPointReached += OnVideoEnded;

        StartCoroutine(TurnCamera3());
        StartCoroutine(FadeImage(1f, 0f, _fadeTime));
    }

    private void OnVideoEnded(VideoPlayer vp)
    {
        vp.Stop();

        StartCoroutine(LoadNewtonScene());
    }

    /// <summary> NewtonScene으로 전환 </summary>
    private IEnumerator LoadNewtonScene()
    {
        // 페이드 아웃 
        yield return FadeImage(0f, 1f, _fadeTime);
        //SceneManager.LoadScene("NewtonScene");
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
}