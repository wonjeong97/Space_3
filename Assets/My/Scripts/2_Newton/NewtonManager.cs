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
    public ImageSetting subImage;

    public VideoSetting introVideo;
    public VideoSetting[] newtonsRuleVideos;
}

/// <summary> 뉴턴의 제 1~3법칙 씬 관리 매니저 </summary>
public sealed class NewtonManager : SceneManager_Base<NewtonSetting>
{
    // ===== JSON 경로 =====
    protected override string JsonPath => "JSON/NewtonSetting.json";

    #region Serialized Refs

    [Header("UI")]
    [SerializeField] private GameObject videoPlayerObject; // 비디오를 표시할 GameObject (VideoPlayer + RawImage + AudioSource)
    [SerializeField] private GameObject subImage;
    
    #endregion

    #region Settings / State

    // protected 없음

    // private: 타입 → 이름 알파벳 정렬
    private AudioSource _audio;                 // 비디오 오디오 소스
    private bool _isSwitching;                  // 다음 영상으로 스위칭 중 여부
    private CancellationTokenSource _skipCts;   // 루프 구간 스킵 대기 토큰
    private RawImage _raw;                      // 비디오 출력용 RawImage
    private RenderTexture _lastRT;              // 마지막으로 표시한 RenderTexture (해제 관리)
    private VideoPlayer _vp;                    // 비디오 플레이어

    private enum Phase { Intro, RuleSeq, Done }
    private Phase _phase;                       // 현재 진행 단계

    private int _ruleIndex;                     // 법칙 비디오 인덱스
    private VideoSetting[] _ruleSeq;            // 법칙 비디오 시퀀스

    #endregion

    #region Unity Life-Cycle

    /// <summary> 씬 비활성화 시 비디오/토큰/리소스 정리 </summary>
    protected override void OnDisable()
    {   
        base.OnDisable();
        
        // 루프 입력 대기 토큰 정리
        CancelAndDispose(ref _skipCts);

        // 비디오 이벤트 해제 및 정지
        if (_vp)
        {
            try { _vp.loopPointReached -= OnVideoEnded; }
            catch (Exception e) { Debug.LogWarning("[NewtonManager] OnDisable-> 비디오 이벤트 해제 중 예외: " + e.Message); }

            try { _vp.Stop(); }
            catch (Exception e) { Debug.LogWarning("[NewtonManager] OnDisable-> 비디오 정지 중 예외: " + e.Message); }
        }

        // 마지막 RenderTexture 해제
        if (_lastRT)
        {
            try
            {
                if (_lastRT.IsCreated()) _lastRT.Release();
                Destroy(_lastRT);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[NewtonManager] OnDisable-> RenderTexture 해제 중 예외: " + e.Message);
            }
            finally { _lastRT = null; }
        }
        
        SoundManager.Instance?.ResumeBGM();
    }

    #endregion

    #region Initialization

