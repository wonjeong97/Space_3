using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

[Serializable]
public class RMSetting
{
    public float videoFadeTime;
    public ImageSetting main1;
    public ImageSetting main2;
    public ImageSetting main3;
    public ImageSetting sub1;

    public ImageSetting[] rockets;
    public ImageSetting[] satellites;

    public VideoSetting locationVideo;
    public VideoSetting rocketMakeVideo;
}

/// <summary>
/// 로켓/위성 선택 → 장소 영상 → 제작 영상 → 다음 씬
/// - 입력/페이드/씬 전환은 부모(SceneManager_Base) 공통 메서드 사용
/// - 비디오는 loopPointReached 이벤트 + async 전환으로 처리
/// </summary>
public class RMManager : SceneManager_Base<RMSetting>
{
    [Header("UI")]
    [SerializeField] private GameObject mainImage1;
    [SerializeField] private GameObject mainImage2;
    [SerializeField] private GameObject mainImage3;
    [SerializeField] private GameObject popupImage;        // 좌측 선택 미리보기
    [SerializeField] private GameObject videoPlayerObject; // RawImage + VideoPlayer
    [SerializeField] private GameObject subImage;

    protected override string JsonPath => "JSON/RMSetting.json";

    private enum Phase { SelectRocket, SelectSatellite, Location, PlayingMake, Done }
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
    }

    protected override async Task Init()
    {
        _vp = videoPlayerObject.GetComponent<VideoPlayer>();
        _raw = videoPlayerObject.GetComponent<RawImage>();
        _audio = UIUtility.GetOrAdd<AudioSource>(videoPlayerObject);

        _videoFadeTime = Mathf.Max(0f, setting.videoFadeTime);

        // 고정 이미지/서브 디스플레이 세팅
        SettingImageObject(mainImage1, setting.main1);
        SettingImageObject(mainImage2, setting.main2);
        SettingImageObject(mainImage3, setting.main3);
        SettingImageObject(subImage,  setting.sub1);

        popupImage.SetActive(false);

        // 장소 영상 세팅 
        await SettingVideoObject(videoPlayerObject, setting.locationVideo, _vp, _raw, _audio);
        _vp.Pause();
        _vp.time = 0;
        videoPlayerObject.SetActive(false);

        // 첫 진입 페이드 인
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage2, fadeImage3 }); // 

        // 입력 루프(좌/우/확인)
        while (_phase != Phase.Done)
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow))      MoveSelection(-1);
            else if (Input.GetKeyDown(KeyCode.RightArrow)) MoveSelection(+1);
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                if (await ConfirmAsync()) break; // Location 진입 시 true로 탈출
            }

            await Task.Yield();
        }
    }

    private void MoveSelection(int delta)
    {
        if (!canInput) return;

        if (_phase == Phase.SelectRocket)
        {
            int max = Mathf.Max(0, setting.rockets.Length - 1);
            
            int baseIndex = (_selectedRocket < 0) ? 0 : _selectedRocket;
            _selectedRocket = Mathf.Clamp(baseIndex + ((_selectedRocket < 0) ? 0 : delta), 0, max);

            SetSelectedImage(_selectedRocket, isRocket: true);
        }
        else if (_phase == Phase.SelectSatellite)
        {
            int max = Mathf.Max(0, setting.satellites.Length - 1);
            _selectedSatellite = Mathf.Clamp((_selectedSatellite < 0 ? 0 : _selectedSatellite) + delta, 0, max);
            SetSelectedImage(_selectedSatellite, isRocket: false);
        }
    }

    private void SetSelectedImage(int index, bool isRocket)
    {
        if (!popupImage) return;
        if (!popupImage.activeInHierarchy) popupImage.SetActive(true);

        if (isRocket)
        {
            if (index < 0 || index >= setting.rockets.Length) return;
            SettingImageObject(popupImage, setting.rockets[index]);
        }
        else
        {
            if (index < 0 || index >= setting.satellites.Length) return;
            SettingImageObject(popupImage, setting.satellites[index]);
        }
    }

    private async Task<bool> ConfirmAsync()
    {
        if (!canInput) return false;

        if (_phase == Phase.SelectRocket)
        {
            // 수정: 선택이 아직 없으면(= -1) 0번을 강제로 보여주고 계속 Rocket 단계 유지
            if (_selectedRocket < 0)
            {
                _selectedRocket = 0;
                SetSelectedImage(_selectedRocket, isRocket: true);
                return false; // 위성 단계로 넘어가지 않음
            }

            // 이미 선택된 상태면 위성 단계로 전환
            _phase = Phase.SelectSatellite;

            // 위성도 초기 표시는 0번으로 (선택이 없을 때)
            _selectedSatellite = Mathf.Clamp(_selectedSatellite, 0, Mathf.Max(0, setting.satellites.Length - 1));
            SetSelectedImage(_selectedSatellite, isRocket: false);
            return false;
        }

        if (_phase == Phase.SelectSatellite)
        {
            _phase = Phase.Location;
            await PlayLocationThenMakeAsync(); // 장소 -> 제작 영상 시퀀스
            return true;
        }

        return false;
    }

    /// <summary> 장소 영상 페이드 인 재생 → 종료 시 제작 영상으로 전환 </summary>
    private async Task PlayLocationThenMakeAsync()
    {
        canInput = false;

        // RawImage 투명으로, 오브젝트 활성화
        if (_raw)
        {
            var c = _raw.color;
            _raw.color = new Color(c.r, c.g, c.b, 0f);
        }
        videoPlayerObject.SetActive(true);

        // 장소 영상 재생 설정 및 재생 시작 (원본 ActivateLocationVideo 기준) 
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
            var c = _raw.color; _raw.color = new Color(c.r, c.g, c.b, a);
            await Task.Yield();
        }
    }

    /// <summary> 장소 영상 종료 → 화면 페이드 → 제작 영상 세팅·재생 → 화면 복원 </summary>
    private async void OnLocationEnded(VideoPlayer vp)
    {
        _vp.loopPointReached -= OnLocationEnded;

        try
        {
            // 화면 덮기
            await FadeImageAsync(0f, 1f, fadeTime, new[] { fadeImage1 }); // 

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

    /// <summary> 제작 영상 종료 → 다음 씬 </summary>
    private void OnMakeEnded(VideoPlayer vp)
    {
        _vp.loopPointReached -= OnMakeEnded;

        int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 5; // 원본: 씬 5로 이동 
        _ = LoadSceneAsync(target, new[] { fadeImage1, fadeImage2, fadeImage3 }); // 공통 씬 전환
        _phase = Phase.Done;
    }
}
