using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

[Serializable]
public class NewtonSetting
{
    public ImageSetting background;
    public ImageSetting infoImage1;
    public ImageSetting infoImage2;

    public VideoSetting introVideo;
    public VideoSetting[] newtonsRuleVideos;
}

/// <summary> 뉴턴의 제 1~3법칙 씬 관리 매니저 </summary>
public class NewtonManager : SceneManager_Base<NewtonSetting>
{
    [Header("UI")] 
    [SerializeField] private GameObject titleImage;
    [SerializeField] private GameObject infoImage1;
    [SerializeField] private GameObject infoImage2;
    [SerializeField] private GameObject videoPlayerObject;

    protected override string JsonPath => "JSON/NewtonSetting.json";

    // videoPlayerObject의 컴포넌트
    private VideoPlayer _vp;
    private RawImage _raw;
    private AudioSource _audio;

    private bool _isSwitching;
    private RenderTexture _lastRT;

    private enum Phase
    {
        Intro,
        RuleSeq,
        Done
    }

    private Phase _phase;

    private VideoSetting[] _ruleSeq; // 뉴턴의 법칙 비디오를 저장하는 배열
    private int _ruleIndex;

    

    // 루프 영상에서 사용자 입력을 감시하는 태스크를 제어하기 위한 토큰
    private CancellationTokenSource _skipCts;

    protected override void OnDisable()
    {
        CancelAndDispose(ref _skipCts);

        if (_vp != null)
        {
            _vp.loopPointReached -= OnVideoEnded;
            _vp.Stop();
        }

        // 마지막 RenderTexture 해제
        if (_lastRT != null)
        {
            if (_lastRT.IsCreated()) _lastRT.Release();
            Destroy(_lastRT);
            _lastRT = null;
        }
    }

