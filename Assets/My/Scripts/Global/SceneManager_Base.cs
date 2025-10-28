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

    [Header("Camera")] [SerializeField] protected Camera mainCamera; // Display1
    [SerializeField] protected Camera camera2; // Display2
    [SerializeField] protected Camera camera3; // Display3

    [Header("Canvas")] [SerializeField] protected Canvas mainCanvas;
    [SerializeField] protected Canvas subCanvas;
    [SerializeField] protected Canvas verticalCanvas;

    [Header("Fade Images")] [SerializeField]
    protected Image fadeImage1; // Display1 Fade

    [SerializeField] protected Image fadeImage2; // Display2 Fade
    [SerializeField] protected Image fadeImage3; // Display3 Fade

    [Header("Scene Flow")] [Tooltip("현재 씬에서 다음 씬으로 넘어갈 때 사용할 빌드 인덱스")] [SerializeField]
    protected int nextSceneBuildIndex = -1;

    [Tooltip("이 씬에서 비활성 타임아웃을 적용할지 여부")] [SerializeField]
    private bool useInactivityTimeout = true;

    // ========= LED Effects (공통) =========
    private CancellationTokenSource _ledCts;
    private int _blinkHalfPeriodMs = 300; // 초록 깜빡임 반주기(기본 300ms)

    #endregion

    #region Settings / State

    private bool _isLoading;

    [NonSerialized] protected T setting;
    private Settings _globalSettings; // JsonLoader.Instance.settings 캐시

    protected float fadeTime; // 페이드 시간
    protected bool canInput; // 페이드 중/전환 중 입력 방지
    protected bool inputReceived; // 중복 입력 방지

    private float _inactivityTimer; // 무입력 시간 누적
    private float _inactivityThreshold; // Scene0로 복귀 임계값
    private float _camera3TurnSpeed; // 회전 속도

    protected int buttonDelayTime;
    private float _lastDebugSkipTime;
    private const float DebugSkipCooldown = 0.25f; // 너무 빠른 중복 입력 방지

    private readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>(); // 버튼 스프라이트 ON/OFF 용
    private readonly Dictionary<GameObject, CancellationTokenSource> _anchoredYAnimCts = new Dictionary<GameObject, CancellationTokenSource>();

    protected abstract string JsonPath { get; }

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

            await InitSafe();
        }
        catch (OperationCanceledException)
        {
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
                if (!_isLoading)
                    LoadSceneAsync(0, new[] { fadeImage1, fadeImage2, fadeImage3 }).Forget();
            }
        }

        if (IsAnyUserInputDown()) _inactivityTimer = 0f;
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

    protected virtual void OnDisable()
    {
        StopLedEffects();
    }

    #endregion

    #region Template Methods (for children)

    /// <summary> 자식에서 구현할 실제 초기화. 안전 래핑은 InitSafe가 담당. </summary>
    protected abstract UniTask Init();

    // 씬 로드 직후 초기화용
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _isLoading = false;
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

    #region Camera / Fade / Scene

    /// <summary> Display3 카메라를 일정 속도로 계속 회전시킴 </summary>
    protected async UniTaskVoid TurnCamera3Async(CancellationToken token)
    {
        if (!camera3)
        {
            Debug.LogError("[SceneManager] camera3 is not assigned");
            return;
        }

        try
        {
            while (!token.IsCancellationRequested) //매 프레임마다 회전
            {
                camera3.transform.Rotate(Vector3.up, _camera3TurnSpeed * Time.deltaTime, Space.World);
                await UniTask.Yield(PlayerLoopTiming.Update, token); // 다음 프레임까지 대기
            }
        }
        catch (OperationCanceledException)
        {
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
            await UniTask.Yield(); // 다음 프레임까지 양보
        }

        SetAlpha(from, 0f);
        fromGo.SetActive(false);
        SetAlpha(to, 1f);
    }

    /// <summary> 페이드 후 씬 로드 (async) </summary>
    protected async UniTask LoadSceneAsync(int buildIndex, Image[] fadeImages)
    {
        // 중복 로드 가드
        if (_isLoading) return;
        _isLoading = true;

        // 공통 정리 (LED/입력/코루틴 등)
        OnBeforeSceneUnload();

        // sceneLoaded 핸들러 등록 여부 트래킹
        SceneManager.sceneLoaded += OnSceneLoaded;

        // 파괴/종료 토큰
        CancellationToken cancel = this.GetCancellationTokenOnDestroy();

        // 페이드아웃
        if (fadeImages != null && fadeImages.Length > 0)
        {
            try
            {
                await FadeImageAsync(0f, 1f, Mathf.Max(0f, fadeTime), fadeImages);
            }
            catch (OperationCanceledException)
            {
                // 정리 후 조기 반환
                SceneManager.sceneLoaded -= OnSceneLoaded;
                _isLoading = false;
                return;
            }
        }

        // 로딩 시작
        AsyncOperation op = null;
        try
        {
            op = SceneManager.LoadSceneAsync(buildIndex);
            if (op == null)
            {
                Debug.LogError($"[SceneManager_Base] LoadSceneAsync returned null (buildIndex: {buildIndex})");
                // 정리 후 조기 반환
                SceneManager.sceneLoaded -= OnSceneLoaded;
                _isLoading = false;
                return;
            }

            op.allowSceneActivation = false;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SceneManager_Base] Exception starting LoadSceneAsync: {e}");
            // 정리 후 조기 반환
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _isLoading = false;
            return;
        }

        // 로딩 완료 대기 (0.9 == 준비 완료)
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
            // 파괴/취소 시 정리
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _isLoading = false;
        }
    }

    /// <summary> 씬 전환 직전 클래스의 비동기/이벤트 정리 </summary>
    protected virtual void OnBeforeSceneUnload()
    {
        canInput = false;
        inputReceived = true;

        try
        {
            StopAllCoroutines();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SceneManager_Base] StopAllCoroutines failed: {e.Message}");
        }

        // LED 효과 안전 중지
        StopLedEffects();

        try
        {
            if (ArduinoInputManager.Instance)
                ArduinoInputManager.Instance.FlushAll();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SceneManager_Base] FlushAll failed: {e.Message}");
        }

        if (!Mathf.Approximately(Time.timeScale, 1f)) Time.timeScale = 1f;
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
        if (imageObject.TryGetComponent(out Image img) &&
            imageObject.TryGetComponent(out RectTransform rt))
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

        // 외부 Task를 UniTask로 변환해 await
        await VideoManager.Instance.PrepareAndPlayAsync(vp, url, audioSource, vs.volume, this.GetCancellationTokenOnDestroy()).AsUniTask();
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
    protected async UniTask AdvanceStepAsync(GameObject fromGo, GameObject toGo, float duration)
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

    #region LED Effects (public helpers for children)

    public void PublicStartBlinkGreen(int periodMsHalf, int onBrightness)
    {
        StartBlinkGreenAsync(periodMsHalf, onBrightness);
    }

    protected void StartBlinkGreenAsync(int periodMsHalf, int onBrightness)
    {
        _blinkHalfPeriodMs = Mathf.Max(50, periodMsHalf);
        StopLedEffects();

        _ledCts = new CancellationTokenSource();
        _ = BlinkGreenLoopAsync(_ledCts.Token, onBrightness);
    }

    public void PublicStopLedEffects()
    {
        StopLedEffects();
    }

    protected void StopLedEffects()
    {
        CancelAndDispose(ref _ledCts);
    }

    private async UniTaskVoid BlinkGreenLoopAsync(CancellationToken token, int onBrightness)
    {
        bool on = true;

        while (!token.IsCancellationRequested)
        {
            if (on)
            {
                // 켬: 초록으로 채우고(필요시 밝기도 지정)
                LedStrip.Fill(0, 255, 0);
                if (onBrightness < 255)
                    LedStrip.Bright(Mathf.Clamp(onBrightness, 1, 255));
            }
            else
            {
                // 끔: 전체 소등
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

    /// <summary> Graphic 알파를 minA~maxA로 왕복(ping-pong) 애니메이션. </summary>
    private async UniTask AnimateAlphaPingPongAsync(Graphic g, float minA, float maxA, float periodSec, CancellationToken token)
    {
        if (!g) return;
        if (periodSec <= 0f) periodSec = 1f;

        Color baseColor = g.color;
        float speed = 2f / periodSec; // 0→1→0이 periodSec 동안
        float phase = 0f;

        // 초기 알파를 범위 안으로 강제
        float clamped = Mathf.Clamp(baseColor.a, minA, maxA);
        g.color = new Color(baseColor.r, baseColor.g, baseColor.b, clamped);

        while (!token.IsCancellationRequested)
        {
            phase += Time.deltaTime * speed;
            float t = Mathf.PingPong(phase, 1f); // 0..1..0
            float a = Mathf.Lerp(minA, maxA, t); // minA..maxA..minA
            g.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
            await UniTask.Yield();
        }
    }

    /// <summary> 알파 핑퐁 애니메이션 시작. 기존 토큰이 있으면 취소/해제 후 새로 시작. </summary>
    protected void StartAlphaPingPong(Graphic g, float minA, float maxA, float periodSec, ref CancellationTokenSource cts)
    {
        CancelAndDispose(ref cts);
        cts = new CancellationTokenSource();
        _ = AnimateAlphaPingPongAsync(g, minA, maxA, periodSec, cts.Token);
    }

    /// <summary> CTS 정리 헬퍼 </summary>
    protected static void CancelAndDispose(ref CancellationTokenSource cts)
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    /// <summary> 디버그 스킵 키 입력 시 공통 처리 -> 파생 클래스 훅 호출 </summary>
    private void HandleDebugSkipKey()
    {
        _inactivityTimer = 0f; // 타임아웃 리셋
        OnDebugSkip();
    }

    /// <summary>
    /// 디버그 스킵 동작 훅 -> 파생 클래스에서 "영상 정지, 다음 단계로 진행" 로직을 구현
    /// 기본 구현: 입력 래치만 세팅하여 '다음 단계 대기 루프'를 빠져나오게 함
    /// </summary>
    protected virtual void OnDebugSkip()
    {
        if (!inputReceived && canInput)
        {
            inputReceived = true;
            Debug.Log("[SceneManager_Base] Debug skip -> inputReceived = true");
        }
    }

    /// <summary> 비디오 재생 시 첫 프레임을 렌더 텍스쳐에 그리고 대기하는 헬퍼 (깜빡임 방지) </summary>
    protected async UniTask<bool> WaitFirstFrameAsync(VideoPlayer vp, RawImage ri, CancellationToken token, double maxSeconds = 2.0)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + maxSeconds;
        while (!token.IsCancellationRequested && vp != null)
        {
            // frame > 0 && texture 존재 && RawImage에도 텍스처 바인딩 완료
            if (vp.frame > 0 && vp.texture != null && ri != null && ri.texture != null)
                return true;

            if (Time.realtimeSinceStartupAsDouble >= deadline)
                break;

            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        return vp != null && vp.texture != null;
    }

    #region 버튼 스프라이트 ON/OFF

    /// <summary> StreamingAssets의 상대 경로를 받아 Sprite를 반환한다 -> 캐시 사용. 실패 시 null 반환. </summary>
    protected Sprite GetSpriteFromStreamingAssets(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            Debug.LogError("[SceneManager_Base] GetSpriteFromStreamingAssets -> path is null or empty");
            return null;
        }

        if (_spriteCache.TryGetValue(relativePath, out Sprite cached))
            return cached;

        Texture2D tex = UIUtility.LoadTextureFromStreamingAssets(relativePath);
        if (tex == null)
        {
            Debug.LogError($"[SceneManager_Base] GetSpriteFromStreamingAssets -> texture load failed: {relativePath}");
            return null;
        }

        Sprite sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        _spriteCache[relativePath] = sp;
        return sp;
    }

    /// <summary> 특정 버튼(GameObject)의 이미지를 지정한 상대 경로 스프라이트로 교체한다. 실패 시 아무 작업도 하지 않는다. </summary>
    protected void SetButtonSprite(GameObject buttonObject, string relativePath)
    {
        if (!buttonObject)
        {
            Debug.LogWarning("[SceneManager_Base] SetButtonSprite -> buttonObject is null");
            return;
        }

        Image img = buttonObject.GetComponent<Image>();
        if (!img)
        {
            Debug.LogWarning("[SceneManager_Base] SetButtonSprite -> Image component not found");
            return;
        }

        Sprite sp = GetSpriteFromStreamingAssets(relativePath);
        if (sp != null)
            img.sprite = sp;
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

    /// <summary> 여러 버튼을 한 번에 On 상태로 교체한다. </summary>
    protected void SetButtonsOn(params GameObject[] buttons)
    {
        if (buttons == null) return;
        for (int i = 0; i < buttons.Length; i++)
            SetButtonOn(buttons[i]);
    }

    /// <summary> 여러 버튼을 한 번에 Off 상태로 교체한다. </summary>
    protected void SetButtonsOff(params GameObject[] buttons)
    {
        if (buttons == null) return;
        for (int i = 0; i < buttons.Length; i++)
            SetButtonOff(buttons[i]);
    }

    #endregion

    #region Anchored Y Animation (주로 쓰로틀 애니메이션 사용)

    /// <summary> target의 RectTransform.anchoredPosition.y를 yStart -> yEnd 로 duration(초) 동안 선형 보간하여 이동 </summary>
    protected void PlayAnchoredY(GameObject target, float yStart, float yEnd, float duration, float waitAtEnd)
    {
        if (!target) return;

        // 기존 실행 취소/정리
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

    /// <summary> 실제 이동 비동기 루틴 </summary>
    private async UniTask MoveAnchoredYStepAsync(GameObject target, float yFrom, float yTo, float duration, CancellationToken token)
    {
        if (!target) return;

        RectTransform rt = target.GetComponent<RectTransform>();
        if (!rt) return;

        // 시작값 적용
        Vector2 pos = rt.anchoredPosition;
        pos.y = yFrom;
        rt.anchoredPosition = pos;

        float t = 0f;
        float d = Mathf.Max(0.001f, duration);

        while (t < d && !token.IsCancellationRequested)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / d);          // 0..1
            float y = Mathf.Lerp(yFrom, yTo, u);     // 선형 보간

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

    /// <summary> yStart -> yEnd 로만 이동을 반복. End에서 waitAtEnd 대기 후 Start로 스냅. </summary>
    private async UniTask MoveAnchoredYLoopAsync(GameObject target, float yStart, float yEnd, float duration, float waitAtEnd, CancellationToken token)
    {
        if (!target) return;

        RectTransform rt = target.GetComponent<RectTransform>();
        if (!rt) return;

        // 초기 위치를 yStart로 스냅
        Vector2 p = rt.anchoredPosition;
        p.y = yStart;
        rt.anchoredPosition = p;

        try
        {
            while (!token.IsCancellationRequested)
            {
                // 1) Start -> End 이동(단발)
                await MoveAnchoredYStepAsync(target, yStart, yEnd, duration, token);
                if (token.IsCancellationRequested) break;

                // 2) End에서 대기
                if (waitAtEnd > 0f)
                {
                    try { await UniTask.Delay(TimeSpan.FromSeconds(waitAtEnd), cancellationToken: token); }
                    catch (OperationCanceledException) { break; }
                }

                // 3) Start로 즉시 스냅(점프)
                if (!token.IsCancellationRequested)
                {
                    Vector2 snap = rt.anchoredPosition;
                    snap.y = yStart;
                    rt.anchoredPosition = snap;
                }
            }
        }
        catch (OperationCanceledException) { }
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
}