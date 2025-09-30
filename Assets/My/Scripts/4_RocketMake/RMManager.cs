using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

[Serializable]
public class RocketSetting
{
    public ImageSetting rocketImage;
    public float velocity;
    public float maxVelocity;
    public float altitude;
    public float slope;
    public float remainDistance;
}

[Serializable]
public class RMSetting
{
    public float videoFadeTime;

    public ImageSetting background;
    public ImageSetting main1;
    public ImageSetting main2;
    public ImageSetting main3;
    public ImageSetting sub1;

    public RocketSetting[] rockets;
    public RocketSetting[] satellites;
    public ImageSetting[] progressBars;

    public VideoSetting locationVideo;
    public VideoSetting rocketMakeVideo;
}

/// <summary>
/// 우주발사체를 다단(3단)으로 제작하는 이유 씬 매니저
/// 발사체, 위성 선택 -> 발사 장소 영상 -> 발사체 다단 제작 영상 → 다음 씬
/// </summary>
public class RMManager : SceneManager_Base<RMSetting>
{
    [Header("UI")]
    [SerializeField] private GameObject backgroundImage;
    [SerializeField] private GameObject mainImage1;
    [SerializeField] private GameObject mainImage2;
    [SerializeField] private GameObject mainImage3;

    [SerializeField] private GameObject rocketImage; // 발사체, 위성 선택 이미지
    [SerializeField] private TextMeshProUGUI textVelocity;
    [SerializeField] private TextMeshProUGUI textMaxVelocity;
    [SerializeField] private TextMeshProUGUI textAltitude;
    [SerializeField] private TextMeshProUGUI textSlope;
    [SerializeField] private TextMeshProUGUI textRemainDistance;
    [SerializeField] private Image imageVelocityBar;
    [SerializeField] private Image imageMaxVelocityBar;
    [SerializeField] private Image imageAltitudeBar;
    [SerializeField] private Image imageSlopeBar;
    [SerializeField] private Image imageRemainDistanceBar;

    [SerializeField] private GameObject videoPlayerObject;
    [SerializeField] private GameObject subImage;

    protected override string JsonPath => "JSON/RMSetting.json";
    
    private const float BarMax = 999f;

    private enum Phase
    {
        SelectRocket,
        SelectSatellite,
        Location,
        PlayingMake,
        Done
    }

    private Phase _phase = Phase.SelectRocket;

    private int _selectedRocket = -1;
    private int _selectedSatellite = -1;

    private VideoPlayer _vp;
    private RawImage _raw;
    private AudioSource _audio;

    private float _videoFadeTime;

    protected override void OnDisable()
    {
        if (_vp)
        {
            _vp.loopPointReached -= OnLocationEnded;
            _vp.loopPointReached -= OnMakeEnded;
            _vp.Stop();
        }
        
        _velBarCts?.Cancel();   _velBarCts?.Dispose();
        _maxBarCts?.Cancel();   _maxBarCts?.Dispose();
        _altBarCts?.Cancel();   _altBarCts?.Dispose();
        _slopeBarCts?.Cancel(); _slopeBarCts?.Dispose();
        _remainBarCts?.Cancel();_remainBarCts?.Dispose();
    }

