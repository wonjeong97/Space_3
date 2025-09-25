using System;
using System.Threading;
using System.Threading.Tasks;
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
    public VideoSetting newtonsRule1Video;
    public VideoSetting newtonsRule2Video;
    public VideoSetting newtonsRule3Video;
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

    private enum Phase
    {
        Intro,
        RuleSeq,
        Done
    }

    private Phase _phase;

    private VideoSetting[] _ruleSeq; // 뉴턴의 법칙 비디오를 저장하는 배열
    private int _ruleIndex;

    private bool _infoShown; // 50% 시 안내 노출 여부
    private bool _awaitingSkip; // 스킵 입력 대기 중인지
    
    //현재 재생 중인 비디오의 진행률을 모니털링하는 태스크를 제어하기 위한 토큰
    private CancellationTokenSource _progressCts;
    
    // 스킵 안내 메시지가 나온 후 사용자 입력을 감시하는 태스크를 제어하기 위한 토큰
    private CancellationTokenSource _skipCts;

    protected override void OnDisable()
    {
        // 이벤트 정리 및 비디오 정지
        try
        {
            CancelAndDispose(ref _progressCts);
            CancelAndDispose(ref _skipCts);
        }
        catch (Exception e)
        {
            Debug.LogError($"[NewtonManager] OnDisable Exception: {e.Message}\n{e}");
        }

        if (_vp)
        {
            _vp.loopPointReached -= OnVideoEnded;
            _vp.Stop();
        }
    }

    protected override async Task Init()
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
        _ruleSeq = new[] { setting.newtonsRule1Video, setting.newtonsRule2Video, setting.newtonsRule3Video };
        _ruleIndex = 0;
        _phase = Phase.Intro;

        // 인트로 세팅 및 재생  
        await SettingVideoObject(videoPlayerObject, setting.introVideo, _vp, _raw, _audio);
        _vp.loopPointReached -= OnVideoEnded;
        _vp.loopPointReached += OnVideoEnded;

        StartCoroutine(TurnCamera3());
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage3 });
    }

    private async void OnVideoEnded(VideoPlayer vp)
    {
        try
        {   
            // 비디오 재생률 및 스킵 관련 토큰 해제
            CancelAndDispose(ref _progressCts);
            CancelAndDispose(ref _skipCts);

            // 이벤트 해제
            _vp.loopPointReached -= OnVideoEnded;

            if (_phase == Phase.Intro) // 인트로 비디오가 끝남
            {   
                // 뉴턴의 법칙 비디오 준비 및 재생
                _phase = Phase.RuleSeq;
                _ruleIndex = 0;
                await SwitchAndPlayNextAsync(_ruleSeq[_ruleIndex]);
            }
            else if (_phase == Phase.RuleSeq) // 뉴턴의 법칙 비디오가 끝남
            {
                _ruleIndex++;
                if (_ruleIndex < _ruleSeq.Length)
                {
                    // 다음 뉴턴의 법칙 비디오 재생
                    await SwitchAndPlayNextAsync(_ruleSeq[_ruleIndex]);
                }
                else // 마지막 비디오 재생 후 다음 씬 전환
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

    /// <summary> 다음 비디오로 전환 및 비디오 재생 50% 모니터링 </summary>
    private async Task SwitchAndPlayNextAsync(VideoSetting next)
    {
        CancelAndDispose(ref _progressCts);
        CancelAndDispose(ref _skipCts);

        // 비디오 재생 관렵 변수 초기화
        if (infoImage2) infoImage2.SetActive(false); // 스킵 메시지 비활성화
        _infoShown = false;
        _awaitingSkip = false; // 스킵 비활성화
        inputReceived = false; // 입력을 받지 않음
        
        await FadeImageAsync(0f, 1f, fadeTime, new[] { fadeImage1 });

        if (_vp) _vp.Stop();
        await SettingVideoObject(videoPlayerObject, next, _vp, _raw, _audio); // 다음 비디오 세팅

        _vp.loopPointReached -= OnVideoEnded;
        _vp.loopPointReached += OnVideoEnded;

        // 새 토큰 발급 & 현재 ruleIndex 캡처
        _progressCts = new CancellationTokenSource();
        int capturedIndex = _ruleIndex;
        _ = MonitorProgressAndEnableSkipAsync(_progressCts.Token, capturedIndex);
        
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1 });
    }

    /// <summary> 영상 대기, 재생률 50% 도달 시 안내 메시지 표시 및 스킵 입력 대기 시작 </summary>
    private async Task MonitorProgressAndEnableSkipAsync(CancellationToken token, int ruleIndexAtStart)
    {
        // 비디오가 준비될 때까지 대기 (isPrepared == true)
        while (!token.IsCancellationRequested && _vp && !_vp.isPrepared)
            await Task.Yield();

        // 재생 중 50% 모니터링
        while (!token.IsCancellationRequested && _vp && _vp.isPlaying)
        {
            // 인덱스가 바뀌면(비디오 전환) 즉시 종료
            if (ruleIndexAtStart != _ruleIndex) break;

            if (!_infoShown && _vp.length > 0.0)
            {
                // 비디오 재생 퍼센트 계산
                double ratio = _vp.time / _vp.length;
                if (ratio >= 0.5)
                {
                    if (infoImage2) infoImage2.SetActive(true);
                    _infoShown = true;
                    inputReceived = false; // 입력 받았음을 한번 더 초기화

                    if (!_awaitingSkip)
                    {
                        _awaitingSkip = true;
                        _skipCts = new CancellationTokenSource();
                        _ = WaitSkipThenProceedAsync(_skipCts.Token, ruleIndexAtStart);
                    }
                }
            }

            await Task.Yield();
        }
    }

    /// <summary> 사용자 입력을 받고, 현재 영상을 스킵하여 다음으로 진행 </summary>
    private async Task WaitSkipThenProceedAsync(CancellationToken token, int ruleIndexAtStart)
    {
        // 스킵 허용 시점 기록
        long skipEnableMs = ArduinoInputManager.NowMs;

        // 직전까지 들어온 잔여 이벤트 플러시
        if (ArduinoInputManager.Instance) ArduinoInputManager.Instance.FlushAll();
        
        // 잔상 방지용으로 한 프레임 양보
        await Task.Yield();

        // 입력 대기 루프: 아두이노(허용 시점 이후) 또는 키/마우스/터치 중 먼저 들어온 1건 소비
        while (true)
        {
            if (token.IsCancellationRequested) return;

            // 아두이노: 허용 시각 이후 이벤트만 소비
            if (ArduinoInputManager.Instance != null && ArduinoInputManager.Instance.TryConsumePressNewerThan(skipEnableMs, out _)) break;

            // 키/마우스/터치
            if (TryConsumeSingleInput()) break;
            
            await Task.Yield();
        }

        if (token.IsCancellationRequested) return;
        if (ruleIndexAtStart != _ruleIndex) return; // 전환되었다면 무시

        // 스킵 처리
        if (_vp)
        {
            _vp.loopPointReached -= OnVideoEnded;
            _vp.Stop();
        }
        if (infoImage2)infoImage2.SetActive(false);
        if (_phase != Phase.RuleSeq) return;

        // 모니터/스킵 태스크 중단 및 정리
        CancelAndDispose(ref _progressCts);
        CancelAndDispose(ref _skipCts);

        // 다음 규칙 영상 또는 다음 씬
        _ruleIndex++;
        if (_ruleIndex < _ruleSeq.Length)
        {
            await SwitchAndPlayNextAsync(_ruleSeq[_ruleIndex]);
        }
        else
        {
            _phase = Phase.Done;
            await GoNextSceneAsync();
        }
    }
    
    /// <summary> 다음 씬으로 전환 </summary>
    private Task GoNextSceneAsync()
    {
        int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 3;
        return LoadSceneAsync(target, new[] { fadeImage1, fadeImage3 });
    }
    
    /// <summary> 실행 중인 태스크를 취소하고 메모리/리소스 해제 </summary>
    private void CancelAndDispose(ref CancellationTokenSource cts)
    {
        if (cts == null) return;
        try
        {
            cts.Cancel();
        }
        catch(Exception e)
        {
            Debug.LogError($"[NewtonManager] CancelAndDispose failed with {cts.Token} exception: {e}");
        }

        cts.Dispose();
        cts = null;
    }
    
    // 씬 전환 직전 클래스의 비동기/이벤트 정리
    protected override void OnBeforeSceneUnload()
    {
        // 진행률/스킵 토큰 정리
        try { _progressCts?.Cancel(); } catch { }
        try { _skipCts?.Cancel(); } catch { }
        _progressCts?.Dispose(); _progressCts = null;
        _skipCts?.Dispose();     _skipCts = null;

        // VideoPlayer 이벤트 해제 및 정지
        if (_vp)
        {
            _vp.loopPointReached -= OnVideoEnded;
            _vp.Stop();
        }
        
        if (infoImage2) infoImage2.SetActive(false);
    }
}