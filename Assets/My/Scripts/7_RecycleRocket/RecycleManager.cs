using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

[Serializable]
public class RecycleSetting
{
    public float popupFadeTime;
    public float gameCloseTime;

    public ImageSetting recyclePopup;
    public VideoSetting recycleVideo;

    public ImageSetting successImage;
    public ImageSetting messageImage;

    public ImageSetting subImage;
}

/// <summary>
/// 발사체 회수 팝업을 띄운 뒤 체험을 종료 -> 타이틀로 복귀
/// 흐름: 초기세팅 -> 입력 대기 -> LED/가이드 종료 -> 팝업->엔딩 크로스페이드 -> 지정시간 후 씬 전환
/// </summary>
public class RecycleManager : SceneManager_Base<RecycleSetting>
{
    public static RecycleManager Instance;
    
    [Header("UI")]
    [SerializeField] private GameObject recyclePopup;
    [SerializeField] private GameObject recycleVideo;
    [SerializeField] private GameObject successImage;
    [SerializeField] private GameObject messageImage;
    [SerializeField] private GameObject subImage;

    protected override string JsonPath => "JSON/RecycleSetting.json";

    private float _popupFadeTime;
    private float _gameCloseTime;
    
    private AudioSource _audio;
    private RawImage _raw;
    private VideoPlayer _video;

    #region Unity lifecycle
    
    protected override void Awake()
    {   
        base.Awake();
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    protected override void OnDisable()
    {
        base.OnDisable();

        SoundManager.Instance?.StopBGM();
        bool isQuitting = GameManager.Instance != null && GameManager.Instance.IsQuitting;
        if (!isQuitting && _video != null)
        {
            _video.Stop();
        }
    }

    /// <summary> 초기 세팅 및 입력 대기 -> 크로스페이드 -> 종료 타이머 -> 타이틀 복귀 </summary>
    protected override async UniTask Init()
    {
        CancellationToken token = DestroyToken;
        
        _video = recycleVideo.GetComponent<VideoPlayer>();
        _audio = recycleVideo.GetComponent<AudioSource>();
        _raw = recycleVideo.GetComponent<RawImage>();

        if (_video) _video.isLooping = true;

        // 시간 파라미터 보정
        _popupFadeTime = Mathf.Max(0f, setting.popupFadeTime);
        _gameCloseTime = Mathf.Max(0f, setting.gameCloseTime);

        // 이미지 & 비디오 세팅
        SettingImageObject(recyclePopup, setting.recyclePopup);
        SettingImageObject(successImage, setting.successImage);
        SettingImageObject(messageImage, setting.messageImage);
        SettingImageObject(subImage, setting.subImage);
        await SettingVideoObject(recycleVideo, setting.recycleVideo, _video, _raw, _audio);

        // 엔딩 백그라운드는 시작 시 비활성
        if (successImage != null) successImage.gameObject.SetActive(false);
        if (messageImage != null) messageImage.gameObject.SetActive(false);
        
        // LED 안내 시작
        ArduinoInputManager.Instance?.SetLedAll(true);
        StartBlinkGreenAsync(500, 160);

        SoundManager.Instance?.CrossFadeBGMByKey("End");
        TurnCamera3Async(token).Forget();
        
        // 첫 페이드 인
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage2, fadeImage3 });
        
        float elapsed = 0f;
        const float autoSkipTime = 15.0f; // 15초 대기
        
        // 입력 대기 루프 (아두이노 버튼 or 키보드)
        while (!token.IsCancellationRequested)
        {
            bool pressed = (ArduinoInputManager.Instance != null && ArduinoInputManager.Instance.TryConsumeAnyPress(out _))|| TryConsumeSingleInput();
            elapsed += Time.deltaTime;
            bool timeOut = elapsed >= autoSkipTime;

            if (pressed || timeOut)
            {
                StopLedEffects();
                ArduinoInputManager.Instance?.SetLedAll(false);
                LedStrip.Range(0, 9, 255, 0, 0);
                break;
            }

            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        ArduinoInputManager.Instance?.FlushAll();

        // 팝업(앞) -> 엔딩(뒤) 크로스페이드
        try
        {   
            recycleVideo.SetActive(false);
            
            await CrossFadeAsync(recyclePopup, successImage, _popupFadeTime);
            messageImage.SetActive(true);
        }
        catch (Exception e)
        {
            LogUtil.LogWarn(nameof(RecycleManager), nameof(Init), "CrossFadeAsync failed: " + e.Message);
            // 실패해도 종료 타이머는 계속 진행
        }

        // 종료 타이머 후 타이틀로 이동
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(_gameCloseTime), cancellationToken: token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // 무입력 복귀 정책과 충돌 방지
        PauseInactivityTimer();

        // 0번 빌드 인덱스(타이틀)로 전환
        await LoadSceneAsync(0, new[] { fadeImage1, fadeImage3 });
    }

    #endregion
    
    public void ForceStopVideo()
    {
        if (_video != null)
        {
            if (_video.isPlaying) _video.Stop();
            _video.enabled = false;
        }
    }
}