    protected override async UniTask Init()
    {
        _vp = videoPlayerObject.GetComponent<VideoPlayer>();
        _raw = videoPlayerObject.GetComponent<RawImage>();
        _audio = videoPlayerObject.GetComponent<AudioSource>();

        _videoFadeTime = Mathf.Max(0f, setting.videoFadeTime);

        SettingImageObject(backgroundImage, setting.background);

        // 고정 이미지/서브 디스플레이 세팅
        SettingImageObject(mainImage1, setting.main1);
        SettingImageObject(mainImage2, setting.main2);
        SettingImageObject(mainImage3, setting.main3);
        SettingImageObject(subImage, setting.sub1);
        
        InitializeProgressBar();
        
        rocketImage.SetActive(false);

        // 장소 영상 세팅 
        await SettingVideoObject(videoPlayerObject, setting.locationVideo, _vp, _raw, _audio);
        _vp.Pause();
        _vp.time = 0;
        videoPlayerObject.SetActive(false);

        // 첫 진입 페이드 인
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage2, fadeImage3 });

        // 입력 루프(좌/우/확인)
        while (_phase != Phase.Done)
        {
            if (!ArduinoInputManager.Instance) return;

            if ((ArduinoInputManager.Instance.TryConsumeAnyPress(out ArduinoInputManager.ButtonId btn) &&
                 btn == ArduinoInputManager.ButtonId.Button1) || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                MoveSelection(-1);
            }
            else if (btn == ArduinoInputManager.ButtonId.Button3 || Input.GetKeyDown(KeyCode.RightArrow))
            {
                MoveSelection(+1);
            }
            else if ((btn == ArduinoInputManager.ButtonId.Button2) || Input.GetKeyDown(KeyCode.DownArrow))
            {
                if (await ConfirmAsync()) break; // Location 진입 시 true로 탈출
            }

            ArduinoInputManager.Instance.FlushAll();

            await UniTask.Yield();
        }
    }

    /// <summary> 사용자의 입력에 따라 인덱스를 바꾸고 이미지를 변경함 </summary>
    private void MoveSelection(int delta)
    {
        if (!canInput) return;

        if (_phase == Phase.SelectRocket)
        {
            int max = Mathf.Max(0, setting.rockets.Length - 1);

            int baseIndex = (_selectedRocket < 0) ? 0 : _selectedRocket;
            _selectedRocket = Mathf.Clamp(baseIndex + ((_selectedRocket < 0) ? 0 : delta), 0, max);

            SetSelected(_selectedRocket, isRocket: true);
        }
        else if (_phase == Phase.SelectSatellite)
        {
            int max = Mathf.Max(0, setting.satellites.Length - 1);
            _selectedSatellite = Mathf.Clamp((_selectedSatellite < 0 ? 0 : _selectedSatellite) + delta, 0, max);
            SetSelected(_selectedSatellite, isRocket: false);
        }
    }

    /// <summary> 로켓 정보를 선택된 로켓으로 바꿈 </summary>
    private void SetSelected(int index, bool isRocket)
    {
        if (!rocketImage) return;
        if (!rocketImage.activeInHierarchy) rocketImage.SetActive(true);

        // 숫자 표시 초기 상태값이 없었다면 NaN으로 표기해 첫 진입시 점프 없이 고정 표시되게 함
        if (_curVelocity == 0f && textVelocity && string.IsNullOrEmpty(textVelocity.text))
        {
            _curVelocity = float.NaN;
            _curMaxVelocity = float.NaN;
            _curAltitude = float.NaN;
            _curSlope = float.NaN;
            _curRemainDistance = float.NaN;
        }

        RocketSetting src;
        if (isRocket)
        {
            if (index < 0 || index >= setting.rockets.Length) return;
            src = setting.rockets[index];
        }
        else
        {
            if (index < 0 || index >= setting.satellites.Length) return;
            // BUGFIX: satellites 배열로 참조
            src = setting.satellites[index];
        }

        // 이미지 교체
        SettingImageObject(rocketImage, src.rocketImage);

        // 숫자 라벨 애니메이션 시작
        StartLabelAnimation(textVelocity,       ref _curVelocity,       src.velocity,       ref _velCts,    0, " km/s");
        StartLabelAnimation(textMaxVelocity,    ref _curMaxVelocity,    src.maxVelocity,    ref _maxVelCts, 0, " km/s");
        StartLabelAnimation(textAltitude,       ref _curAltitude,       src.altitude,       ref _altCts,    0, " km");
        StartLabelAnimation(textSlope,          ref _curSlope,          src.slope,          ref _slopeCts,  0, " °");
        StartLabelAnimation(textRemainDistance, ref _curRemainDistance, src.remainDistance, ref _remainCts, 0, " km");
        
        // 진행 바 즉시 갱신
        UpdateProgressBars(src);
    }

    /// <summary> 확인 버튼을 눌렀을 때 동작하는 메서드 </summary>
    private async UniTask<bool> ConfirmAsync()
    {
        if (!canInput) return false;

        if (_phase == Phase.SelectRocket)
        {
            // 선택이 아직 없으면 0번을 강제로 보여주고 계속 Rocket 단계 유지
            if (_selectedRocket < 0)
            {
                _selectedRocket = 0;
                SetSelected(_selectedRocket, isRocket: true);
                return false; // 위성 단계로 넘어가지 않음
            }

            // 이미 선택된 상태면 위성 단계로 전환
            _phase = Phase.SelectSatellite;

            // 위성도 초기 표시는 0번으로 (선택이 없을 때)
            _selectedSatellite = Mathf.Clamp(_selectedSatellite, 0, Mathf.Max(0, setting.satellites.Length - 1));
            SetSelected(_selectedSatellite, isRocket: false);
            return false;
        }

        if (_phase == Phase.SelectSatellite)
        {
            _phase = Phase.Location;
            ArduinoInputManager.Instance?.SetLedAll(false);
            await PlayLocationThenMakeAsync(); // 장소 -> 제작 영상 시퀀스
            return true;
        }

        return false;
    }

    /// <summary> 장소 영상 페이드 인 재생 -> 종료 시 제작 영상으로 전환 </summary>
    private async UniTask PlayLocationThenMakeAsync()
    {
        canInput = false;

        // RawImage 투명으로, 오브젝트 활성화
        if (_raw)
        {
            var c = _raw.color;
            _raw.color = new Color(c.r, c.g, c.b, 0f);
        }

        videoPlayerObject.SetActive(true);

        // 장소 영상 재생 설정 및 재생 시작
        _vp.isLooping = false;
        _vp.loopPointReached -= OnLocationEnded;
        _vp.loopPointReached += OnLocationEnded;
        _vp.Play();

        // RawImage 알파 페이드 인
        float t = 0f;
        while (t < _videoFadeTime)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / _videoFadeTime);
            var c = _raw.color;
            _raw.color = new Color(c.r, c.g, c.b, a);
            await UniTask.Yield();
        }
    }

    /// <summary> 장소 영상 종료 -> 화면 페이드 -> 제작 영상 세팅·재생 -> 화면 복원 </summary>
    private async void OnLocationEnded(VideoPlayer vp)
    {
        try
        {
            _vp.loopPointReached -= OnLocationEnded;

            // 화면 덮기
            await FadeImageAsync(0f, 1f, fadeTime, new[] { fadeImage1 });

            // 제작 영상으로 세팅/재생
            await SettingVideoObject(videoPlayerObject, setting.rocketMakeVideo, _vp, _raw, _audio);
            _vp.isLooping = false;
            _vp.loopPointReached -= OnMakeEnded;
            _vp.loopPointReached += OnMakeEnded;
            _vp.Play();

            // 화면 복원
            await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1 });
            _phase = Phase.PlayingMake;
        }
        catch (Exception e)
        {
            Debug.LogError($"[RMManager] Failed to set rocketMakeVideo: {e}");
            canInput = true;
        }
    }

    /// <summary> 제작 이유 영상 종료 -> 다음 씬 </summary>
    private void OnMakeEnded(VideoPlayer vp)
    {
        _vp.loopPointReached -= OnMakeEnded;

        int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 5; // 원본: 씬 5로 이동 
        _ = LoadSceneAsync(target, new[] { fadeImage1, fadeImage2, fadeImage3 }); // 공통 씬 전환
        _phase = Phase.Done;
    }

    // RMManager 내부 어디든 적당한 위치(예: 하단)에 추가

    #region Value Animation

