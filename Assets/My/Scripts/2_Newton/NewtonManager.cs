using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

[Serializable]
public class NewtonSetting
{
    public VideoSetting introVideo;
    public VideoSetting newtonsRule1Video;
    public VideoSetting newtonsRule2Video;
    public VideoSetting newtonsRule3Video;

    public TextSetting titleText;
    public TextSetting infoText;
}

public class NewtonManager : SceneManager_Base<NewtonSetting>
{
    [Header("UI")]
    [SerializeField] private GameObject videoPlayerObject;
    [SerializeField] private GameObject titleTextObj;
    [SerializeField] private GameObject infoTextObj;
    
    protected override string JsonPath => "JSON/NewtonSetting.json";

    private VideoPlayer _vp;
    private RawImage _raw;
    private AudioSource _audio;

    private enum Phase { Intro, RuleSeq, Done }
    private Phase _phase;

    private VideoSetting[] _ruleSeq;
    private int _ruleIndex;

    private bool _infoShown;        // 50% 시 안내 노출 여부
    private bool _awaitingSkip;     // 스킵 입력 대기 중인지

    private CancellationTokenSource _progressCts;
    private CancellationTokenSource _skipCts;
    
    protected override void OnDisable()
    {
        // 이벤트 정리 및 비디오 정지
        try { _progressCts?.Cancel(); } catch { }
        try { _skipCts?.Cancel(); } catch { }
        _progressCts?.Dispose(); _progressCts = null;
        _skipCts?.Dispose();     _skipCts = null;

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
        _audio = UIUtility.GetOrAdd<AudioSource>(videoPlayerObject); // 비디오 오브젝트에 오디오 소스를 보장함

        // 타이틀/안내 텍스트 설정
        await SettingTextObject(titleTextObj, setting.titleText);
        await SettingTextObject(infoTextObj, setting.infoText);
        if (infoTextObj) infoTextObj.SetActive(false);

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
            CancelAndDispose(ref _progressCts);
            CancelAndDispose(ref _skipCts);
            
            _vp.loopPointReached -= OnVideoEnded;

            if (_phase == Phase.Intro) // 인트로 비디오가 끝남
            {
                _phase = Phase.RuleSeq;
                _ruleIndex = 0;
                await SwitchAndPlayNextAsync(_ruleSeq[_ruleIndex]);
            }
            else if (_phase == Phase.RuleSeq) // 뉴턴의 법칙 비디오가 끝남
            {
                _ruleIndex++;
                if (_ruleIndex < _ruleSeq.Length)
                {
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
            Debug.LogError("[NewtonManager] Video player ended: " + e.Message);
        }
      
    }

    /// <summary> 다음 비디오로 페이드 아웃/인 하며 전환하고 50% 모니터링 시작 </summary>
    private async Task SwitchAndPlayNextAsync(VideoSetting next)
    {   
        CancelAndDispose(ref _progressCts);
        CancelAndDispose(ref _skipCts);

        // 비디오 재생 프로퍼티 초기화
        if (infoTextObj) infoTextObj.SetActive(false);  // 스킵 메시지 비활성화
        _infoShown = false; 
        _awaitingSkip = false;  // 스킵 비활성화
        inputReceived = false;  // 입력을 받지 않음
        
        // 페이드 아웃
        await FadeImageAsync(0f, 1f, fadeTime, new[] { fadeImage1 });

        if (_vp) _vp.Stop();
        await SettingVideoObject(videoPlayerObject, next, _vp, _raw, _audio); // 다음 비디오 준비

        _vp.loopPointReached -= OnVideoEnded;
        _vp.loopPointReached += OnVideoEnded;

        // 새 토큰 발급 & 현재 ruleIndex 캡처
        _progressCts = new CancellationTokenSource();
        int capturedIndex = _ruleIndex;
        _ = MonitorProgressAndEnableSkipAsync(_progressCts.Token, capturedIndex);
        
        // 페이드 인
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1 });
    }
    
    /// <summary> 영상 길이 확보 대기 및 50% 도달 시 안내 노출 및 스킵 입력 대기 시작 </summary>
    private async Task MonitorProgressAndEnableSkipAsync(CancellationToken token, int ruleIndexAtStart)
    {
        // 비디오가 준비될 때까지 대기 (isPrepared == true)
        while (!token.IsCancellationRequested && _vp && !_vp.isPrepared)
            await Task.Yield();

        // 재생 중 50% 모니터링
        while (!token.IsCancellationRequested && _vp && _vp.isPlaying)
        {
            // 인덱스가 바뀌면(비디오 전환됨) 즉시 종료
            if (ruleIndexAtStart != _ruleIndex) break;

            if (!_infoShown && _vp.length > 0.0)
            {   
                // 비디오 재생 퍼센트 계산
                double ratio = _vp.time / _vp.length;
                if (ratio >= 0.5)
                {
                    if (infoTextObj) infoTextObj.SetActive(true);
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

    /// <summary> 사용자 입력을 단발로 소비하고, 현재 영상을 스킵하여 다음으로 진행 </summary>
    private async Task WaitSkipThenProceedAsync(CancellationToken token, int ruleIndexAtStart)
    {
        while (!token.IsCancellationRequested && !TryConsumeSingleInput())
            await Task.Yield();

        if (token.IsCancellationRequested) return;

        // 전환되었다면 무시
        if (ruleIndexAtStart != _ruleIndex) return;

        // 스킵 처리
        if (_vp)
        {
            _vp.loopPointReached -= OnVideoEnded;
            _vp.Stop();
        }
        if (infoTextObj) infoTextObj.SetActive(false);

        if (_phase != Phase.RuleSeq) return;

        // 기존 모니터/스킵 태스크 중단
        CancelAndDispose(ref _progressCts);
        CancelAndDispose(ref _skipCts);

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

    private Task GoNextSceneAsync()
    {
        int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 3;
        return LoadSceneAsync(target, new[] { fadeImage1, fadeImage3 });
    }

    private void CancelAndDispose(ref CancellationTokenSource cts)
    {
        if (cts == null) return;
        try { cts.Cancel(); } catch { }
        cts.Dispose();
        cts = null;
    }
}