    /// <summary> 초기 세팅: 컴포넌트 바인딩, 인트로 준비/재생, 정책 바인딩, 페이드 인 </summary>
    protected override async UniTask Init()
    {
        if (!videoPlayerObject)
        {
            Debug.LogError("[NewtonManager] Init-> 비디오 플레이어 오브젝트가 지정되지 않았습니다");
            return;
        }

        SettingImageObject(subImage, setting.subImage);
        
        // 컴포넌트 캐시
        _vp   = videoPlayerObject.GetComponent<VideoPlayer>();
        _raw  = videoPlayerObject.GetComponent<RawImage>();
        _audio= videoPlayerObject.GetComponent<AudioSource>();

        // 시퀀스 초기화
        _ruleSeq  = setting.newtonsRuleVideos;
        _ruleIndex= 0;
        _phase    = Phase.Intro;

        // LED 초기 상태
        StopLedEffects();
        try { ArduinoInputManager.Instance?.SetLedAll(false); } catch { /* 로그는 아래로 통일 */ }
        LedStrip.Range(0, 9, 255, 0, 0);

        // 인트로 준비/재생
        
        SoundManager.Instance?.PauseBGM();
        await SettingVideoObject(videoPlayerObject, setting.introVideo, _vp, _raw, _audio);

        _vp.loopPointReached -= OnVideoEnded;
        _vp.loopPointReached += OnVideoEnded;

        BindInactivityPolicyToVideo(_vp, false, DestroyToken); // 인트로: 루프 아님

        // 3번 모니터 카메라 회전 및 페이드 인
        TurnCamera3Async(DestroyToken).Forget();
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage2, fadeImage3 });
    }

    #endregion

    #region Video Flow Handlers

    /// <summary> 비디오 종료 시 다음 단계로 전환 </summary>
    private async void OnVideoEnded(VideoPlayer vp)
    {
        try
        {
            if (_vp) _vp.loopPointReached -= OnVideoEnded;

            if (_phase == Phase.Intro)
            {
                _phase = Phase.RuleSeq;
                _ruleIndex = 0;
                SoundManager.Instance.ResumeBGM();

                if (_ruleSeq == null || _ruleSeq.Length == 0)
                {
                    _phase = Phase.Done;
                    await LoadSceneAsync(3, new[] { fadeImage1, fadeImage2, fadeImage3 });
                    return;
                }

                await SwitchAndPlayNextAsync(_ruleSeq[_ruleIndex], true); // 인트로 -> 첫 법칙 (페이드 O)
            }
            else if (_phase == Phase.RuleSeq)
            {
                if (_ruleSeq == null || _ruleSeq.Length == 0 || _ruleIndex < 0 || _ruleIndex >= _ruleSeq.Length)
                {
                    _phase = Phase.Done;
                    await LoadSceneAsync(3, new[] { fadeImage1, fadeImage2, fadeImage3 });
                    return;
                }

                if (IsLoopClip(_ruleSeq[_ruleIndex]))
                {
                    if (_vp != null) _vp.loopPointReached += OnVideoEnded; // 루프면 자동 진행 없음
                    return;
                }

                _ruleIndex++;
                while (_ruleIndex < _ruleSeq.Length && _ruleSeq[_ruleIndex] == null)
                    _ruleIndex++;

                if (_ruleIndex < _ruleSeq.Length)
                {
                    await SwitchAndPlayNextAsync(_ruleSeq[_ruleIndex], false); // 법칙 → 법칙 (페이드 X)
                }
                else
                {
                    _phase = Phase.Done;
                    await LoadSceneAsync(3, new[] { fadeImage1, fadeImage2, fadeImage3 });
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[NewtonManager] OnVideoEnded-> 예외 발생: " + e);
        }
    }

    /// <summary> 다음 비디오로 전환/재생. withFade=true면 페이드 덮고 준비 후 스왑 </summary>
    private async UniTask SwitchAndPlayNextAsync(VideoSetting next, bool withFade)
    {
        if (_isSwitching) return;
        _isSwitching = true;

        CancelAndDispose(ref _skipCts);
        inputReceived = false;

        StopLedEffects();
        ArduinoInputManager.Instance?.SetLedAll(false);
        LedStrip.Range(0, 9, 255, 0, 0);

        bool holdLastFrame = !withFade;
        if (withFade) await FadeImageAsync(0f, 1f, fadeTime, new[] { fadeImage1 });

        // 현재 프레임 고정 (깜빡임 방지)
        if (_vp)
        {
            try
            {
                _vp.loopPointReached -= OnVideoEnded;
                _vp.Pause();
                _vp.playbackSpeed = 0f;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[NewtonManager] SwitchAndPlayNextAsync-> 일시정지 처리 중 예외: " + e.Message);
            }
        }

        // RT 준비 및 비디오 준비/재생
        Vector2Int desired = new Vector2Int(Mathf.RoundToInt(next.size.x), Mathf.RoundToInt(next.size.y));
        RenderTexture keepShowing = _raw != null ? _raw.texture as RenderTexture : null;
        RenderTexture rtForNext = VideoManager.Instance.EnsureRenderTexture(_vp, _raw, desired, reuseIfSame: holdLastFrame);

        string url = VideoManager.Instance.ResolvePlayableUrl(next.fileName);
        bool isLoop = IsLoopClip(next);
        double timeout = next.fileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ? 20.0 : 10.0;

        bool ok = await VideoManager.Instance.PrepareAndPlayAsync(_vp, url, _audio, next.volume, DestroyToken, timeout);
        if (!ok)
        {
            Debug.LogError("[NewtonManager] SwitchAndPlayNextAsync-> 비디오 준비 실패: " + url);
            _isSwitching = false;
            if (withFade) await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1 });
            return;
        }

        BindInactivityPolicyToVideo(_vp, isLoop, DestroyToken);

        // 첫 프레임 렌더 보장
        if (withFade)
        {
            await WaitFirstFrameAsync(_vp, _raw, DestroyToken, 2.0);
            await UniTask.Delay(TimeSpan.FromMilliseconds(50), cancellationToken: DestroyToken);
        }
        else
        {
            int guard = 0;
            while (guard++ < 5 && _vp != null && _vp.texture == null && _vp.frame <= 0)
                await UniTask.Yield();
        }

        // 화면 스왑 및 이전 RT 해제
        if (_raw != null && rtForNext != null && keepShowing != rtForNext)
        {
            if (_lastRT != null && _lastRT != rtForNext && _lastRT != keepShowing)
            {
                try { if (_lastRT.IsCreated()) _lastRT.Release(); Destroy(_lastRT); }
                catch (Exception e) { Debug.LogWarning("[NewtonManager] SwitchAndPlayNextAsync-> RT 해제 중 예외: " + e.Message); }
            }

            _raw.texture = rtForNext;
            _lastRT = rtForNext;
        }

        if (_vp != null)
        {
            _vp.isLooping = isLoop;
            _vp.playbackSpeed = 1f;
            _vp.loopPointReached += OnVideoEnded;
        }

        // 루프 구간이면 LED 안내 및 스킵 대기 시작
        if (isLoop)
        {
            ArduinoInputManager.Instance?.SetLedAll(true);
            StartBlinkGreenAsync(500, 160);

            _skipCts = new CancellationTokenSource();
            int capturedIndex = _ruleIndex;
            WaitSkipThenProceedAsync(_skipCts.Token, capturedIndex).Forget();
        }

        if (withFade) await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1 });
        _isSwitching = false;
    }

    /// <summary> 루프 영상에서 입력을 받으면 다음 영상으로 진행 </summary>
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

            if (arduinoPressed || TryConsumeSingleInput()) break;
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

        CancelAndDispose(ref _skipCts);

        _ruleIndex++;
        if (_ruleIndex < _ruleSeq.Length)
        {
            await SwitchAndPlayNextAsync(_ruleSeq[_ruleIndex], false);
        }
        else
        {
            _phase = Phase.Done;
            await LoadSceneAsync(3, new[] { fadeImage1, fadeImage2, fadeImage3 });
        }
    }

    #endregion

    #region Scene Transition Hooks

    /// <summary> 씬 전환 직전 정리 (루프 입력 대기/비디오/LED) </summary>
    protected override void OnBeforeSceneUnload()
    {
        CancelAndDispose(ref _skipCts);

        if (_vp)
        {
            _vp.loopPointReached -= OnVideoEnded;
            _vp.Stop();
        }

        try { ArduinoInputManager.Instance?.SetLedAll(false); }
        catch (Exception e) { Debug.LogWarning("[NewtonManager] OnBeforeSceneUnload-> LED 정리 중 예외: " + e.Message); }
    }

    /// <summary> 디버그 스킵: 모든 대기/재생을 종료하고 다음 씬으로 이동 </summary>
    protected override void OnDebugSkip()
    {
        try
        {
            CancelAndDispose(ref _skipCts);

            if (_vp)
            {
                _vp.loopPointReached -= OnVideoEnded;
                if (_vp.isPlaying)
                {
                    _vp.Stop();
                }
            }

            StopLedEffects();
            ArduinoInputManager.Instance?.SetLedAll(false);
            LedStrip.Range(0, 9, 255, 0, 0);

            _isSwitching = false;
            _phase = Phase.Done;

            LoadSceneAsync(3, new[] { fadeImage1, fadeImage2, fadeImage3 }).Forget();
        }
        catch (Exception e)
        {
            Debug.LogError("[NewtonManager] OnDebugSkip-> 예외 발생: " + e);
        }
    }

    #endregion

    #region Helpers

    /// <summary> VideoSetting이 루프형인지 판단 (name 또는 파일명(확장자 제외)이 "...Loop"로 끝나면 true) </summary>
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
}
