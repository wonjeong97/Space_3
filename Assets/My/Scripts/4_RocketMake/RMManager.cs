using System;
using System.Collections;
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

public class RMManager : SceneManager_Base<RMSetting>
{
    [Header("UI")] 
    [SerializeField] private GameObject mainImage1;
    [SerializeField] private GameObject mainImage2;
    [SerializeField] private GameObject mainImage3;
    [SerializeField] private GameObject popupImage;
    [SerializeField] private GameObject videoPlayerObject;
    [SerializeField] private GameObject subImage1;

    protected override string JsonPath => "JSON/RMSetting.json";

    private enum Phase
    {
        SelectRocket,
        SelectSatellite,
        Location
    }

    private Phase _phase = Phase.SelectRocket;
    private int _selectedRocket = -1;
    private int _selectedSatellite = -1;

    private VideoPlayer _vp;
    private RawImage _raw;
    private AudioSource _audioSource;
    
    private float _videoFadeTime;

    private void Update()
    {
        if (!canInput) return;

        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            if (_phase == Phase.SelectRocket)
            {
                if (_selectedRocket > 0) _selectedRocket--;
                SetSelectedImage(_selectedRocket);
            }
            else if (_phase == Phase.SelectSatellite)
            {
                if (_selectedSatellite > 0) _selectedSatellite--;
                SetSelectedImage(_selectedSatellite);
            }
        }
        else if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            if (_phase == Phase.SelectRocket)
            {
                if (_selectedRocket < setting.rockets.Length - 1) _selectedRocket++;
                SetSelectedImage(_selectedRocket);
            }
            else if (_phase == Phase.SelectSatellite)
            {
                if (_selectedSatellite < setting.satellites.Length - 1) _selectedSatellite++;
                SetSelectedImage(_selectedSatellite);
            }
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            EnterConfirm();
        }
    }

    protected override async Task Init()
    {
        _vp = videoPlayerObject.GetComponent<VideoPlayer>();
        _raw = videoPlayerObject.GetComponent<RawImage>();
        _audioSource = UIUtility.GetOrAdd<AudioSource>(videoPlayerObject);
        
        _videoFadeTime = setting.videoFadeTime;

        SettingImageObject(mainImage1, setting.main1);
        SettingImageObject(mainImage2, setting.main2);
        SettingImageObject(mainImage3, setting.main3);
        SettingImageObject(popupImage, setting.rockets[0]);
        SettingImageObject(subImage1, setting.sub1);
        
        popupImage.SetActive(false);

        await SettingVideoObject(videoPlayerObject, setting.locationVideo, _vp, _raw, _audioSource);
        videoPlayerObject.SetActive(false);

        StartCoroutine(FadeImage(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage2, fadeImage3 }));
    }
    
    /// <summary> 가장 좌측 이미지를 index에 따라 텍스쳐를 변경함 </summary>
    private void SetSelectedImage(int index)
    {
        if (index < 0) return;
        if (_phase == Phase.SelectRocket && index >= setting.rockets.Length) return;
        if (_phase == Phase.SelectSatellite && index >= setting.satellites.Length) return;

        if (!popupImage.activeInHierarchy) popupImage.SetActive(true);
        
        if (_phase == Phase.SelectRocket)
        {
            SettingImageObject(popupImage, setting.rockets[index]);
        }
        else if (_phase == Phase.SelectSatellite)
        {   
            SettingImageObject(popupImage, setting.satellites[index]);
        }
    }
    
    /// <summary> 엔터(가운데 버튼)를 눌렀을 때 메서드 </summary>
    private void EnterConfirm()
    {
        if (_phase == Phase.SelectRocket)
        {
            _phase = Phase.SelectSatellite;
            _selectedSatellite = 0;
            SettingImageObject(popupImage, setting.satellites[_selectedSatellite]);
        }
        else if (_phase == Phase.SelectSatellite)
        {
            _phase = Phase.Location;
            StartCoroutine(ActivateLocationVideo());
        }
    }
    
    /// <summary> 발사 장소 표시 비디오 재생 </summary>
    private IEnumerator ActivateLocationVideo()
    {
        canInput = false;

        if (_raw)
        {
            Color c = _raw.color;
            _raw.color = new Color(c.r, c.g, c.b, 0f);
        }

        videoPlayerObject.SetActive(true);
        _vp.isLooping = false;
        _vp.loopPointReached -= OnLocationEnded;
        _vp.loopPointReached += OnLocationEnded;
        _vp.Play();

        yield return StartCoroutine(FadeRawImage(_raw, 0f, 1f, _videoFadeTime));
    }

    /// <summary> 발사 장소 표시 비디오가 끝나면 발사체 다단 제작 이유 비디오 재생 </summary>
    private async void OnLocationEnded(VideoPlayer vp)
    {
        try
        {
            await FadeImageAsync(0f, 1f, fadeTime, new[] { fadeImage1 });
            _vp.loopPointReached -= OnLocationEnded;
            
            await SettingVideoObject(videoPlayerObject, setting.rocketMakeVideo, _vp, _raw, _audioSource);
            _vp.isLooping = false;
            _vp.loopPointReached -= OnRocketMakeEnded;
            _vp.loopPointReached += OnRocketMakeEnded;
            _vp.Play();
            
            await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1 });
        }
        catch (Exception e)
        {
            Debug.LogError($"[RMManager] Failed to set rocketMakeVideo: {e}");

            canInput = true;
        }
    }

    /// <summary> 발사체 다단 제작 이유 비디오 종료 후 다음 씬 전환 </summary>
    private void OnRocketMakeEnded(VideoPlayer vp)
    {
        _vp.loopPointReached -= OnRocketMakeEnded;
        StartCoroutine(FadeAndLoadScene(5, new[] { fadeImage1, fadeImage2, fadeImage3 }));
    }

    /// <summary> Raw Image 페이드 </summary>
    private IEnumerator FadeRawImage(RawImage raw, float start, float end, float duration)
    {
        if (!raw || duration <= 0f)
        {
            if (raw)
            {
                Color color = raw.color;
                raw.color = new Color(color.r, color.g, color.b, end);
            }

            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float time = Mathf.Clamp01(elapsed / duration);
            float alpha = Mathf.Lerp(start, end, time);
            Color color = raw.color;
            raw.color = new Color(color.r, color.g, color.b, alpha);
            yield return null;
        }

        Color c = raw.color;
        raw.color = new Color(c.r, c.g, c.b, end);
    }
}