// 현재 표시값을 기억해 중복 파싱 없이 애니메이션 시작점으로 사용
    private float _curVelocity, _curMaxVelocity, _curAltitude, _curSlope, _curRemainDistance;

// 항목별 애니메이션 취소 토큰 (새 갱신 시 이전 애니메이션 취소)
    private CancellationTokenSource _velCts, _maxVelCts, _altCts, _slopeCts, _remainCts;

    /// <summary>
    /// 라벨 값 변경을 애니메이션으로 표시
    /// - 작은 정수 변화: 1 → 2 → 3 순차 증가/감소
    /// - 그 외: Lerp 보간
    /// </summary>
    private async UniTask AnimateNumberChangeAsync(
        TextMeshProUGUI label, float from, float to, int decimals, string unit, CancellationToken token)
    {
        if (!label) return;

        bool bothInt = Mathf.Approximately(from, Mathf.Round(from)) && Mathf.Approximately(to, Mathf.Round(to));
        int deltaInt = Mathf.Abs(Mathf.RoundToInt(to) - Mathf.RoundToInt(from));
        bool stepMode = bothInt && deltaInt <= 10;

        if (stepMode)
        {
            int start = Mathf.RoundToInt(from);
            int end = Mathf.RoundToInt(to);
            int dir = (end >= start) ? 1 : -1;

            for (int v = start; v != end + dir; v += dir)
            {
                if (token.IsCancellationRequested) return;
                label.SetText($"{v}{unit}");
                float t = 0f;
                while (t < 0.1f)
                {
                    if (token.IsCancellationRequested) return;
                    t += Time.deltaTime;
                    await UniTask.Yield();
                }
            }

            return;
        }

        // Lerp 모드
        const float dur = 0.6f;
        float time = 0f;
        while (time < dur)
        {
            if (token.IsCancellationRequested) return;
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / dur);
            float v = Mathf.Lerp(from, to, t);
            label.SetText($"{v.ToString($"F{decimals}")}{unit}");
            await UniTask.Yield();
        }

        label.SetText($"{to.ToString($"F{decimals}")}{unit}");
    }

    /// <summary>
    /// 항목별 애니메이션 시작 헬퍼: 이전 애니메이션 취소→신규 토큰으로 시작
    /// </summary>
    private void StartLabelAnimation(TextMeshProUGUI label, ref float current, float next,
        ref CancellationTokenSource cts, int decimals, string unit)
    {
        if (float.IsNaN(current)) current = next;

        cts?.Cancel();
        cts?.Dispose();
        cts = new CancellationTokenSource();
        _ = AnimateNumberChangeAsync(label, current, next, decimals, unit, cts.Token);
        current = next;
    }

    #endregion

    #region Progress Bar
    
    private CancellationTokenSource _velBarCts, _maxBarCts, _altBarCts, _slopeBarCts, _remainBarCts;
    private const float BarAnimDur = 0.4f;

    private void InitializeProgressBar()
    {
        // 진행 바 베이스 이미지 세팅
        if (setting.progressBars != null)
        {
            if (setting.progressBars.Length > 0 && imageVelocityBar)
                SettingImageObject(imageVelocityBar.gameObject, setting.progressBars[0]);
            if (setting.progressBars.Length > 1 && imageMaxVelocityBar)
                SettingImageObject(imageMaxVelocityBar.gameObject, setting.progressBars[1]);
            if (setting.progressBars.Length > 2 && imageAltitudeBar)
                SettingImageObject(imageAltitudeBar.gameObject, setting.progressBars[2]);
            if (setting.progressBars.Length > 3 && imageSlopeBar)
                SettingImageObject(imageSlopeBar.gameObject, setting.progressBars[3]);
            if (setting.progressBars.Length > 4 && imageRemainDistanceBar)
                SettingImageObject(imageRemainDistanceBar.gameObject, setting.progressBars[4]);
        }
        
        // 모든 진행 바를 0으로 초기화
        if (imageVelocityBar)       { EnsureFilled(imageVelocityBar);       imageVelocityBar.fillAmount = 0f; }
        if (imageMaxVelocityBar)    { EnsureFilled(imageMaxVelocityBar);    imageMaxVelocityBar.fillAmount = 0f; }
        if (imageAltitudeBar)       { EnsureFilled(imageAltitudeBar);       imageAltitudeBar.fillAmount = 0f; }
        if (imageSlopeBar)          { EnsureFilled(imageSlopeBar);          imageSlopeBar.fillAmount = 0f; }
        if (imageRemainDistanceBar) { EnsureFilled(imageRemainDistanceBar); imageRemainDistanceBar.fillAmount = 0f; }
    }
    
    /// <summary> Image 타입을 Filled로 강제하고 좌→우 채우기로 표준화 </summary>
    private static void EnsureFilled(Image img)
    {
        if (!img) return;
        if (img.type != Image.Type.Filled) img.type = Image.Type.Filled;
        if (img.fillMethod != Image.FillMethod.Horizontal) img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = 0;        // Left
        img.fillClockwise = true;  // 좌→우
    }
    
    /// <summary> 0..BAR_MAX 스칼라 값을 Image.fillAmount로 애니메이션 </summary>
    private async UniTask AnimateBarAsync(Image img, float fromValue, float toValue, float duration, CancellationToken token)
    {
        if (!img) return;
        EnsureFilled(img);

        // from 을 현재 fill 기준으로 보정(안정성)
        float from = Mathf.Clamp01(fromValue / BarMax);
        float to   = Mathf.Clamp01(toValue   / BarMax);

        float t = 0f;
        while (t < duration)
        {
            if (token.IsCancellationRequested) return;
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);

            // 약간의 이징(스무스스텝)
            float s = u * u * (3f - 2f * u);

            img.fillAmount = Mathf.Lerp(from, to, s);
            await UniTask.Yield();
        }
        img.fillAmount = to;
    }

    /// <summary> 이전 애니메이션 취소 후 새 애니메이션 시작 </summary>
    private void StartBarAnimation(Image img, float currentValue, float nextValue,
        ref CancellationTokenSource cts, float duration = BarAnimDur)
    {
        if (!img) return;
        cts?.Cancel();
        cts?.Dispose();
        cts = new CancellationTokenSource();

        // 현재 value는 이미지의 fillAmount에서 역산(일관성)
        float cur = Mathf.Clamp01(img.fillAmount) * BarMax;
        if (!float.IsFinite(cur)) cur = currentValue; // 방어

        _ = AnimateBarAsync(img, cur, nextValue, duration, cts.Token);
    }

    /// <summary> 선택된 데이터로 모든 진행 바를 한 번에 갱신 </summary>
    private void UpdateProgressBars(RocketSetting src)
    {
        if (src == null) return;

        StartBarAnimation(imageVelocityBar,       0f, src.velocity,       ref _velBarCts);
        StartBarAnimation(imageMaxVelocityBar,    0f, src.maxVelocity,    ref _maxBarCts);
        StartBarAnimation(imageAltitudeBar,       0f, src.altitude,       ref _altBarCts);
        StartBarAnimation(imageSlopeBar,          0f, src.slope,          ref _slopeBarCts);
        StartBarAnimation(imageRemainDistanceBar, 0f, src.remainDistance, ref _remainBarCts);
    }

    #endregion
}