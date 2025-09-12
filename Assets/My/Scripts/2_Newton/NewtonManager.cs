using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
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
    [SerializeField] private GameObject titleText;
    [SerializeField] private GameObject infoText;
    protected override string JsonPath => "JSON/NewtonSetting.json";

    private VideoPlayer _vp;
    private RawImage _raw;
    private AudioSource _audioSource;
    
    private enum Phase { Intro, RuleSeq, Done }
    private Phase _phase;
    private VideoSetting[] _ruleSeq;
    private int _ruleIndex;

    // 비디오가 50% 재생되었을 때 인포 텍스트를 표시 및 입력을 받도록 함
    private Coroutine _progressCo;
    private Coroutine _awaitInputCo;
    private bool _infoShown;
    private bool _waitingForInput;
    
    private void OnDisable()
    {
        StopAllCoroutines();
        _progressCo = null;
        _infoShown = false;

        if (infoText) infoText.SetActive(false);

        if (_vp)
        {
            _vp.loopPointReached -= OnVideoEnded;
            _vp.Stop();
        }
    }

    protected override async Task Init()
    {
        _vp = videoPlayerObject.GetComponent<VideoPlayer>();
        _raw = videoPlayerObject.GetComponent<RawImage>();
        _audioSource = UIUtility.GetOrAdd<AudioSource>(videoPlayerObject);
    
        // 타이틀 "뉴턴의 운동 법칙) 작용과 반작용" 텍스트 설정
        await SettingTextObject(titleText, setting.titleText);
        
        // 인포 "컨트롤러의 아무 버튼을 누르면 다음 화면으로 진행됩니다." 텍스트 설정
        await SettingTextObject(infoText, setting.infoText);
        infoText.SetActive(false);
        
        // 비디오 저장
        _ruleSeq = new[]
        {
            setting.newtonsRule1Video,
            setting.newtonsRule2Video,
            setting.newtonsRule3Video
        };
        _ruleIndex = 0;
        _phase = Phase.Intro;
        
        await SettingVideoObject(videoPlayerObject, setting.introVideo, _vp, _raw, _audioSource);
        _vp.loopPointReached -= OnVideoEnded;
        _vp.loopPointReached += OnVideoEnded;

        StartCoroutine(TurnCamera3());
        StartCoroutine(FadeImage(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage3 }));
    }
    
    private void OnVideoEnded(VideoPlayer vp)
    {   
        vp.loopPointReached -= OnVideoEnded;
        vp.Stop();
        
        if (_progressCo != null)
        {
            StopCoroutine(_progressCo);
            _progressCo = null;
        }
        
        // 인트로 비디오가 끝났을 경우
        if (_phase == Phase.Intro)
        {
            _phase = Phase.RuleSeq; // 페이즈 변경
            _ruleIndex = 0; // 인덱스 0번부터 시작
            StartCoroutine(SwitchAndPlayNext(_ruleSeq[_ruleIndex]));
        }
        else if (_phase == Phase.RuleSeq)
        {
            _ruleIndex++;
            if (_ruleIndex < _ruleSeq.Length)
            {
                StartCoroutine(SwitchAndPlayNext(_ruleSeq[_ruleIndex]));
            }
            else // 마지막 비디오가 끝난 후
            {
                _phase = Phase.Done;
                StartCoroutine(FinishFlow());
            }
        }
    }
    
    /// <summary> 다음 비디오로 변환함 </summary>
    private IEnumerator SwitchAndPlayNext(VideoSetting next)
    {   
        if (infoText) infoText.SetActive(false);
        _infoShown = false;
        _waitingForInput = false;
        
        // 페이드 아웃(검게 덮기)
        yield return FadeImage(0f, 1f, fadeTime, new[] { fadeImage1 });

        // 현재 비디오 정지
        if (_vp) _vp.Stop();

        // 다음 비디오 세팅
        Task t = SettingVideoObject(videoPlayerObject, next, _vp, _raw, _audioSource);
        while (!t.IsCompleted) yield return null;
        if (t.IsFaulted)
        {
            Debug.LogError(t.Exception);
            yield break;
        }

        // 끝 이벤트 재구독
        _vp.loopPointReached -= OnVideoEnded;
        _vp.loopPointReached += OnVideoEnded;
        
        if (_phase == Phase.RuleSeq)
        {
            if (_progressCo != null) StopCoroutine(_progressCo);
            _progressCo = StartCoroutine(_MonitorRuleProgress());
        }

        // 페이드 인(화면 열기)
        yield return FadeImage(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage3 });
    }
    
    private IEnumerator FinishFlow()
    {
        // 마지막 페이드 아웃
        yield return FadeImage(0f, 1f, fadeTime, new[] { fadeImage1, fadeImage3 });
    }
    
    private IEnumerator _MonitorRuleProgress()
    {
        // 영상 길이 정보가 잡힐 때까지 잠깐 대기
        float guard = 0f;
        while (_vp && _vp.isPrepared && _vp.length <= 0.0 && guard < 1.0f)
        {
            guard += Time.unscaledDeltaTime;
            yield return null;
        }

        // 재생 중 진행률 체크
        while (_vp && _vp.isPlaying)
        {
            if (!_infoShown && _vp.length > 0.0)
            {
                double ratio = _vp.time / _vp.length;
                if (ratio >= 0.5)
                {
                    if (infoText) infoText.SetActive(true);
                    _infoShown = true;
                    
                    if (!_waitingForInput)
                    {
                        _waitingForInput = true;
                        if (_awaitInputCo != null) StopCoroutine(_awaitInputCo);
                        _awaitInputCo = StartCoroutine(_WaitForAnyInputThenProceed());
                    }
                }
            }
            yield return null;
        }

        _progressCo = null;
    }
    
    private IEnumerator _WaitForAnyInputThenProceed()
    {
        while (!IsAnyUserInputDown())
            yield return null;

        _waitingForInput = false;

        // 이벤트/코루틴 정리
        if (_progressCo != null) { StopCoroutine(_progressCo); _progressCo = null; }
        if (_vp)
        {
            _vp.loopPointReached -= OnVideoEnded; // 끝 이벤트 중복 방지
            _vp.Stop();                           // 현재 룰 영상 스킵
        }

        if (infoText) infoText.SetActive(false);

        // 다음으로 진행 or Finish
        ProceedToNextFromInput();

        _awaitInputCo = null;
    }

    private bool IsAnyUserInputDown()
    {
        if (Input.anyKeyDown) return true;
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) return true;
        if (Input.touchCount > 0) return true;
        return false;
    }
    
    private void ProceedToNextFromInput()
    {
        if (_phase != Phase.RuleSeq) return;

        _ruleIndex++;
        if (_ruleIndex < _ruleSeq.Length)
        {
            StartCoroutine(SwitchAndPlayNext(_ruleSeq[_ruleIndex]));
        }
        else
        {
            _phase = Phase.Done;
            StartCoroutine(FinishFlow());
        }
    }
}