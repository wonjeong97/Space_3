using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

public abstract class SceneManager_Base<T> : MonoBehaviour
{
    #region Serialized Refs

    [Header("Camera")]
    [SerializeField] protected Camera mainCamera;       // Display1
    [SerializeField] protected Camera subCamera;        // Display2
    [SerializeField] protected Camera verticalCamera;   // Display3

    [Header("Canvas")]
    [SerializeField] protected Canvas mainCanvas;     // 1920 x 1080 캔버스
    [SerializeField] protected Canvas subCanvas;      // 1920 x 540 캔버스
    [SerializeField] protected Canvas verticalCanvas; // 1080 x 3840 캔버스

    [Header("Fade Images")] 
    [SerializeField] protected Image fadeImage1; // Display1 Fade
    [SerializeField] protected Image fadeImage2; // Display2 Fade
    [SerializeField] protected Image fadeImage3; // Display3 Fade

    [Header("Scene Flow")] 
    [Tooltip("현재 씬에서 다음 씬으로 넘어갈 때 사용할 빌드 인덱스")] 
    [SerializeField] protected int nextSceneBuildIndex = -1;

    [Tooltip("이 씬에서 비활성 타임아웃을 적용할지 여부")] 
    [SerializeField] private bool useInactivityTimeout = true;

    #endregion

    #region Settings / State

    private static volatile bool _sTransitionInProgress; // 전역 씬 전환 중 여부
    protected static bool TransitionInProgress => _sTransitionInProgress;

    [NonSerialized] protected T setting;    // JSON 설정 데이터 참조
    private Settings _mainSettings;         // 공통 설정 참조

    // ===== protected =====
    protected bool canInput;        // 입력 가능 여부 (페이드 중 방지)
    protected int buttonDelayTime;  // 버튼 클릭 간 딜레이 시간 (ms)
    protected bool inputReceived;   // 입력 한 번만 허용할 때 사용
    protected float fadeTime;       // 페이드 시간 설정

    // ===== private =====
    private readonly Dictionary<GameObject, CancellationTokenSource> _anchoredYAnimCts = new Dictionary<GameObject, CancellationTokenSource>(); // Y좌표 애니메이션 관리
    private readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>(); // 버튼 이미지 캐싱
    private readonly Dictionary<GameObject, CancellationTokenSource> _blinkCtsDict = new Dictionary<GameObject, CancellationTokenSource>(); // 조작 방식 버튼 이미지 on/off 깜빡임 관리

    private CancellationToken _destroyToken;        // 객체 파괴 감지용 토큰
    private CancellationTokenSource _ledCts;        // LED 제어용 토큰

    private const float DebugSkipCooldown = 0.25f;  // 디버그 스킵 쿨다운 시간

    private bool _destroyTokenInitialized;          // Destroy 토큰 초기화 여부
    private bool _isLoading;                        // 현재 씬 로드 중 여부

    private float _camera3TurnSpeed;                // 세 번째 카메라 회전 속도
    private float _inactivityThreshold;             // 비활성 상태에서 홈으로 복귀 시간 기준
    private float _inactivityTimer;                 // 현재 비활성 누적 시간
    private float _lastDebugSkipTime;               // 마지막 디버그 스킵 입력 시간

    private int _arduinoTouchedFlag;                // 아두이노 입력 감지 플래그
    private int _blinkHalfPeriodMs = 300;           // LED 초록색 깜빡임 주기 절반 시간
    private int _inactivityPauseCount;              // 비활성 타이머 일시정지 중첩 카운트
    
    private bool IsInactivityPaused => _inactivityPauseCount > 0; // 일시정지 상태 여부

    public bool CanInput { get; set; }
    public bool InputReceived { get; set; }

    public float InactivityTimer // 현재 비활성 누적 시간 접근자
    {
        get => _inactivityTimer;
        set => _inactivityTimer = value;
    }
    
    protected abstract string JsonPath { get; } // 설정 JSON 경로

    #endregion

    #region Unity Life-Cycle

    /// <summary> 의존성 확인 및 아두이노 이벤트 구독 </summary>
    protected virtual void Awake()
    {
        if (!mainCamera || !subCamera || !verticalCamera) Debug.LogWarning("[SceneManager_Base] Awake-> 카메라가 지정되지 않았습니다");
        if (!mainCanvas || !subCanvas || !verticalCanvas) Debug.LogWarning("[SceneManager_Base] Awake-> 캔버스가 지정되지 않았습니다");
        if (!fadeImage1 || !fadeImage2 || !fadeImage3) Debug.LogWarning("[SceneManager_Base] Awake-> 페이드 이미지가 지정되지 않았습니다");

        if (!_destroyTokenInitialized)
        {
            try
            {
                _destroyToken = this.GetCancellationTokenOnDestroy();
                _destroyTokenInitialized = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SceneManager_Base] Awake-> DestroyToken 생성 중 예외: {e.Message}");
            }
        }

