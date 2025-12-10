using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

[Serializable]
public class TitleSetting
{
    public ImageSetting background;
    public ImageSetting infoImage;
    public ImageSetting titleImage;

    public ImageSetting subImage;
    public VideoSetting idleVideo;
}

/// <summary> 타이틀 씬 관리 클래스 </summary>
public sealed class TitleManager : SceneManager_Base<TitleSetting>
{
    public static TitleManager Instance;
    // ===== JSON 경로 =====
    protected override string JsonPath => "JSON/TitleSetting.json";

    #region Serialized Refs

    [Header("UI")]
    [SerializeField] private GameObject backgroundImage; // 배경 이미지
    [SerializeField] private GameObject infoImage;       // "시작하려면 아무 버튼이나 누르세요"
    [SerializeField] private GameObject titleImage;      // 우주발사체 타이틀 이미지
    [SerializeField] private GameObject subImage;        // "누리호 발사 임무" 이미지
    
    [Header("Video")]
    [SerializeField] private GameObject verticalVideoObject;

    #endregion

    #region Initialization
    
    private VideoPlayer _vp;
    private RawImage _raw;
    private AudioSource _audio;

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

    /// <summary> 씬 초기화: UI 세팅 → 아두이노 준비 대기 → LED/카메라 시작 → 페이드 인 → 입력 대기 → 다음 씬 </summary>
    protected override async UniTask Init()
    {   
        SoundManager.Instance.PauseBGM();
        inputReceived = false;

        // 1) UI 세팅
        SettingImageObject(backgroundImage, setting.background); 
        SettingImageObject(titleImage, setting.titleImage);
        SettingImageObject(infoImage, setting.infoImage);
        SettingImageObject(subImage, setting.subImage);

        if (verticalVideoObject != null && setting.idleVideo != null)
        {
            _vp = verticalVideoObject.GetComponent<VideoPlayer>();
            _raw = verticalVideoObject.GetComponent<RawImage>();
            _audio = verticalVideoObject.GetComponent<AudioSource>();
            
            await SettingVideoObject(verticalVideoObject, setting.idleVideo, _vp, _raw, _audio);
            if (_vp != null)
            {
                _vp.isLooping = true;
            }
        }
        
        await UniTask.Yield();

        // 2) 아두이노 준비 대기
        CancellationToken ct = DestroyToken;
        bool ready = false;
        try
        {
            ready = await WaitArduinoReadyAsync(3000, ct);
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning("[TitleManager] Init-> 아두이노 준비 대기 취소");
            return;
        }
        catch (Exception e)
        {
            Debug.LogError($"[TitleManager] Init-> 아두이노 준비 대기 중 예외: {e}");
        }

        if (!ready)
        {
            Debug.LogWarning("[TitleManager] Init-> 아두이노 준비 타임아웃");
        }
        else
        {
            try
            {
                await UniTask.Delay(1000, cancellationToken: ct); // 1초 지연
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("[TitleManager] Init-> 초기 지연 취소");
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TitleManager] Init-> 초기 지연 중 예외: {e.Message}");
            }

            try { ArduinoInputManager.Instance?.SetLedAll(true); } 
            catch (Exception e) { Debug.LogWarning($"[TitleManager] Init-> LED 전체 켜기 중 예외: {e.Message}"); }
            StartBlinkGreenAsync(500, 160);
        }

        // 3) 페이드 인
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage2, fadeImage3 });
        if (ct.IsCancellationRequested || !this || !isActiveAndEnabled) return;

        // 4) 입력 대기 → 다음 씬
        while (!ct.IsCancellationRequested && isActiveAndEnabled)
        {
            ArduinoInputManager aim = ArduinoInputManager.Instance;
            if (aim != null && aim.TryConsumeAnyPress(out _)) break;
            if (TryConsumeSingleInput()) break;
            await UniTask.Yield();
        }

        SoundManager.Instance?.PlayByKey("Title_Confirm");
        int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 1;
        await LoadSceneAsync(target, new[] { fadeImage1, fadeImage2, fadeImage3 });
    }

    #endregion
    
    protected override void OnDisable()
    {
        base.OnDisable();
        bool isQuitting = GameManager.Instance != null && GameManager.Instance.IsQuitting;
        if (!isQuitting && _vp != null)
        {
            _vp.Stop();
        }
    }

    #region Helpers

    /// <summary> 아두이노가 준비(포트 오픈)될 때까지 대기 </summary>
    private async UniTask<bool> WaitArduinoReadyAsync(int timeoutMs, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                ArduinoInputManager inst = ArduinoInputManager.Instance;
                if (inst != null && inst.ArduinoReady) return true;
                await UniTask.Delay(50, cancellationToken: ct);
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("[TitleManager] WaitArduinoReadyAsync-> 대기 취소");
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TitleManager] WaitArduinoReadyAsync-> 폴링 중 예외: {e.Message}");
            }
        }
        return false;
    }

    #endregion
    
    public void ForceStopVideo()
    {
        if (_vp != null)
        {
            if (_vp.isPlaying) _vp.Stop();
            _vp.enabled = false;
        }
    }
}
