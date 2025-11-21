using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

[Serializable]
public class RMSetting
{
    public float videoFadeTime;

    public ImageSetting background;
    public ImageSetting subBg;
    public ImageSetting subRocket;

    public ImageSetting rocketInfoImage;
    public ImageSetting satelliteInfoImage;

    public VideoSetting[] locationVideo;
    public VideoSetting[] rocketMakeVideo;
}

/// <summary>
/// 우주발사체를 다단(3단)으로 제작하는 이유 씬 매니저
/// 발사체/위성 정보 팝업 -> 발사 장소 영상 -> 발사체 다단 제작 영상 -> 다음 씬
/// </summary>
public class RMManager : SceneManager_Base<RMSetting>
{
    // JSON 경로
    protected override string JsonPath => "JSON/RMSetting.json";

    [Header("UI")]
    [SerializeField] private GameObject backgroundImage;
    [SerializeField] private GameObject rocketInfoImage;
    [SerializeField] private GameObject satelliteInfoImage;

    [SerializeField] private GameObject videoPlayerObject;
    [SerializeField] private GameObject subBgImage;
    [SerializeField] private GameObject subRocketImage;

    private enum Phase
    {
        SelectRocket,
        SelectSatellite,
        Location,
        PlayingMake,
        Done
    }

    private Phase _phase = Phase.SelectRocket;

    // 비디오
    private VideoPlayer _vp;
    private RawImage _raw;
    private AudioSource _audio;

    // 전환/루프 대기
    private bool _isSwitching;

    // 배열 인덱스
    private int _locIndex;
    private int _makeIndex;

    private float _videoFadeTime;
    private RenderTexture _lastRT;

    private CancellationTokenSource _skipCts;

    #region Unity lifecycle

    /// <summary> 리소스/이벤트/토큰 정리 </summary>
    protected override void OnDisable()
    {
        base.OnDisable();
        try
        {
            if (_vp != null)
            {
                _vp.loopPointReached -= OnLocationEnded;
                _vp.loopPointReached -= OnMakeEnded;
                if (_vp.isPlaying) _vp.Stop();
            }

            if (_audio != null) _audio.Stop();

            CancelAndDispose(ref _skipCts);

            // RawImage가 잡고 있는 텍스처 분리 후 파기
            if (_raw != null) _raw.texture = null;

            if (_lastRT != null)
            {
                if (_lastRT.IsCreated()) _lastRT.Release();
                Destroy(_lastRT);
                _lastRT = null;
            }
        }
        catch (Exception e)
        {
            LogUtil.LogError(nameof(RMManager), nameof(OnDisable), e.ToString());
        }
    }