        TryHookArduino();
    }

    /// <summary> 공통 초기값 로드 및 자식 Init 안전 래핑 호출 </summary>
    protected virtual async void Start()
    {
        try
        {
            _mainSettings ??= JsonLoader.Instance.settings;
            setting = JsonLoader.Instance.LoadJsonData<T>(JsonPath);

            _camera3TurnSpeed = _mainSettings.camera3TurnSpeed;
            fadeTime = _mainSettings.fadeTime;
            _inactivityThreshold = _mainSettings.inactivityTime;
            buttonDelayTime = _mainSettings.buttonDelayTime;

            // 윈도우 디스플레이 순서가 바뀌어도 JSON으로 지정 가능
            mainCamera.targetDisplay = _mainSettings.canvas1TargetMonitorIndex;
            mainCanvas.targetDisplay = _mainSettings.canvas1TargetMonitorIndex;
            subCamera.targetDisplay = _mainSettings.canvas2TargetMonitorIndex;
            subCanvas.targetDisplay = _mainSettings.canvas2TargetMonitorIndex;
            verticalCamera.targetDisplay = _mainSettings.canvas3TargetMonitorIndex;
            verticalCanvas.targetDisplay = _mainSettings.canvas3TargetMonitorIndex;

            await InitSafe();
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning("[SceneManager_Base] Start-> 초기화가 취소되었습니다");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SceneManager_Base] Start-> 예외 발생: {e}");
        }
    }

    /// <summary> 무입력 타임아웃 및 디버그 스킵 처리 </summary>
    protected virtual void Update()
    {
        if (useInactivityTimeout)
        {
            if (SceneManager.GetActiveScene().buildIndex != 0)
            {
                if (!IsInactivityPaused)
                {
                    _inactivityTimer += Time.deltaTime;
                    if (_inactivityTimer >= _inactivityThreshold && !_isLoading && !TransitionInProgress)
                    {
                        _inactivityTimer = 0f;
                        _sTransitionInProgress = true;
                        LoadSceneAsync(0, new[] { fadeImage1, fadeImage2, fadeImage3 }).Forget();
                    }
                }
            }

            // 입력 감지 시 타이머 리셋
            if (IsAnyUserInputDown()) _inactivityTimer = 0f;
            else if (Interlocked.Exchange(ref _arduinoTouchedFlag, 0) != 0) _inactivityTimer = 0f;
        }

        // 디버그 스킵
        if (Input.GetKeyDown(KeyCode.Space))
        {
            float now = Time.time;
            if (now - _lastDebugSkipTime >= DebugSkipCooldown)
            {
                _lastDebugSkipTime = now;
                HandleDebugSkipKey();
            }
        }
    }

    /// <summary> LED/이벤트 해제 </summary>
    protected virtual void OnDisable()
    {
        try
        {
            StopLedEffects();
            ArduinoInputManager.Instance?.SetLedAll(false);
        }
        catch (Exception e)
        {
            Debug.LogError($"[{SceneManager.GetActiveScene().name}] OnDisable Exception: {e}");
        }
        finally
        {
            UnhookArduino();    
        }
    }

    #endregion

    #region Template Methods (for children)

    /// <summary> 자식에서 구현할 실제 초기화. 안전 래핑은 InitSafe가 담당. </summary>
    protected abstract UniTask Init();

    /// <summary> 씬 로드 직후 상태 리셋 </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _isLoading = false;
        _sTransitionInProgress = false; // 전역 잠금 해제

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

    /// <summary> 자식 Init 안전 호출과 입력 래치 초기화 </summary>
    private async UniTask InitSafe()
    {
        canInput = false;
        inputReceived = false;

        // 씬 전환 시 이전 씬에서 받았던 버튼 입력 큐 초기화
        ArduinoInputManager.Instance?.FlushAll();

        await Init();
        canInput = true;
    }

    #endregion

    #region Scene / Fade / Camera

    /// <summary> Display3 카메라를 일정 속도로 계속 회전시킴 </summary>
    protected async UniTaskVoid TurnCamera3Async(CancellationToken token)
    {
        if (!verticalCamera)
        {
            Debug.LogError("[SceneManager_Base] TurnCamera3Async-> camera3가 지정되지 않았습니다");
            return;
        }

        try
        {
            while (!token.IsCancellationRequested)
            {
                verticalCamera.transform.Rotate(Vector3.up, _camera3TurnSpeed * Time.deltaTime, Space.World);
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary> 그래픽 알파만 변경 </summary>
    protected void SetAlpha(Graphic g, float a)
    {
        if (!g) return;
        Color c = g.color;
        c.a = a;
        g.color = c;
    }

    /// <summary> 씬 시작/종료 페이드 </summary>
    protected async UniTask FadeImageAsync(float start, float end, float duration, Image[] targets)
    {
        canInput = false;
        float elapsed = 0f;

        duration = Mathf.Max(0f, duration);
        while (elapsed < duration)
        {
            float a = Mathf.Lerp(start, end, elapsed / duration);
            if (targets != null)
            {
                foreach (Image img in targets)
                {
                    if (img) SetAlpha(img, a);
                }
            }

            elapsed += Time.deltaTime;
            await UniTask.Yield();
        }

        if (targets != null)
        {
            foreach (Image img in targets)
            {
                if (img) SetAlpha(img, end);
            }
        }

        canInput = true;
    }

    /// <summary> 두 UI 이미지 간 크로스 페이드 </summary>
    protected async UniTask CrossFadeAsync(GameObject fromGo, GameObject toGo, float duration)
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
            await UniTask.Yield();
        }

        SetAlpha(from, 0f);
        fromGo.SetActive(false);
        SetAlpha(to, 1f);
    }

    /// <summary> 페이드 후 씬 로드(0번 씬은 동기 로드) </summary>
    protected async UniTask LoadSceneAsync(int buildIndex, Image[] fadeImages)
    {
        if (_isLoading) return;
        _isLoading = true;
        _sTransitionInProgress = true; // 어떤 씬 로드든 시작 시 전역 잠금

        OnBeforeSceneUnload();

        CancellationToken cancel = DestroyToken;

        // 타이틀(0) 복귀는 동기 로드 경로
        if (buildIndex == 0)
        {
            if (fadeImages != null && fadeImages.Length > 0)
            {
                try
                {
                    await FadeImageAsync(0f, 1f, Mathf.Max(0f, fadeTime), fadeImages);
                }
                catch (OperationCanceledException)
                {
                    Debug.LogWarning("[SceneManager_Base] LoadSceneAsync-> 타이틀 페이드가 취소되었습니다");
                    _isLoading = false;
                    _sTransitionInProgress = false;
                    return;
                }
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            try
            {
                SceneManager.LoadScene(0, LoadSceneMode.Single); // 동기 로드
            }
            catch (Exception e)
            {
                Debug.LogError($"[SceneManager_Base] LoadSceneAsync-> 동기 로드 예외: {e}");
                SceneManager.sceneLoaded -= OnSceneLoaded;
                _isLoading = false;
                _sTransitionInProgress = false;
            }

            return;
        }

        // ===== 일반 전환: 비동기 경로 =====
        SceneManager.sceneLoaded += OnSceneLoaded;

        if (fadeImages != null && fadeImages.Length > 0)
        {
            try
            {
                await FadeImageAsync(0f, 1f, Mathf.Max(0f, fadeTime), fadeImages);
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("[SceneManager_Base] LoadSceneAsync-> 페이드가 취소되었습니다");
                SceneManager.sceneLoaded -= OnSceneLoaded;
                _isLoading = false;
                _sTransitionInProgress = false;
                return;
            }
        }

        AsyncOperation op = null;
        try
        {
            op = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
            if (op == null)
            {
                Debug.LogError($"[SceneManager_Base] LoadSceneAsync-> AsyncOperation이 null입니다 (buildIndex: {buildIndex})");
                SceneManager.sceneLoaded -= OnSceneLoaded;
                _isLoading = false;
                _sTransitionInProgress = false;
                return;
            }

            op.allowSceneActivation = false;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SceneManager_Base] LoadSceneAsync-> 시작 중 예외: {e}");
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _isLoading = false;
            _sTransitionInProgress = false;
            return;
        }

        try
        {
            while (!cancel.IsCancellationRequested && !op.isDone)
            {
                if (op.progress < 0.9f)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, cancel);
                    continue;
                }

                op.allowSceneActivation = true;
                await UniTask.Yield(PlayerLoopTiming.Update, cancel);
                break;
            }
        }
        catch (OperationCanceledException)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _isLoading = false;
            _sTransitionInProgress = false;
        }
    }

    /// <summary> 씬 전환 직전 비동기/이벤트 정리 </summary>
    protected virtual void OnBeforeSceneUnload()
    {
        if (!this || !isActiveAndEnabled) return;

        canInput = false;
        inputReceived = true;
        _inactivityPauseCount = 0;

        try
        {
            StopAllCoroutines();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SceneManager_Base] OnBeforeSceneUnload-> StopAllCoroutines 실패: {e.Message}");
        }

        StopLedEffects();

        try
        {
            ArduinoInputManager.Instance?.FlushAll();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SceneManager_Base] OnBeforeSceneUnload-> FlushAll 실패: {e.Message}");
        }

        if (!Mathf.Approximately(Time.timeScale, 1f)) Time.timeScale = 1f;
    }

    #endregion

    #region Input Utils

    /// <summary> 사용자 입력이 있었는지 간단 체크 </summary>
    private bool IsAnyUserInputDown()
    {
        if (Input.anyKeyDown) return true;
        if (Input.touchCount > 0) return true;
        return false;
    }

    /// <summary> 크로스 페이드 도중 입력을 막아 바로 다음 이미지로 넘어가는 것을 방지 </summary>
    protected async UniTask AdvanceStepAsync(GameObject fromGo, GameObject toGo, float duration)
    {
        canInput = false;
        if (ArduinoInputManager.Instance) ArduinoInputManager.Instance.FlushAll();
        inputReceived = false;

        await CrossFadeAsync(fromGo, toGo, duration);

        if (ArduinoInputManager.Instance) ArduinoInputManager.Instance.FlushAll();
        inputReceived = false;
        canInput = true;
    }

    /// <summary> 디버그 스킵 키 처리 </summary>
    private void HandleDebugSkipKey()
    {
        _inactivityTimer = 0f; // 타임아웃 리셋
        OnDebugSkip();
    }

    /// <summary> 디버그 스킵 훅: 파생 클래스에서 영상 정지/다음 단계 진행 등 구현 </summary>
    protected virtual void OnDebugSkip()
    {
        if (!inputReceived && canInput)
        {
            inputReceived = true;
            Debug.Log("[SceneManager_Base] OnDebugSkip-> 디버그 스킵 입력 감지, inputReceived=true");
        }
    }

    #endregion

    #region UI Builders

    /// <summary> TextObject 설정: 폰트/문구/색/정렬/RectTransform 반영 </summary>
    protected async UniTask SettingTextObject(GameObject textObject, TextSetting ts, string overrideText = null)
    {
        if (!textObject || ts == null) return;

        if (textObject.TryGetComponent(out TextMeshProUGUI tmp) && textObject.TryGetComponent(out RectTransform rt))
        {
            string finalText = string.IsNullOrEmpty(overrideText) ? ts.text : overrideText;

            await UICreator.Instance.ApplyFontAsync(
                tmp,
                ts.fontName,
                finalText,
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
    protected void SettingImageObject(GameObject imageObject, ImageSetting imageSet)
    {
        if (!imageObject || imageSet == null) return;
        if (imageObject.TryGetComponent(out Image img) && imageObject.TryGetComponent(out RectTransform rt))
        {
            Texture2D tex = UIUtility.LoadTextureFromStreamingAssets(imageSet.sourceImage);
            if (tex != null)
            {
                img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                img.color = imageSet.color;
                img.type = (Image.Type)imageSet.type;
            }

            UIUtility.ApplyRect(
                rt,
                size: imageSet.size,
                anchoredPos: new Vector2(imageSet.position.x, -imageSet.position.y),
                rotation: imageSet.rotation,
                scale: imageSet.scale
            );
        }
    }

    /// <summary> VideoObject 설정: RT 바인딩, URL 해석, Prepare & Play </summary>
    protected async UniTask SettingVideoObject(GameObject vpObject, VideoSetting vs, VideoPlayer vp, RawImage raw, AudioSource audioSource)
    {   
        if (!vpObject || vs == null || !vp || !raw)
        {   
            Debug.LogWarning($"vpObject: {vpObject?.name}, vs: {vs?.name}, vp: {vp?.name}, raw: {raw?.name}");
            return;
        }
        
        if (vpObject.TryGetComponent(out RectTransform rt))
        {
            UIUtility.ApplyRect(
                rt,
                size: vs.size,
                anchoredPos: new Vector2(vs.position.x, -vs.position.y),
                rotation: Vector3.zero
            );
        }

        VideoManager.Instance.WireRawImageAndRenderTexture(vp, raw, new Vector2Int(Mathf.RoundToInt(vs.size.x), Mathf.RoundToInt(vs.size.y)));
        string url = VideoManager.Instance.ResolvePlayableUrl(vs.fileName);

        await VideoManager.Instance.PrepareAndPlayAsync(vp, url, audioSource, vs.volume, DestroyToken).AsUniTask();
    }

    #endregion

    #region Video Helpers / Inactivity Policy

    /// <summary> 비디오 재생 시 첫 프레임을 렌더 텍스처에 그리고 대기(깜빡임 방지) </summary>
    protected async UniTask<bool> WaitFirstFrameAsync(VideoPlayer vp, RawImage ri, CancellationToken token, double maxSeconds = 2.0)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + maxSeconds;
        while (!token.IsCancellationRequested && vp != null)
        {
            if (vp.frame > 0 && vp.texture != null && ri != null && ri.texture != null)
                return true;

            if (Time.realtimeSinceStartupAsDouble >= deadline) break;
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        return vp != null && vp.texture != null;
    }

    /// <summary> 루프 영상 외에는 재생 중 무입력 타이머 일시정지 정책을 적용 </summary>
    protected void BindInactivityPolicyToVideo(VideoPlayer vp, bool isLoopVideo, CancellationToken token)
    {
        if (vp == null) return;
        if (isLoopVideo) return; // Loop 영상은 타이머 계속 진행

        BeginInactivityPause();
        bool ended = false;

        void SafeEndPause()
        {
            if (ended) return;
            ended = true;
            try
            {
                vp.loopPointReached -= OnLoopPointReached;
                vp.errorReceived -= OnError;
                vp.started -= OnStarted;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SceneManager_Base] BindInactivityPolicyToVideo-> 이벤트 해제 중 예외: {e.Message}");
            }

            EndInactivityPause();
        }

        void OnLoopPointReached(VideoPlayer _) => SafeEndPause();
        void OnError(VideoPlayer _, string __) => SafeEndPause();

        void OnStarted(VideoPlayer _)
        {
            /* no-op */
        }

        vp.loopPointReached += OnLoopPointReached;
        vp.errorReceived += OnError;
        vp.started += OnStarted;

        UniTask.Void(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested && vp != null && !vp.isPlaying)
                    await UniTask.Yield();
                while (!token.IsCancellationRequested && vp != null && vp.isPlaying)
                    await UniTask.Yield();
            }
            finally
            {
                SafeEndPause();
            }
        });
    }

    #endregion

    #region Button Sprite Helpers (On/Off)

    /// <summary> StreamingAssets 경로를 받아 Sprite를 반환(캐시 사용). 실패 시 null. </summary>
    private Sprite GetSpriteFromStreamingAssets(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            Debug.LogError("[SceneManager_Base] GetSpriteFromStreamingAssets-> 경로가 비어 있습니다");
            return null;
        }

        if (_spriteCache.TryGetValue(relativePath, out Sprite cached))
            return cached;

        Texture2D tex = UIUtility.LoadTextureFromStreamingAssets(relativePath);
        if (tex == null)
        {
            Debug.LogError($"[SceneManager_Base] GetSpriteFromStreamingAssets-> 텍스처 로드 실패: {relativePath}");
            return null;
        }

        Sprite sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        _spriteCache[relativePath] = sp;
        return sp;
    }

    /// <summary> 버튼 이미지 교체 </summary>
    private void SetButtonSprite(GameObject buttonObject, string relativePath)
    {
        if (!buttonObject)
        {
            Debug.LogWarning("[SceneManager_Base] SetButtonSprite-> buttonObject가 null입니다");
            return;
        }

        Image img = buttonObject.GetComponent<Image>();
        if (!img)
        {
            Debug.LogWarning("[SceneManager_Base] SetButtonSprite-> Image 컴포넌트를 찾을 수 없습니다");
            return;
        }

        Sprite sp = GetSpriteFromStreamingAssets(relativePath);
        if (sp != null) img.sprite = sp;
    }

    /// <summary> 버튼 이미지를 On 상태로 교체한다 -> Image/MainDisplay/ButtonOn.png </summary>
    protected void SetButtonOn(GameObject buttonObject)
    {
        SetButtonSprite(buttonObject, "Image/MainDisplay/ButtonOn.png");
    }

    /// <summary> 버튼 이미지를 Off 상태로 교체한다 -> Image/MainDisplay/ButtonOff.png </summary>
    protected void SetButtonOff(GameObject buttonObject)
    {
        SetButtonSprite(buttonObject, "Image/MainDisplay/ButtonOff.png");
    }

    /// <summary> 여러 버튼을 한 번에 On 상태로 교체 </summary>
    protected void SetButtonsOn(params GameObject[] buttons)
    {
        if (buttons == null) return;
        for (int i = 0; i < buttons.Length; i++) SetButtonOn(buttons[i]);
    }

    /// <summary> 여러 버튼을 한 번에 Off 상태로 교체 </summary>
    protected void SetButtonsOff(params GameObject[] buttons)
    {
        if (buttons == null) return;
        for (int i = 0; i < buttons.Length; i++) SetButtonOff(buttons[i]);
    }

    /// <summary> 지정한 버튼을 깜빡이게 한다. </summary>
    protected void StartButtonBlink(GameObject buttonObject, float interval = 0.3f)
    {
        if (buttonObject == null)
        {
            Debug.LogWarning("[SceneManager_Base] StartButtonBlink-> buttonObject가 null입니다");
            return;
        }

        // 기존에 깜빡이는 중이면 중단
        StopButtonBlink(buttonObject);

        CancellationTokenSource cts = new CancellationTokenSource();
        _blinkCtsDict[buttonObject] = cts;
        BlinkRoutineAsync(buttonObject, interval, cts.Token).Forget();
    }

    /// <summary> 버튼 깜빡임 중단 </summary>
    protected void StopButtonBlink(GameObject buttonObject)
    {
        if (buttonObject == null) return;

        if (_blinkCtsDict.TryGetValue(buttonObject, out CancellationTokenSource cts))
        {
            cts.Cancel();
            cts.Dispose();
            _blinkCtsDict.Remove(buttonObject);
        }

        // 중단 시 Off 상태로 복귀
        SetButtonOff(buttonObject);
    }

    /// <summary> 모든 버튼의 깜빡임 중단 </summary>
    protected void StopAllButtonBlinks()
    {
        foreach (var kvp in _blinkCtsDict)
        {
            kvp.Value.Cancel();
            kvp.Value.Dispose();
            if (kvp.Key != null)
                SetButtonOff(kvp.Key);
        }
        _blinkCtsDict.Clear();
    }

    /// <summary> 내부 비동기 루프: On/Off 반복 </summary>
    private async UniTask BlinkRoutineAsync(GameObject buttonObject, float interval, CancellationToken token)
    {
        bool isOn = false;
        while (!token.IsCancellationRequested)
        {
            if (isOn)
                SetButtonOff(buttonObject);
            else
                SetButtonOn(buttonObject);

            isOn = !isOn;
            await UniTask.Delay(TimeSpan.FromSeconds(interval), cancellationToken: token);
        }
    }
    
    #endregion

    #region Anchored Y Animation (쓰로틀 바 등)

    /// <summary> target의 RectTransform.anchoredPosition.y를 yStart -> yEnd 로 duration 동안 선형 보간 </summary>
    protected void PlayAnchoredY(GameObject target, float yStart, float yEnd, float duration, float waitAtEnd)
    {
        if (!target) return;

        if (_anchoredYAnimCts.TryGetValue(target, out CancellationTokenSource running) && running != null)
        {
            CancelAndDispose(ref running);
            _anchoredYAnimCts[target] = null;
        }

        CancellationTokenSource cts = new CancellationTokenSource();
        _anchoredYAnimCts[target] = cts;

        float d = Mathf.Max(0.001f, duration);
        float w = Mathf.Max(0f, waitAtEnd);

        MoveAnchoredYLoopAsync(target, yStart, yEnd, d, w, cts.Token).Forget();
    }

    /// <summary> 진행 중인 target의 Y 애니메이션을 중지 </summary>
    protected void StopAnchoredY(GameObject target)
    {
        if (!target) return;

        if (_anchoredYAnimCts.TryGetValue(target, out CancellationTokenSource cts) && cts != null)
        {
            CancelAndDispose(ref cts);
            _anchoredYAnimCts.Remove(target);
        }
    }

    /// <summary> 모든 Anchored Y 애니메이션을 중지 </summary>
    protected void StopAllAnchoredY()
    {
        if (_anchoredYAnimCts.Count == 0) return;

        foreach (KeyValuePair<GameObject, CancellationTokenSource> kv in _anchoredYAnimCts)
        {
            CancellationTokenSource cts = kv.Value;
            CancelAndDispose(ref cts);
        }

        _anchoredYAnimCts.Clear();
    }

    /// <summary> 단발 이동 스텝 </summary>
    private async UniTask MoveAnchoredYStepAsync(GameObject target, float yFrom, float yTo, float duration, CancellationToken token)
    {
        if (!target) return;
        RectTransform rt = target.GetComponent<RectTransform>();
        if (!rt) return;

        Vector2 pos = rt.anchoredPosition;
        pos.y = yFrom;
        rt.anchoredPosition = pos;

        float t = 0f;
        float d = Mathf.Max(0.001f, duration);

        while (t < d && !token.IsCancellationRequested)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / d);
            float y = Mathf.Lerp(yFrom, yTo, u);
            Vector2 p = rt.anchoredPosition;
            p.y = y;
            rt.anchoredPosition = p;
            await UniTask.Yield();
        }

        if (!token.IsCancellationRequested)
        {
            Vector2 p = rt.anchoredPosition;
            p.y = yTo;
            rt.anchoredPosition = p;
        }
    }

    /// <summary> yStart -> yEnd 이동 반복. End에서 waitAtEnd 대기 후 Start로 스냅 </summary>
    private async UniTask MoveAnchoredYLoopAsync(GameObject target, float yStart, float yEnd, float duration, float waitAtEnd, CancellationToken token)
    {
        if (!target) return;
        RectTransform rt = target.GetComponent<RectTransform>();
        if (!rt) return;

        Vector2 p = rt.anchoredPosition;
        p.y = yStart;
        rt.anchoredPosition = p;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await MoveAnchoredYStepAsync(target, yStart, yEnd, duration, token);
                if (token.IsCancellationRequested) break;

                if (waitAtEnd > 0f)
                {
                    try
                    {
                        await UniTask.Delay(TimeSpan.FromSeconds(waitAtEnd), cancellationToken: token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                if (!token.IsCancellationRequested)
                {
                    Vector2 snap = rt.anchoredPosition;
                    snap.y = yStart;
                    rt.anchoredPosition = snap;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning("[SceneManager_Base] MoveAnchoredYLoopAsync-> 애니메이션이 취소되었습니다");
        }
        finally
        {
            if (_anchoredYAnimCts.TryGetValue(target, out CancellationTokenSource mine))
            {
                if (mine != null && mine.Token == token)
                {
                    CancelAndDispose(ref mine);
                    _anchoredYAnimCts.Remove(target);
                }
            }
        }
    }

    #endregion

    #region LED Effects
    
    /// <summary> 버튼 LED 블링크 </summary>
    protected async UniTask BlinkLedAsync(int ledIndex, int onMs, int offMs, CancellationToken token)
    {
        ArduinoInputManager mgr = ArduinoInputManager.Instance;
        if (mgr == null) return;

        try
        {
            while (!token.IsCancellationRequested)
            {
                mgr.SetLed(ledIndex, true);
                try { await UniTask.Delay(onMs, cancellationToken: token); }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SceneManager_Base] BlinkLedAsync-> 대기 중 예외(켜짐 구간): {e.Message}");
                }

                mgr.SetLed(ledIndex, false);
                try { await UniTask.Delay(offMs, cancellationToken: token); }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SceneManager_Base] BlinkLedAsync-> 대기 중 예외(꺼짐 구간): {e.Message}");
                }
            }
        }
        finally
        {
            try { mgr.SetLed(ledIndex, false); }
            catch (Exception e) { Debug.LogWarning($"[SceneManager_Base] BlinkLedAsync-> LED 종료 처리 중 예외: {e.Message}"); }
        }
    }

    /// <summary> 외부 공개: 초록 깜빡임 시작 </summary>
    public void PublicStartBlinkGreen(int periodMsHalf, int onBrightness)
    {
        StartBlinkGreenAsync(periodMsHalf, onBrightness);
    }

    /// <summary> 내부 시작: 파라미터 적용 후 루프 시작 </summary>
    protected void StartBlinkGreenAsync(int periodMsHalf, int onBrightness)
    {
        _blinkHalfPeriodMs = Mathf.Max(50, periodMsHalf);
        StopLedEffects();
        _ledCts = new CancellationTokenSource();
        _ = BlinkGreenLoopAsync(_ledCts.Token, onBrightness);
    }

    /// <summary> 외부 공개: LED 효과 정지 </summary>
    public void PublicStopLedEffects()
    {
        StopLedEffects();
    }

    /// <summary> 내부 정지: CTS 취소/해제 </summary>
    protected void StopLedEffects()
    {
        CancelAndDispose(ref _ledCts);
    }

    /// <summary> 깜빡임 루프 </summary>
    private async UniTaskVoid BlinkGreenLoopAsync(CancellationToken token, int onBrightness)
    {
        bool on = true;
        while (!token.IsCancellationRequested)
        {
            if (on)
            {
                LedStrip.Fill(0, 255, 0);
                if (onBrightness < 255) LedStrip.Bright(Mathf.Clamp(onBrightness, 1, 255));
            }
            else
            {
                LedStrip.Clear();
            }

            on = !on;

            try
            {
                await UniTask.Delay(_blinkHalfPeriodMs, cancellationToken: token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    #endregion

    #region Inactivity Pause API

    public void PauseInactivityTimer()
    {
        BeginInactivityPause();
    }

    public void ResumeInactivityTimer()
    {
        EndInactivityPause();
    }

    private void BeginInactivityPause()
    {
        Interlocked.Increment(ref _inactivityPauseCount);
    }

    private void EndInactivityPause()
    {
        if (Interlocked.Decrement(ref _inactivityPauseCount) < 0)
            _inactivityPauseCount = 0; // 안전 장치
    }

    #endregion

    #region Utilities

    /// <summary> 파괴 토큰 접근자(지연 초기화) </summary>
    protected CancellationToken DestroyToken
    {
        get
        {
            if (_destroyTokenInitialized) return _destroyToken;
            try
            {
                _destroyToken = this.GetCancellationTokenOnDestroy();
                _destroyTokenInitialized = true;
                return _destroyToken;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SceneManager_Base] DestroyToken-> 토큰 생성 중 예외: {e.Message}");
                return CancellationToken.None;
            }
        }
    }

    /// <summary> Graphic 알파를 minA~maxA로 왕복하는 핑퐁 애니메이션 시작 </summary>
    protected void StartAlphaPingPong(Graphic g, float minA, float maxA, float periodSec, ref CancellationTokenSource cts)
    {
        CancelAndDispose(ref cts);
        cts = new CancellationTokenSource();
        _ = AnimateAlphaPingPongAsync(g, minA, maxA, periodSec, cts.Token);
    }

    /// <summary> 알파 핑퐁 루틴 </summary>
    private async UniTask AnimateAlphaPingPongAsync(Graphic g, float minA, float maxA, float periodSec, CancellationToken token)
    {
        if (!g) return;
        if (periodSec <= 0f) periodSec = 1f;

        Color baseColor = g.color;
        float speed = 2f / periodSec; // 0->1->0 이 periodSec 동안
        float phase = 0f;

        float clamped = Mathf.Clamp(baseColor.a, minA, maxA);
        g.color = new Color(baseColor.r, baseColor.g, baseColor.b, clamped);

        while (!token.IsCancellationRequested)
        {
            phase += Time.deltaTime * speed;
            float t = Mathf.PingPong(phase, 1f);
            float a = Mathf.Lerp(minA, maxA, t);
            g.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
            await UniTask.Yield();
        }
    }

    /// <summary> CTS 정리 헬퍼 </summary>
    protected static void CancelAndDispose(ref CancellationTokenSource cts)
    {
        if (cts == null) return;
        try
        {
            cts.Cancel();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SceneManager_Base] CancelAndDispose-> Cancel 중 예외: {e.Message}");
        }
        finally
        {
            cts.Dispose();
            cts = null;
        }
    }

    #endregion

    #region Arduino Hooks

    /// <summary> 아두이노 라인 수신 구독 시도 </summary>
    private void TryHookArduino()
    {
        ArduinoInputManager inst = ArduinoInputManager.Instance;
        if (inst == null) return;
        UnhookArduino();
        inst.LineReceived += OnArduinoLineReceived;
    }

    /// <summary> 아두이노 라인 수신 구독 해제 </summary>
    private void UnhookArduino()
    {
        ArduinoInputManager inst = ArduinoInputManager.Instance;
        if (inst != null)
        {
            inst.LineReceived -= OnArduinoLineReceived;
        }
    }

    /// <summary> 버튼 입력 감지 -> 무입력 타이머 리셋 플래그 세팅 </summary>
    private void OnArduinoLineReceived(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        string s = line.Trim();
        bool isButton = s.IndexOf("BTN", StringComparison.OrdinalIgnoreCase) >= 0;
        if (isButton) Interlocked.Exchange(ref _arduinoTouchedFlag, 1);
    }

    #endregion
}