    protected override async UniTask Init()
    {
        if (!videoPlayerObject) Debug.LogError("[NewtonManager] videoPlayerObject is not assigned");

        _vp = videoPlayerObject.GetComponent<VideoPlayer>();
        _raw = videoPlayerObject.GetComponent<RawImage>();
        _audio = videoPlayerObject.GetComponent<AudioSource>();

        // 타이틀/안내 이미지 설정
        SettingImageObject(titleImage, setting.background);
        SettingImageObject(infoImage1, setting.infoImage1);
        SettingImageObject(infoImage2, setting.infoImage2);
        if (infoImage2) infoImage2.SetActive(false);

        // 뉴턴의 법칙 비디오 저장
        _ruleSeq = setting.newtonsRuleVideos;
        _ruleIndex = 0;
        _phase = Phase.Intro;
        
        StopLedEffects();
        ArduinoInputManager.Instance?.SetLedAll(false);
        LedStrip.Range(0, 9, 255, 0, 0);

        // 인트로 세팅 및 재생  
        await SettingVideoObject(videoPlayerObject, setting.introVideo, _vp, _raw, _audio);
        _vp.loopPointReached -= OnVideoEnded;
        _vp.loopPointReached += OnVideoEnded;

        TurnCamera3Async(this.GetCancellationTokenOnDestroy()).Forget();
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage3 });
    }

    private async void OnVideoEnded(VideoPlayer vp)
    {
        try
        {
            if (_vp != null) _vp.loopPointReached -= OnVideoEnded;

            if (_phase == Phase.Intro)
            {
                _phase = Phase.RuleSeq;
                _ruleIndex = 0;

                if (_ruleSeq == null || _ruleSeq.Length == 0)
                {
                    _phase = Phase.Done;
                    await GoNextSceneAsync();
                    return;
                }

                await SwitchAndPlayNextAsync(_ruleSeq[_ruleIndex], true); // 인트로 → 첫 법칙: 페이드 O
            }
            else if (_phase == Phase.RuleSeq)
            {
                if (_ruleSeq == null || _ruleSeq.Length == 0 || _ruleIndex < 0 || _ruleIndex >= _ruleSeq.Length)
                {
                    _phase = Phase.Done;
                    await GoNextSceneAsync();
                    return;
                }

                if (IsLoopClip(_ruleSeq[_ruleIndex]))
                {
                    if (_vp != null) _vp.loopPointReached += OnVideoEnded; // 루프: 자동 진행 없음
                    return;
                }

                _ruleIndex++;
                while (_ruleIndex < _ruleSeq.Length && _ruleSeq[_ruleIndex] == null)
                    _ruleIndex++;

                if (_ruleIndex < _ruleSeq.Length)
                {
                    await SwitchAndPlayNextAsync(_ruleSeq[_ruleIndex], false); // 법칙 → 법칙: 페이드 X
                }
                else
                {
                    _phase = Phase.Done;
                    await GoNextSceneAsync();
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[NewtonManager] Video player ended exception: {e}");
        }
    }

    /// <summary>
    /// 다음 비디오로 전환.
    /// - 기본: 영상이 끝나면 자동으로 다음으로 이동.
    /// - 이름 또는 파일명이 '...Loop'로 끝나는 영상: 계속 반복 재생하며, 사용자 입력 시 다음으로 이동.
    /// </summary>
    private async UniTask SwitchAndPlayNextAsync(VideoSetting next, bool withFade)
{
    if (_isSwitching) return;
    _isSwitching = true;

    CancelAndDispose(ref _skipCts);

    if (infoImage2) infoImage2.SetActive(false);
    inputReceived = false;

    StopLedEffects();
    ArduinoInputManager.Instance?.SetLedAll(false);
    LedStrip.Range(0, 9, 255, 0, 0);

    bool holdLastFrame = !withFade;

    if (withFade)
        await FadeImageAsync(0f, 1f, fadeTime, new[] { fadeImage1 });

    // 현재 프레임 고정
    if (_vp != null)
    {
        _vp.loopPointReached -= OnVideoEnded;
        try
        {
            _vp.Pause();
            _vp.playbackSpeed = 0f;
        }
        catch (ObjectDisposedException)
        {
            // 이미 파괴/해제된 경우 무시
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[NewtonManager] Pause/playbackSpeed guard failed: {e.Message}");
        }
    }

    Vector2Int desired = new Vector2Int(Mathf.RoundToInt(next.size.x), Mathf.RoundToInt(next.size.y));
    RenderTexture keepShowing = _raw != null ? _raw.texture as RenderTexture : null;
    RenderTexture rtForNext = VideoManager.Instance.EnsureRenderTexture(_vp, _raw, desired, reuseIfSame: holdLastFrame);

    string url = VideoManager.Instance.ResolvePlayableUrl(next.fileName);
    bool isLoop = IsLoopClip(next);
    double timeout = next.fileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ? 20.0 : 10.0;

    bool ok = await VideoManager.Instance.PrepareAndPlayAsync(
        _vp, url, _audio, next.volume, this.GetCancellationTokenOnDestroy(), timeout);

    if (!ok)
    {
        Debug.LogError($"[NewtonManager] Prepare failed: {url}");
        _isSwitching = false;
        if (withFade) await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1 });
        return;
    }

    int guard = 0;
    while (guard++ < 5 && _vp != null && _vp.texture == null && _vp.frame <= 0)
        await UniTask.Yield();

    // 화면 스왑 + 이전 RT 해제
    if (_raw != null && rtForNext != null && keepShowing != rtForNext)
    {
        // 이전에 트래킹하던 RT가 있고, 이번에 보여줄 텍스처와도 다르면 해제
        if (_lastRT != null && _lastRT != rtForNext && _lastRT != keepShowing)
        {
            if (_lastRT.IsCreated()) _lastRT.Release();
            Destroy(_lastRT);
        }

        _raw.texture = rtForNext;  // 스왑
        _lastRT = rtForNext;       // 새 RT 기억
    }

    if (_vp != null)
    {
        _vp.isLooping = isLoop;
        _vp.playbackSpeed = 1f;
        _vp.loopPointReached += OnVideoEnded;
    }

    if (isLoop)
    {
        if (infoImage2) infoImage2.SetActive(true);
        ArduinoInputManager.Instance?.SetLedAll(true);
        StartBlinkGreenAsync(500, 160);

        _skipCts = new CancellationTokenSource();
        int capturedIndex = _ruleIndex;
        _ = WaitSkipThenProceedAsync(_skipCts.Token, capturedIndex);
    }

    if (withFade)
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1 });

    _isSwitching = false;
}


    /// <summary>
    /// 루프 영상에서 사용자 입력을 받으면 다음 영상으로 진행.
    /// 루프가 아닌 경우 이 함수는 보통 호출되지 않음.
    /// </summary>
    private async UniTask WaitSkipThenProceedAsync(CancellationToken token, int ruleIndexAtStart)
    {
        if (ArduinoInputManager.Instance != null) ArduinoInputManager.Instance.FlushAll();
        await UniTask.Yield();

        while (true)
        {
            if (token.IsCancellationRequested) return;
            if (_phase != Phase.RuleSeq) return;

            bool arduinoPressed = ArduinoInputManager.Instance != null &&
                                  ArduinoInputManager.Instance.TryConsumeAnyPress(out _);

            if (arduinoPressed || TryConsumeSingleInput())
                break;

            await UniTask.Yield();
        }

        if (token.IsCancellationRequested) return;
        if (ruleIndexAtStart != _ruleIndex) return;

        if (_vp != null)
        {
            _vp.loopPointReached -= OnVideoEnded;
            _vp.Stop();
        }
    
        StopLedEffects();
        ArduinoInputManager.Instance?.SetLedAll(false);
        LedStrip.Range(0, 9, 255, 0, 0);
        
        if (infoImage2) infoImage2.SetActive(false);
        CancelAndDispose(ref _skipCts);

        _ruleIndex++;
        if (_ruleIndex < _ruleSeq.Length)
        {
            await SwitchAndPlayNextAsync(_ruleSeq[_ruleIndex], false);
        }
        else
        {
            _phase = Phase.Done;
            await GoNextSceneAsync();
        }
    }

    /// <summary> 다음 씬으로 전환 </summary>
    private UniTask GoNextSceneAsync()
    {
        int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 3;
        return LoadSceneAsync(target, new[] { fadeImage1, fadeImage3 });
    }

    // 씬 전환 직전 클래스의 비동기/이벤트 정리
    protected override void OnBeforeSceneUnload()
    {
        // 루프 입력 대기 토큰 정리
        CancelAndDispose(ref _skipCts);

        // VideoPlayer 이벤트 해제 및 정지
        if (_vp)
        {
            _vp.loopPointReached -= OnVideoEnded;
            _vp.Stop();
        }

        if (infoImage2) infoImage2.SetActive(false);
        ArduinoInputManager.Instance?.SetLedAll(false);
    }

    /// <summary>
    /// VideoSetting이 루프형인지 판단:
    /// - VideoSetting.name 이 "…Loop"로 끝나거나
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
    
    /// <summary>
    /// 디버그 스킵 입력 처리
    /// - 모든 영상 재생/대기 상태를 종료하고 바로 다음 씬으로 이동
    /// </summary>
    protected override void OnDebugSkip()
    {
        try
        {
            // 루프 입력 대기 태스크 취소
            CancelAndDispose(ref _skipCts);

            // VideoPlayer 이벤트 해제 및 정지
            if (_vp)
            {
                _vp.loopPointReached -= OnVideoEnded;
                if (_vp.isPlaying) _vp.Stop();
            }

            // 안내 UI 및 LED 효과 정리
            if (infoImage2) infoImage2.SetActive(false);
            
            StopLedEffects();
            ArduinoInputManager.Instance?.SetLedAll(false);
            LedStrip.Range(0, 9, 255, 0, 0);

            // 상태 플래그 정리 후 다음 씬
            _isSwitching = false;
            _phase = Phase.Done;

            _ = GoNextSceneAsync();
        }
        catch (Exception e)
        {
            Debug.LogError($"[NewtonManager] OnDebugSkip Exception: {e}");
        }
    }
}