    /// <summary> 초기 세팅 및 입력 루프 시작 </summary>
    protected override async UniTask Init()
    {
        if (videoPlayerObject == null)
        {
            LogUtil.LogError(nameof(RMManager), nameof(Init), "videoPlayerObject is not assigned");
            return;
        }

        _vp = videoPlayerObject.GetComponent<VideoPlayer>();
        _raw = videoPlayerObject.GetComponent<RawImage>();
        _audio = videoPlayerObject.GetComponent<AudioSource>();

        if (_vp == null || _raw == null)
        {
            LogUtil.LogError(nameof(RMManager), nameof(Init), "VideoPlayer or RawImage component missing on videoPlayerObject");
            return;
        }

        _videoFadeTime = Mathf.Max(0f, setting.videoFadeTime);

        // 고정 이미지 세팅
        SettingImageObject(backgroundImage, setting.background);
        SettingImageObject(rocketInfoImage, setting.rocketInfoImage);
        SettingImageObject(satelliteInfoImage, setting.satelliteInfoImage);
        SettingImageObject(subBgImage, setting.subBg);
        SettingImageObject(subRocketImage, setting.subRocket);

        // 초기 표시: 로켓 정보만 보이기
        if (rocketInfoImage != null) rocketInfoImage.SetActive(true);
        if (satelliteInfoImage != null) satelliteInfoImage.SetActive(false);

        // 비디오 오브젝트 초기 상태 비활성/알파 0
        if (_raw != null)
        {
            Color c = _raw.color;
            _raw.color = new Color(c.r, c.g, c.b, 0f);
            _raw.texture = null;
        }

        videoPlayerObject.SetActive(false);

        // 널 가드
        if (setting.locationVideo == null) setting.locationVideo = Array.Empty<VideoSetting>();
        if (setting.rocketMakeVideo == null) setting.rocketMakeVideo = Array.Empty<VideoSetting>();

        _locIndex = 0;
        _makeIndex = 0;

        // 시작 페이드 아웃
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage2, fadeImage3 });

        // LED 효과 시작
        ArduinoInputManager.Instance?.SetLedAll(true);
        StartBlinkGreenAsync(500, 160);

        // 초기 상태는 로켓 정보 단계
        _phase = Phase.SelectRocket;

        // 입력 루프: 아두이노 버튼 아무거나 + 디버그용 키 입력
        while (_phase != Phase.Done)
        {
            if (!ArduinoInputManager.Instance) return;

            bool arduinoPressed = ArduinoInputManager.Instance.TryConsumeAnyPress(out _);
            bool keyPressed =
                Input.GetKeyDown(KeyCode.Space) ||
                Input.GetKeyDown(KeyCode.Return) ||
                Input.GetKeyDown(KeyCode.DownArrow);

            if (arduinoPressed || keyPressed)
            {
                if (await ConfirmAsync()) break;
            }

            ArduinoInputManager.Instance.FlushAll();
            await UniTask.Yield();
        }
    }

    #endregion

    #region Selection / Confirm

    /// <summary>
    /// 버튼 입력 처리
    /// 1단계: 로켓 정보 -> 위성 정보
    /// 2단계: 위성 정보 -> 장소 영상 재생
    /// </summary>
    private async UniTask<bool> ConfirmAsync()
    {
        if (!canInput) return false;

        // 1번째 입력: 로켓 정보 -> 위성 정보
        if (_phase == Phase.SelectRocket)
        {
            if (rocketInfoImage != null) rocketInfoImage.SetActive(false);
            if (satelliteInfoImage != null) satelliteInfoImage.SetActive(true);

            _phase = Phase.SelectSatellite;
            return false; // 아직 영상 안 돌림, 입력 루프 유지
        }

        // 2번째 입력: 위성 정보 -> 장소 영상 시퀀스 시작
        if (_phase == Phase.SelectSatellite)
        {
            _phase = Phase.Location;

            // LED 및 효과 정리
            StopLedEffects();
            ArduinoInputManager.Instance?.SetLedAll(false);
            LedStrip.Range(0, 9, 255, 0, 0);

            // 정보 이미지 숨김
            if (rocketInfoImage != null) rocketInfoImage.SetActive(false);
            if (satelliteInfoImage != null) satelliteInfoImage.SetActive(false);

            // 장소 영상 시퀀스 시작
            await StartLocationSequenceAsync();
            return true; // 입력 루프 탈출
        }

        // 그 외 단계에서는 별 처리 없음
        return false;
    }

    #endregion

    #region Video Sequences (arrays)

    /// <summary> 장소 영상 배열 시퀀스 시작 </summary>
    private async UniTask StartLocationSequenceAsync()
    {
        _locIndex = 0;

        if (setting.locationVideo == null || setting.locationVideo.Length == 0)
        {
            await StartMakeSequenceAsync();
            return;
        }

        await SwitchAndPlayNextAsync(setting.locationVideo[_locIndex], true);
    }

    /// <summary> 제작 영상 배열 시퀀스 시작 </summary>
    private async UniTask StartMakeSequenceAsync()
    {
        _phase = Phase.PlayingMake;
        _makeIndex = 0;

        if (setting.rocketMakeVideo == null || setting.rocketMakeVideo.Length == 0)
        {
            _phase = Phase.Done;
            await LoadSceneAsync(5, new[] { fadeImage1, fadeImage2, fadeImage3 });
            return;
        }

        await SwitchAndPlayNextAsync(setting.rocketMakeVideo[_makeIndex], true);
    }

    /// <summary>
    /// 다음 비디오로 전환 후 재생
    /// withFade=true면 화면 덮고 세팅/재생 후 복원(안전), false면 즉시 전환
    /// Loop 영상이면 사용자 입력 대기를 걸고, 아니면 자연 종료 이벤트로 다음 처리
    /// </summary>
    private async UniTask SwitchAndPlayNextAsync(VideoSetting next, bool withFade)
    {
        if (_isSwitching) return;
        _isSwitching = true;

        // 이전 Loop 대기 정리
        CancelAndDispose(ref _skipCts);
        inputReceived = false;

        bool holdLastFrame = !withFade;

        if (withFade)
            await FadeImageAsync(0f, 1f, fadeTime, new[] { fadeImage1 });

        // 비디오 오브젝트 활성화 및 Rect 적용
        if (!videoPlayerObject.activeSelf) videoPlayerObject.SetActive(true);

        if (videoPlayerObject.TryGetComponent(out RectTransform rtx))
        {
            UIUtility.ApplyRect(
                rtx,
                size: next.size,
                anchoredPos: new Vector2(next.position.x, -next.position.y),
                rotation: Vector3.zero
            );
        }

        // 현재 프레임 고정
        if (_vp != null)
        {
            _vp.Pause();
            _vp.playbackSpeed = 0f;
        }

        // RT 준비: 동일 사이즈면 재사용, 다르면 새 RT를 미리 VideoPlayer에만 연결
        Vector2Int desired = new Vector2Int(
            Mathf.RoundToInt(next.size.x),
            Mathf.RoundToInt(next.size.y)
        );

        RenderTexture keepShowing = _raw != null ? _raw.texture as RenderTexture : null;
        RenderTexture rtForNext = VideoManager.Instance.EnsureRenderTexture(
            _vp,
            _raw,
            desired,
            reuseIfSame: holdLastFrame
        );

        // 다음 영상 준비
        string url = VideoManager.Instance.ResolvePlayableUrl(next.fileName);
        bool isLoop = IsLoopClip(next);
        double timeout = (next.fileName?.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ?? false)
            ? 20.0
            : 10.0;

        if (_vp != null)
        {
            _vp.loopPointReached -= OnLocationEnded;
            _vp.loopPointReached -= OnMakeEnded;
        }

        bool ok = await VideoManager.Instance.PrepareAndPlayAsync(
            _vp,
            url,
            _audio,
            next.volume,
            DestroyToken,
            timeout
        );

        if (!ok)
        {
            LogUtil.LogError(nameof(RMManager), nameof(SwitchAndPlayNextAsync), $"Prepare failed: {url}");
            _isSwitching = false;

            if (withFade)
                await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1 });

            return;
        }

        if (!isLoop)
        {
            if (_phase == Phase.Location) _vp.loopPointReached += OnLocationEnded;
            if (_phase == Phase.PlayingMake) _vp.loopPointReached += OnMakeEnded;
        }

        BindInactivityPolicyToVideo(_vp, isLoop, DestroyToken);

        // 첫 프레임 생성까지 가드 대기
        int guard = 0;
        while (guard++ < 5 && _vp != null && _vp.texture == null && _vp.frame <= 0)
            await UniTask.Yield();

        // 화면에 스왑 (사이즈 동일 재사용이면 이미 보이는 중이므로 스왑 불필요)
        if (_raw != null && rtForNext != null && !ReferenceEquals(keepShowing, rtForNext))
        {
            if (_lastRT != null && _lastRT != rtForNext && _lastRT != keepShowing)
            {
                if (_lastRT.IsCreated()) _lastRT.Release();
                Destroy(_lastRT);
            }

            _raw.texture = rtForNext;
            _lastRT = rtForNext;
        }

        // RawImage 알파 페이드 인
        bool needAlphaFadeIn = withFade || keepShowing == null;
        if (needAlphaFadeIn && _raw != null)
        {
            float tIn = 0f;
            while (tIn < _videoFadeTime)
            {
                tIn += Time.deltaTime;
                float a = Mathf.Clamp01(tIn / _videoFadeTime);

                Color cIn = _raw.color;
                _raw.color = new Color(cIn.r, cIn.g, cIn.b, a);
                await UniTask.Yield();
            }
        }

        // 재생 재개/루프 설정
        if (_vp != null)
        {
            _vp.isLooping = isLoop;
            _vp.playbackSpeed = 1f;
        }

        // Loop라면 사용자 입력 대기 태스크 기동
        if (isLoop)
        {
            ArduinoInputManager.Instance?.SetLedAll(true);
            StartBlinkGreenAsync(500, 160);

            _skipCts = new CancellationTokenSource();
            int loc = _locIndex;
            int make = _makeIndex;
            WaitSkipThenProceedAsync(_skipCts.Token, loc, make).Forget();
        }

        if (withFade)
            await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1 });

        _isSwitching = false;
    }

    /// <summary> Loop 영상에서 사용자 입력이 들어오면 현재 시퀀스의 다음 아이템으로 진행 </summary>
    private async UniTask WaitSkipThenProceedAsync(CancellationToken token, int locIndexAtStart, int makeIndexAtStart)
    {
        if (ArduinoInputManager.Instance != null) ArduinoInputManager.Instance.FlushAll();
        await UniTask.Yield();

        while (true)
        {
            if (token.IsCancellationRequested) return;
            if (_phase != Phase.Location && _phase != Phase.PlayingMake) return;

            bool arduinoPressed =
                ArduinoInputManager.Instance != null &&
                ArduinoInputManager.Instance.TryConsumeAnyPress(out _);

            if (arduinoPressed || TryConsumeSingleInput())
                break;

            await UniTask.Yield();
        }

        if (token.IsCancellationRequested) return;

        // 진행 시점 레이스 방지
        if (_phase == Phase.Location && locIndexAtStart != _locIndex) return;
        if (_phase == Phase.PlayingMake && makeIndexAtStart != _makeIndex) return;

        // Loop 종료 처리
        if (_vp != null)
        {
            _vp.loopPointReached -= OnLocationEnded;
            _vp.loopPointReached -= OnMakeEnded;
            _vp.Stop();
        }

        StopLedEffects();
        ArduinoInputManager.Instance?.SetLedAll(false);
        LedStrip.Range(0, 9, 255, 0, 0);

        CancelAndDispose(ref _skipCts);

        // 다음으로 진행
        if (_phase == Phase.Location)
        {
            _locIndex++;
            if (setting.locationVideo != null && _locIndex < setting.locationVideo.Length)
            {
                await SwitchAndPlayNextAsync(setting.locationVideo[_locIndex], false);
            }
            else
            {
                await StartMakeSequenceAsync();
            }
        }
        else if (_phase == Phase.PlayingMake)
        {
            _makeIndex++;
            if (setting.rocketMakeVideo != null && _makeIndex < setting.rocketMakeVideo.Length)
            {
                await SwitchAndPlayNextAsync(setting.rocketMakeVideo[_makeIndex], false);
            }
            else
            {
                _phase = Phase.Done;
                await LoadSceneAsync(5, new[] { fadeImage1, fadeImage2, fadeImage3 });
            }
        }
    }

    /// <summary> 장소 영상 하나가 자연 종료되면 다음 장소(또는 제작 시퀀스)로 </summary>
    private async void OnLocationEnded(VideoPlayer vp)
    {
        try
        {
            if (_vp != null) _vp.loopPointReached -= OnLocationEnded;

            _locIndex++;
            if (setting.locationVideo != null && _locIndex < setting.locationVideo.Length)
            {
                await SwitchAndPlayNextAsync(setting.locationVideo[_locIndex], false);
            }
            else
            {
                await StartMakeSequenceAsync();
            }
        }
        catch (Exception e)
        {
            LogUtil.LogError(nameof(RMManager), nameof(OnLocationEnded), e.ToString());
        }
    }

    /// <summary> 제작 영상 하나가 자연 종료되면 다음 제작(또는 다음 씬)으로 </summary>
    private async void OnMakeEnded(VideoPlayer vp)
    {
        try
        {
            if (_vp != null) _vp.loopPointReached -= OnMakeEnded;

            _makeIndex++;
            if (setting.rocketMakeVideo != null && _makeIndex < setting.rocketMakeVideo.Length)
            {
                await SwitchAndPlayNextAsync(setting.rocketMakeVideo[_makeIndex], false);
            }
            else
            {
                _phase = Phase.Done;
                await LoadSceneAsync(5, new[] { fadeImage1, fadeImage2, fadeImage3 });
            }
        }
        catch (Exception e)
        {
            LogUtil.LogError(nameof(RMManager), nameof(OnMakeEnded), e.ToString());
        }
    }

    /// <summary>
    /// VideoSetting이 루프형인지 판단
    /// - name 이 "…Loop"로 끝나거나
    /// - fileName(확장자 제거)이 "…Loop"로 끝나면 true
    /// </summary>
    private static bool IsLoopClip(VideoSetting vs)
    {
        if (vs == null) return false;

        string n = string.IsNullOrEmpty(vs.name) ? string.Empty : vs.name;
        if (n.EndsWith("Loop", StringComparison.OrdinalIgnoreCase)) return true;

        string fn = string.IsNullOrEmpty(vs.fileName) ? string.Empty : vs.fileName;
        string stem = string.IsNullOrEmpty(fn) ? string.Empty : Path.GetFileNameWithoutExtension(fn);
        return stem.EndsWith("Loop", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Debug

    /// <summary>
    /// 디버그 스킵 입력 처리
    /// - 모든 영상/대기 상태 정리 -> 즉시 다음 씬 이동
    /// </summary>
    protected override void OnDebugSkip()
    {
        try
        {
            if (_phase == Phase.Done) return;

            // 루프 대기 태스크 취소
            CancelAndDispose(ref _skipCts);

            // 비디오 이벤트 해제 및 정지
            if (_vp != null)
            {
                _vp.loopPointReached -= OnLocationEnded;
                _vp.loopPointReached -= OnMakeEnded;
                if (_vp.isPlaying) _vp.Stop();
            }

            // LED/이펙트 정리
            StopLedEffects();
            ArduinoInputManager.Instance?.SetLedAll(false);
            LedStrip.Range(0, 9, 255, 0, 0);

            // 상태 정리 후 다음 씬 전환
            _isSwitching = false;
            _phase = Phase.Done;

            int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 5;
            LoadSceneAsync(target, new[] { fadeImage1, fadeImage2, fadeImage3 }).Forget();
        }
        catch (Exception e)
        {
            LogUtil.LogError(nameof(RMManager), nameof(OnDebugSkip), e.ToString());
        }
    }

    #endregion
}
