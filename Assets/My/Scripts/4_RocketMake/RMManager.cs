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

    public ImageSetting[] main1Children;

    public RocketSetting[] rockets;
    public RocketSetting[] satellites;
    public ImageSetting[] progressBars;

    public VideoSetting[] locationVideo;
    public VideoSetting[] rocketMakeVideo;
}

/// <summary>
/// 우주발사체를 다단(3단)으로 제작하는 이유 씬 매니저
/// 발사체, 위성 선택 -> 발사 장소 영상 -> 발사체 다단 제작 영상 → 다음 씬
/// </summary>
public class RMManager : SceneManager_Base<RMSetting>
{
    #region Serialized

    [Header("UI")] [SerializeField] private GameObject backgroundImage;
    [SerializeField] private GameObject mainImage1;
    [SerializeField] private GameObject mainImage2;
    [SerializeField] private GameObject mainImage3;

    [Header("mainImage1")] [SerializeField]
    private Image[] main1ChildrenImages;

    [Header("mainImage3")] [SerializeField]
    private GameObject rocketImage; // 발사체, 위성 선택 이미지

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

    #endregion

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

    // 비디오
    private VideoPlayer _vp;
    private RawImage _raw;
    private AudioSource _audio;

    // 전환/루프 대기
    private bool _isSwitching;
    private bool _awaitingSkip;

    // 배열 인덱스
    private int _locIndex;
    private int _makeIndex;

    private float _videoFadeTime;

    // RT 최적화
    private RenderTexture _lastRT;

    private CancellationTokenSource _main1AlphaCts;
    private CancellationTokenSource _skipCts;
    private CancellationTokenSource _velCts, _maxVelCts, _altCts, _slopeCts, _remainCts;

    // 현재 표시값을 기억해 중복 파싱 없이 애니메이션 시작점으로 사용
    private float _curVelocity, _curMaxVelocity, _curAltitude, _curSlope, _curRemainDistance;

    #region Unity

    protected override void OnDisable()
    {
        if (_vp)
        {
            _vp.loopPointReached -= OnLocationEnded;
            _vp.loopPointReached -= OnMakeEnded;
            _vp.Stop();
        }

        CancelAndDispose(ref _skipCts);
        CancelAndDispose(ref _main1AlphaCts);
        CancelAndDispose(ref _velCts);
        CancelAndDispose(ref _maxVelCts);
        CancelAndDispose(ref _altCts);
        CancelAndDispose(ref _slopeCts);
        CancelAndDispose(ref _remainCts);
        CancelAndDispose(ref _velBarCts);
        CancelAndDispose(ref _maxBarCts);
        CancelAndDispose(ref _altBarCts);
        CancelAndDispose(ref _slopeBarCts);
        CancelAndDispose(ref _remainBarCts);

        if (_lastRT != null)
        {
            if (_lastRT.IsCreated()) _lastRT.Release();
            Destroy(_lastRT);
            _lastRT = null;
        }
    }

