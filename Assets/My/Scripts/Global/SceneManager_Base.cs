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
    #region Serialized Refs

    [Header("Camera")]
    [SerializeField] protected Camera mainCamera; // Display1
    [SerializeField] protected Camera camera2; // Display2
    [SerializeField] protected Camera camera3; // Display3

    [Header("Canvas")]
    [SerializeField] protected Canvas mainCanvas;
    [SerializeField] protected Canvas subCanvas;
    [SerializeField] protected Canvas verticalCanvas;

    [Header("Fade Images")]
    [SerializeField] protected Image fadeImage1; // Display1 Fade
    [SerializeField] protected Image fadeImage2; // Display2 Fade
    [SerializeField] protected Image fadeImage3; // Display3 Fade

    [Header("Scene Flow")] 
    [Tooltip("현재 씬에서 다음 씬으로 넘어갈 때 사용할 빌드 인덱스")] 
    [SerializeField] protected int nextSceneBuildIndex = -1;

    [Tooltip("이 씬에서 비활성 타임아웃을 적용할지 여부")] [SerializeField]
    private bool useInactivityTimeout = true;

    #endregion

    #region Settings / State

    [SerializeField] private bool sIsLoading;

    [NonSerialized] protected T setting;
    private Settings _globalSettings; // JsonLoader.Instance.settings 캐시

    protected float fadeTime; // 페이드 시간
    protected bool canInput; // 페이드 중/전환 중 입력 방지
    protected bool inputReceived; // 중복 입력 방지

    private float _inactivityTimer; // 무입력 시간 누적
    private float _inactivityThreshold; // Scene0로 복귀 임계값
    private float _camera3TurnSpeed; // 회전 속도

    protected int buttonDelayTime;

    protected abstract string JsonPath { get; }

    private CancellationTokenSource _cts; // 씬 생명주기용 CTS

    #endregion

    #region Unity Life-Cycle

    protected virtual void Awake()
    {
        if (!mainCamera || !camera2 || !camera3)
            Debug.LogError("[SceneManager] camera is not assigned");

        if (!mainCanvas || !subCanvas || !verticalCanvas)
            Debug.LogError("[SceneManager] canvas is not assigned");

        if (!fadeImage1 || !fadeImage2 || !fadeImage3)
            Debug.LogError("[SceneManager] fadeImage is not assigned");

        _cts = new CancellationTokenSource();
    }

    protected virtual async void Start()
    {
        try
        {
            _globalSettings ??= JsonLoader.Instance.settings;
            setting = JsonLoader.Instance.LoadJsonData<T>(JsonPath);

            _camera3TurnSpeed = _globalSettings.camera3TurnSpeed;
            fadeTime = _globalSettings.fadeTime;
            _inactivityThreshold = _globalSettings.inactivityTime;
            buttonDelayTime = _globalSettings.buttonDelayTime;

            // 윈도우 디스플레이 순서가 바뀌어도 JSON으로 지정 가능
            mainCamera.targetDisplay = _globalSettings.canvas1TargetMonitorIndex;
            mainCanvas.targetDisplay = _globalSettings.canvas1TargetMonitorIndex;

            camera2.targetDisplay = _globalSettings.canvas2TargetMonitorIndex;
            subCanvas.targetDisplay = _globalSettings.canvas2TargetMonitorIndex;

            camera3.targetDisplay = _globalSettings.canvas3TargetMonitorIndex;
            verticalCanvas.targetDisplay = _globalSettings.canvas3TargetMonitorIndex;

            await InitSafe(); // 자식 초기화
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }

    protected virtual void Update()
    {
        if (!useInactivityTimeout) return;

        // 타이틀에서는 무시
        if (SceneManager.GetActiveScene().buildIndex != 0)
        {
            _inactivityTimer += Time.deltaTime;
            if (_inactivityTimer >= _inactivityThreshold)
            {
                _inactivityTimer = 0f;
                // 페이드 후 타이틀 복귀
                _ = LoadSceneAsync(0, new[] { fadeImage1, fadeImage2, fadeImage3 });
            }
        }

        if (IsAnyUserInputDown())
            _inactivityTimer = 0f;
    }

    protected virtual void OnDisable()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (Exception e)
        {
            Debug.LogError($"[SceneManager_Base] OnDisable exception Error: {e}");
        }

        _cts?.Dispose();
        _cts = null;
    }

    #endregion

    #region Template Methods (for children)

    /// <summary> 자식에서 구현할 실제 초기화. 안전 래핑은 InitSafe가 담당. </summary>
    protected abstract Task Init();

    // 씬 로드 직후 초기화용
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 로드 완료 후 공통 상태 초기화
        sIsLoading = false;
        _inactivityTimer = 0f; // 타임아웃 카운터 리셋
        inputReceived = false; // 입력 래치 리셋
        canInput = false; // 자식 Init에서 true로 열리게

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary> 입력 1회 트리거를 받고 싶을 때 호출할 헬퍼 </summary>
    protected bool TryConsumeSingleInput()
    {
        if (inputReceived || !canInput) return false;
        if (!IsAnyUserInputDown()) return false;

        inputReceived = true;
        return true;
    }

    #endregion

    #region Init Wrapper

    private async Task InitSafe()
    {
        canInput = false;
        inputReceived = false;
        
        // 씬 전환 시 이전 씬에서 받았던 버튼 입력 큐 초기화
        if (ArduinoInputManager.Instance) ArduinoInputManager.Instance.FlushAll();
        await Init(); // 자식 초기화
        canInput = true;
    }

    #endregion

    #region Camera / Fade / Scene

    /// <summary> Display3 카메라를 일정 속도로 계속 회전시킴 </summary>
    protected IEnumerator TurnCamera3()
    {
        if (!camera3)
        {
            Debug.LogError("[SceneManager] camera3 is not assigned");
            yield break;
        }

        while (true)
        {
            camera3.transform.Rotate(Vector3.up, _camera3TurnSpeed * Time.deltaTime, Space.World);
            yield return null;
        }
    }

    /// <summary> 알파값만 변경 </summary>
    protected void SetAlpha(Graphic g, float a)
    {
        if (!g) return;
        Color c = g.color;
        c.a = a;
        g.color = c;
    }

    /// <summary> 씬 시작/종료 페이드용 </summary>
    protected async Task FadeImageAsync(float start, float end, float duration, Image[] targets)
    {
        canInput = false;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float a = Mathf.Lerp(start, end, elapsed / duration);
            foreach (Image img in targets) SetAlpha(img, a);
            elapsed += Time.deltaTime;
            await Task.Yield();
        }

        foreach (Image img in targets) SetAlpha(img, end);
        canInput = true;
    }

    /// <summary> 두 UI 이미지 간 크로스 페이드 </summary>
    protected async Task CrossFadeAsync(GameObject fromGo, GameObject toGo, float duration)
    {
        if (!fromGo || !toGo) return;
        if (!fromGo.TryGetComponent(out Image from) || !toGo.TryGetComponent(out Image to)) return;

        toGo.SetActive(true);
        SetAlpha(to, 0f);

        float time = 0f;
        while (time < duration)
        {
            float alpha = time / duration;
            SetAlpha(from, 1f - alpha);
            SetAlpha(to, alpha);
            time += Time.deltaTime;
            await Task.Yield(); // 다음 프레임까지 양보
        }

        SetAlpha(from, 0f);
        fromGo.SetActive(false);
        SetAlpha(to, 1f);
    }

    /// <summary> 페이드 후 씬 로드 (async) </summary>
    protected async Task LoadSceneAsync(int buildIndex, Image[] fades)
    {
        if (sIsLoading) return; // 중복 전환 방지
        sIsLoading = true;
        canInput = false; // 씬 전환 중 입력 차단

        StopAllCoroutines();
        
        // 파생 클래스에서 생성한 취소토큰이 있다면 정리
        try
        {
            OnBeforeSceneUnload();
        }
        catch(Exception e)
        {
            Debug.LogError($"[SceneManager_Base] OnBeforeSceneUnload exception Error: {e}]");
        }
        
        await FadeImageAsync(0f, 1f, fadeTime, fades);
        SceneManager.sceneLoaded += OnSceneLoaded;
        AsyncOperation op = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
        await Task.Yield();
    }

    /// <summary> 씬 전환 직전 클래스의 비동기/이벤트 정리 </summary>
    protected virtual void OnBeforeSceneUnload()
    {
    }

    #endregion

    #region UI Builders

    /// <summary> TextObject 설정: 폰트/문구/색/정렬/RectTransform 반영 </summary>
    protected async Task SettingTextObject(GameObject textObject, TextSetting ts)
    {
        if (!textObject || ts == null) return;
        if (textObject.TryGetComponent(out TextMeshProUGUI tmp) &&
            textObject.TryGetComponent(out RectTransform rt))
        {
            await UICreator.Instance.ApplyFontAsync(
                tmp,
                ts.fontName,
                ts.text,
                ts.fontSize,
                ts.fontColor,
                ts.alignment,
                CancellationToken.None
            );

            UIUtility.ApplyRect(
                rt,
                size: null,
                anchoredPos: new Vector2(ts.position.x, -ts.position.y),
                rotation: ts.rotation
            );
        }
    }

    /// <summary> ImageObject 설정: 스트리밍 에셋 이미지 로드/타입/RectTransform 반영 </summary>
    protected void SettingImageObject(GameObject imageObject, ImageSetting iset)
    {
        if (!imageObject || iset == null) return;
        if (imageObject.TryGetComponent(out Image img) &&
            imageObject.TryGetComponent(out RectTransform rt))
        {
            Texture2D tex = UIUtility.LoadTextureFromStreamingAssets(iset.sourceImage);
            if (tex != null)
            {
                img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                img.color = iset.color;
                img.type = (Image.Type)iset.type;
            }

            UIUtility.ApplyRect(
                rt,
                size: iset.size,
                anchoredPos: new Vector2(iset.position.x, -iset.position.y),
                rotation: iset.rotation
            );
        }
    }

    /// <summary> VideoObject 설정: RT 바인딩, URL 해석, Prepare & Play </summary>
    protected async Task SettingVideoObject(GameObject vpObject, VideoSetting vs, VideoPlayer vp, RawImage raw, AudioSource audioSource)
    {
        if (!vpObject || vs == null || !vp || !raw) return;

        if (vpObject.TryGetComponent(out RectTransform rt))
        {
            UIUtility.ApplyRect(
                rt,
                size: vs.size,
                anchoredPos: new Vector2(vs.position.x, -vs.position.y),
                rotation: Vector3.zero
            );
        }

        VideoManager.Instance.WireRawImageAndRenderTexture(
            vp, raw, new Vector2Int(Mathf.RoundToInt(vs.size.x), Mathf.RoundToInt(vs.size.y)));

        string url = VideoManager.Instance.ResolvePlayableUrl(vs.fileName);
        await VideoManager.Instance.PrepareAndPlayAsync(vp, url, audioSource, vs.volume, CancellationToken.None);
    }

    #endregion

    #region Input Utils

    /// <summary> 사용자 입력이 있었는지 간단 체크 </summary>
    private bool IsAnyUserInputDown()
    {
        if (Input.anyKeyDown) return true;
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) return true;
        if (Input.touchCount > 0) return true;
        return false;
    }
    
    /// <summary> 크로스 페이드 도중 입력을 막아 바로 다음 이미지로 넘어가는 것을 방지함 </summary>
    protected async Task AdvanceStepAsync(GameObject fromGo, GameObject toGo, float duration)
    {
        // 입력 잠금 및 큐 비우기(전환 직전)
        canInput = false;
        if (ArduinoInputManager.Instance) ArduinoInputManager.Instance.FlushAll();
        inputReceived = false;

        await CrossFadeAsync(fromGo, toGo, duration);

        // 전환 직후 다시 한 번 큐 비우기(전환 중 누적분 제거)
        if (ArduinoInputManager.Instance) ArduinoInputManager.Instance.FlushAll();
        inputReceived = false;

        // 입력 재개
        canInput = true;
    }
    
    #endregion
}