    protected override async UniTask Init()
    {
        if (videoPlayerObject == null)
        {
            Debug.LogError("[RMManager] videoPlayerObject is not assigned");
            return;
        }

        _vp = videoPlayerObject.GetComponent<VideoPlayer>();
        _raw = videoPlayerObject.GetComponent<RawImage>();
        _audio = videoPlayerObject.GetComponent<AudioSource>();

        _videoFadeTime = Mathf.Max(0f, setting.videoFadeTime);

        // 고정 이미지/서브 디스플레이 세팅
        SettingImageObject(backgroundImage, setting.background);
        SettingImageObject(mainImage1, setting.main1);
        SettingImageObject(mainImage2, setting.main2);
        SettingImageObject(mainImage3, setting.main3);
        SettingImageObject(subImage, setting.sub1);

        // mainImage1의 자식 이미지들 세팅
        if (setting.main1Children != null && main1ChildrenImages != null)
        {
            int count = Mathf.Min(setting.main1Children.Length, main1ChildrenImages.Length);
            for (int i = 0; i < count; i++)
            {
                if (main1ChildrenImages[i] == null) continue;
                SettingImageObject(main1ChildrenImages[i].gameObject, setting.main1Children[i]);
            }
        }

        if (main1ChildrenImages != null && main1ChildrenImages.Length > 0 && main1ChildrenImages[0] != null)
        {
            StartAlphaPingPong(main1ChildrenImages[0], 0.28f, 1.0f, 2.0f, ref _main1AlphaCts);
        }

        InitializeProgressBar();
        if (rocketImage) rocketImage.SetActive(false);

        // 비디오 오브젝트는 처음에 비활성화
        if (_raw)
        {
            Color c = _raw.color;
            _raw.color = new Color(c.r, c.g, c.b, 0f);
        }

        videoPlayerObject.SetActive(false);

        _locIndex = 0;
        _makeIndex = 0;

        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage2, fadeImage3 });
        ArduinoInputManager.Instance?.SetLedAll(true);
        StartBlinkGreenAsync(500, 160);

        // 입력 루프(좌/우/확인)
        while (_phase != Phase.Done)
        {
            if (!ArduinoInputManager.Instance) return;

            ArduinoInputManager.ButtonId btn;
            bool pressed = ArduinoInputManager.Instance.TryConsumeAnyPress(out btn);

            if ((pressed && btn == ArduinoInputManager.ButtonId.Button1) || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                MoveSelection(-1);
            }
            else if ((pressed && btn == ArduinoInputManager.ButtonId.Button3) || Input.GetKeyDown(KeyCode.RightArrow))
            {
                MoveSelection(+1);
            }
            else if ((pressed && btn == ArduinoInputManager.ButtonId.Button2) || Input.GetKeyDown(KeyCode.DownArrow))
            {
                if (await ConfirmAsync()) break; // Location 진입 시 true로 탈출
            }

            ArduinoInputManager.Instance.FlushAll();
            await UniTask.Yield();
        }
    }

    #endregion

    #region Selection / Confirm

    /// <summary> 사용자의 입력에 따라 인덱스를 바꾸고 이미지를 변경함 </summary>
    private void MoveSelection(int delta)
    {
        if (!canInput) return;

        if (_phase == Phase.SelectRocket)
        {
            int max = Mathf.Max(0, setting.rockets.Length - 1);

            int baseIndex = (_selectedRocket < 0) ? 0 : _selectedRocket;
            _selectedRocket = Mathf.Clamp(baseIndex + ((_selectedRocket < 0) ? 0 : delta), 0, max);

            SetSelected(_selectedRocket, true);
        }
        else if (_phase == Phase.SelectSatellite)
        {
            int max = Mathf.Max(0, setting.satellites.Length - 1);
            _selectedSatellite = Mathf.Clamp((_selectedSatellite < 0 ? 0 : _selectedSatellite) + delta, 0, max);
            SetSelected(_selectedSatellite, false);
        }
    }

    /// <summary> 로켓/위성 선택 시 UI와 수치/바를 갱신 </summary>
    private void SetSelected(int index, bool isRocket)
    {
        if (!rocketImage) return;
        if (!rocketImage.activeInHierarchy) rocketImage.SetActive(true);

        // 첫 진입 점프 방지: 초기값 NaN 한 번 세팅
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
            src = setting.satellites[index];
        }

        // 이미지 교체
        SettingImageObject(rocketImage, src.rocketImage);

        // 숫자 라벨 애니메이션 시작
        StartLabelAnimation(textVelocity, ref _curVelocity, src.velocity, ref _velCts, 0, " km/s");
        StartLabelAnimation(textMaxVelocity, ref _curMaxVelocity, src.maxVelocity, ref _maxVelCts, 0, " km/s");
        StartLabelAnimation(textAltitude, ref _curAltitude, src.altitude, ref _altCts, 0, " km");
        StartLabelAnimation(textSlope, ref _curSlope, src.slope, ref _slopeCts, 0, " °");
        StartLabelAnimation(textRemainDistance, ref _curRemainDistance, src.remainDistance, ref _remainCts, 0, " km");

        // 진행 바 즉시 갱신
        UpdateProgressBars(src);
    }

    /// <summary> 확인 버튼을 눌렀을 때 동작: 로켓→위성→장소 시퀀스 시작 </summary>
    private async UniTask<bool> ConfirmAsync()
    {
        if (!canInput) return false;

        if (_phase == Phase.SelectRocket)
        {
            if (_selectedRocket < 0)
            {
                _selectedRocket = 0;
                SetSelected(_selectedRocket, true);
                return false;
            }

            _phase = Phase.SelectSatellite;
            _selectedSatellite = Mathf.Clamp(_selectedSatellite, 0, Mathf.Max(0, setting.satellites.Length - 1));
            SetSelected(_selectedSatellite, false);
            return false;
        }

        if (_phase == Phase.SelectSatellite)
        {
            _phase = Phase.Location;
            StopLedEffects();
            ArduinoInputManager.Instance?.SetLedAll(false);
            LedStrip.Range(0, 9, 255, 0, 0);
            await StartLocationSequenceAsync();
            return true;
        }

        return false;
    }

    #endregion

    #region Video Sequences (arrays)

    /// <summary> 장소 영상 배열 시퀀스 시작 </summary>
    private async UniTask StartLocationSequenceAsync()
    {
        // '거치' 알파 애니메이션 해제
        CancelAndDispose(ref _main1AlphaCts);
        _locIndex = 0;

        // 영상 재생 전 뒷배경 청소
        if (mainImage1) mainImage1.SetActive(false);
        if (mainImage2) mainImage2.SetActive(false);
        if (mainImage3) mainImage3.SetActive(false);

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
            OnMakeSequenceCompleted();
            return;
        }

        await SwitchAndPlayNextAsync(setting.rocketMakeVideo[_makeIndex], true);
    }

    /// <summary>
    /// 다음 비디오로 전환 후 재생.
    /// withFade=true면 화면 덮고 세팅/재생 후 복원(안전), false면 즉시 전환.
    /// Loop 영상이면 사용자 입력 대기를 걸고, 아니면 자연 종료 이벤트로 다음 처리.
    /// </summary>
    private async UniTask SwitchAndPlayNextAsync(VideoSetting next, bool withFade)
    {
        if (_isSwitching) return;
        _isSwitching = true;

        // 이전 Loop 대기 정리
        CancelAndDispose(ref _skipCts);

        _awaitingSkip = false;
        inputReceived = false;

        // 다음 클립 RT 준비 시, 페이드가 없을 때는 마지막 프레임을 유지
        bool holdLastFrame = !withFade;

        if (withFade)
            await FadeImageAsync(0f, 1f, fadeTime, new[] { fadeImage1 });

        // 비디오 오브젝트 활성화 및 Rect 적용
        if (!videoPlayerObject.activeSelf)
            videoPlayerObject.SetActive(true);

        if (videoPlayerObject.TryGetComponent(out RectTransform rt))
        {
            UIUtility.ApplyRect(
                rt,
                size: next.size,
                anchoredPos: new Vector2(next.position.x, -next.position.y),
                rotation: Vector3.zero
            );
        }

        // 현재 프레임 고정(Stop 대신 Pause/속도 0)
        if (_vp != null)
        {
            _vp.Pause();
            _vp.playbackSpeed = 0f;
        }

        // RT 준비: 동일 사이즈면 재사용, 다르면 새 RT를 미리 VideoPlayer에만 연결
        Vector2Int desired = new Vector2Int(Mathf.RoundToInt(next.size.x), Mathf.RoundToInt(next.size.y));
        RenderTexture keepShowing = _raw != null ? _raw.texture as RenderTexture : null;
        RenderTexture rtForNext = VideoManager.Instance.EnsureRenderTexture(_vp, _raw, desired, reuseIfSame: holdLastFrame);

        // 다음 영상 준비, RawImage는 그대로 마지막 프레임을 계속 보여줌
        string url = VideoManager.Instance.ResolvePlayableUrl(next.fileName);
        bool isLoop = IsLoopClip(next);
        double timeout = next.fileName != null && next.fileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ? 20.0 : 10.0;

        bool ok = await VideoManager.Instance.PrepareAndPlayAsync(_vp, url, _audio, next.volume, this.GetCancellationTokenOnDestroy(), timeout);

        if (_vp)
        {
            _vp.loopPointReached -= OnLocationEnded;
            _vp.loopPointReached -= OnMakeEnded;
        }

        if (!isLoop)
        {
            if (_phase == Phase.Location) _vp.loopPointReached += OnLocationEnded;
            if (_phase == Phase.PlayingMake) _vp.loopPointReached += OnMakeEnded;
        }

        if (!ok)
        {
            Debug.LogError("[RMManager] Prepare failed: " + url);
            _isSwitching = false;
            if (withFade) await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1 });
            return;
        }

        // 첫 프레임 생성까지 가드 대기(프레임/텍스처 체크)
        int guard = 0;
        while (guard++ < 5 && _vp != null && _vp.texture == null && _vp.frame <= 0)
            await UniTask.Yield();

        // 화면에 스왑 (사이즈 동일 재사용이면 이미 보이는 중이므로 스왑 불필요)
        if (_raw != null && rtForNext != null && keepShowing != rtForNext)
        {
            if (_lastRT != null && _lastRT != rtForNext && _lastRT != keepShowing)
            {
                if (_lastRT.IsCreated()) _lastRT.Release();
                Destroy(_lastRT);
            }

            _raw.texture = rtForNext; // 깜빡임 없이 교체
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
            _awaitingSkip = true;
            ArduinoInputManager.Instance?.SetLedAll(true);
            StartBlinkGreenAsync(500, 160);

            _skipCts = new CancellationTokenSource();
            int loc = _locIndex;
            int make = _makeIndex;
            _ = WaitSkipThenProceedAsync(_skipCts.Token, loc, make);
        }

        if (withFade)
            await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1 });

        _isSwitching = false;
    }

    /// <summary>
    /// Loop 영상에서 사용자 입력이 들어오면 현재 시퀀스의 다음 아이템으로 진행
    /// </summary>
    private async UniTask WaitSkipThenProceedAsync(CancellationToken token, int locIndexAtStart, int makeIndexAtStart)
    {
        if (ArduinoInputManager.Instance != null) ArduinoInputManager.Instance.FlushAll();
        await UniTask.Yield();

        while (true)
        {
            if (token.IsCancellationRequested) return;
            if (_phase != Phase.Location && _phase != Phase.PlayingMake) return;

            bool arduinoPressed = ArduinoInputManager.Instance != null &&
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
        if (_vp)
        {
            _vp.loopPointReached -= OnLocationEnded;
            _vp.loopPointReached -= OnMakeEnded;
            _vp.Stop();
        }

        StopLedEffects();
        ArduinoInputManager.Instance?.SetLedAll(false);
        LedStrip.Range(0, 9, 255, 0, 0);

        _awaitingSkip = false;
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
                OnMakeSequenceCompleted();
            }
        }
    }

    /// <summary> 장소 영상 하나가 자연 종료되면 다음 장소(또는 제작 시퀀스)로 </summary>
    private async void OnLocationEnded(VideoPlayer vp)
    {
        try
        {
            if (_vp) _vp.loopPointReached -= OnLocationEnded;

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
            Debug.LogError($"[RMManager] OnLocationEnded Exception: {e}");
        }
    }

    /// <summary> 제작 영상 하나가 자연 종료되면 다음 제작(또는 다음 씬)으로 </summary>
    private async void OnMakeEnded(VideoPlayer vp)
    {
        try
        {
            _vp.loopPointReached -= OnMakeEnded;

            _makeIndex++;
            if (setting.rocketMakeVideo != null && _makeIndex < setting.rocketMakeVideo.Length)
            {
                await SwitchAndPlayNextAsync(setting.rocketMakeVideo[_makeIndex], false);
            }
            else
            {
                OnMakeSequenceCompleted();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[RMManager] OnMakeEnded Exception: {e}");
        }
    }

    /// <summary> 제작 시퀀스 종료 → 다음 씬 </summary>
    private void OnMakeSequenceCompleted()
    {
        int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 5;
        _ = LoadSceneAsync(target, new[] { fadeImage1, fadeImage2, fadeImage3 });
        _phase = Phase.Done;
    }

    /// <summary>
    /// VideoSetting이 루프형인지 판단:
    /// - name 이 "…Loop"로 끝나거나
    /// - fileName(확장자 제거)이 "…Loop"로 끝나면 true
    /// </summary>
    private static bool IsLoopClip(VideoSetting vs)
    {
        if (vs == null) return false;

        string n = string.IsNullOrEmpty(vs.name) ? string.Empty : vs.name;
        if (n.EndsWith("Loop", StringComparison.OrdinalIgnoreCase)) return true;

        string fn = string.IsNullOrEmpty(vs.fileName) ? string.Empty : vs.fileName;
        string stem = string.IsNullOrEmpty(fn) ? string.Empty : System.IO.Path.GetFileNameWithoutExtension(fn);
        return stem.EndsWith("Loop", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Value Animation

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
            float u = Mathf.Clamp01(time / dur);
            float v = Mathf.Lerp(from, to, u);
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

        if (cts != null) CancelAndDispose(ref cts);
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
        if (imageVelocityBar)
        {
            EnsureFilled(imageVelocityBar);
            imageVelocityBar.fillAmount = 0f;
        }

        if (imageMaxVelocityBar)
        {
            EnsureFilled(imageMaxVelocityBar);
            imageMaxVelocityBar.fillAmount = 0f;
        }

        if (imageAltitudeBar)
        {
            EnsureFilled(imageAltitudeBar);
            imageAltitudeBar.fillAmount = 0f;
        }

        if (imageSlopeBar)
        {
            EnsureFilled(imageSlopeBar);
            imageSlopeBar.fillAmount = 0f;
        }

        if (imageRemainDistanceBar)
        {
            EnsureFilled(imageRemainDistanceBar);
            imageRemainDistanceBar.fillAmount = 0f;
        }
    }

    /// <summary> Image 타입을 Filled로 강제하고 좌→우 채우기로 표준화 </summary>
    private static void EnsureFilled(Image img)
    {
        if (!img) return;
        if (img.type != Image.Type.Filled) img.type = Image.Type.Filled;
        if (img.fillMethod != Image.FillMethod.Horizontal) img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = 0; // Left
        img.fillClockwise = true; // 좌→우
    }

    /// <summary> 0..BAR_MAX 스칼라 값을 Image.fillAmount로 애니메이션 </summary>
    private async UniTask AnimateBarAsync(Image img, float fromValue, float toValue, float duration, CancellationToken token)
    {
        if (!img) return;
        EnsureFilled(img);

        float from = Mathf.Clamp01(fromValue / BarMax);
        float to = Mathf.Clamp01(toValue / BarMax);

        float t = 0f;
        while (t < duration)
        {
            if (token.IsCancellationRequested) return;
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);

            // 스무스스텝 이징
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
        if (cts != null) CancelAndDispose(ref cts);
        cts = new CancellationTokenSource();

        float cur = Mathf.Clamp01(img.fillAmount) * BarMax;

        bool IsFiniteFloat(float x)
        {
            return !(float.IsNaN(x) || float.IsInfinity(x));
        }

        if (!IsFiniteFloat(cur)) cur = currentValue;

        _ = AnimateBarAsync(img, cur, nextValue, duration, cts.Token);
    }

    /// <summary> 선택된 데이터로 모든 진행 바를 한 번에 갱신 </summary>
    private void UpdateProgressBars(RocketSetting src)
    {
        if (src == null) return;

        StartBarAnimation(imageVelocityBar, 0f, src.velocity, ref _velBarCts);
        StartBarAnimation(imageMaxVelocityBar, 0f, src.maxVelocity, ref _maxBarCts);
        StartBarAnimation(imageAltitudeBar, 0f, src.altitude, ref _altBarCts);
        StartBarAnimation(imageSlopeBar, 0f, src.slope, ref _slopeBarCts);
        StartBarAnimation(imageRemainDistanceBar, 0f, src.remainDistance, ref _remainBarCts);
    }

    #endregion

    /// <summary>
    /// 디버그 스킵 입력 처리
    /// - 모든 영상/대기 상태 정리 -> 즉시 다음 씬 이동
    /// </summary>
    protected override void OnDebugSkip()
    {
        try
        {
            // 루프 대기 태스크 취소
            _skipCts?.Cancel();
            _skipCts?.Dispose();
            _skipCts = null;

            // 비디오 이벤트 해제 및 정지
            if (_vp)
            {
                _vp.loopPointReached -= OnLocationEnded;
                _vp.loopPointReached -= OnMakeEnded;
                if (_vp.isPlaying) _vp.Stop();
            }

            // LED/이펙트 정리
            StopLedEffects();
            ArduinoInputManager.Instance?.SetLedAll(false);
            LedStrip.Range(0, 9, 255, 0, 0);

            // 4) 상태 정리 후 다음 씬 전환
            _awaitingSkip = false;
            _isSwitching = false;
            _phase = Phase.Done;

            int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 5;
            _ = LoadSceneAsync(target, new[] { fadeImage1, fadeImage2, fadeImage3 });
        }
        catch (Exception e)
        {
            Debug.LogError($"[RMManager] OnDebugSkip Exception: {e}");
        }
    }
}