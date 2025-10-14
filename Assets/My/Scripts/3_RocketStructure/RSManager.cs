using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

[Serializable]
public class RSSetting
{
    public float transitionTime;
    public ImageSetting[] explainImages;
    public VideoSetting structureVideo;
}

/// <summary> 우주발사체의 구조와 기능 씬 관리 매니저 </summary>
public class RSManager : SceneManager_Base<RSSetting>
{
    [Header("UI")]
    [SerializeField] private List<GameObject> explainImageObjs;
    [SerializeField] private GameObject videoPlayerObject;

    protected override string JsonPath => "JSON/RSSetting.json";

    private int _index;
    private float _crossTime;
    
    private VideoPlayer _vp;
    private RawImage _raw;
    private AudioSource _audio;
    
    protected override async UniTask Init()
    {   
        // ===========================================================
        // 이미지 크로스페이드 로직 (임시 비활성화)
        // ===========================================================
        /*
        _crossTime = Mathf.Max(0f, setting.transitionTime);
        
        // 설정 개수와 오브젝트 개수 동기화
        // 오브젝트 or 세팅 중 더 작은 개수를 사용하여 null 에러 방지
        int count = Mathf.Min(explainImageObjs.Count, setting.explainImages.Length);
        for (int i = 0; i < count; i++)
        {
            // 이미지 세팅 후 숨김
            SettingImageObject(explainImageObjs[i], setting.explainImages[i]);
            if (explainImageObjs[i]) explainImageObjs[i].SetActive(false); 
        }
        
        // 첫 번째 이미지만 활성화
        _index = 0;
        if (count > 0 && explainImageObjs[0])
        {
            explainImageObjs[0].SetActive(true);
            if (explainImageObjs[0].TryGetComponent(out Image img0))
            {
                var c = img0.color; c.a = 1f; img0.color = c;
            }
        }
        
        ArduinoInputManager.Instance?.SetLedAll(true);    
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage3 });
        
        TurnCamera3Async(this.GetCancellationTokenOnDestroy()).Forget();
        while (true)
        {   
            // 입력 대기
            while (true)
            {
                if (ArduinoInputManager.Instance && ArduinoInputManager.Instance.TryConsumeAnyPress(out _)) break;
                if (TryConsumeSingleInput()) break;
                
                await UniTask.Yield();
            }
            if (_index >= count - 1) break; // 마지막이면 루프 종료 → 씬 전환

            // 현재 이미지 -> 다음 이미지로 크로스페이드
            await AdvanceStepAsync(explainImageObjs[_index], explainImageObjs[_index + 1], _crossTime);
            _index++;
        }

        // 다음 씬 전환
        int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 4;
        await LoadSceneAsync(target, new[] { fadeImage1, fadeImage3 });
        */
        
        // ===========================================================
        // 단일 동영상 재생 -> 끝나면 다음 씬
        // ===========================================================
        if (!videoPlayerObject)
        {
            Debug.LogError("[RSManager] videoPlayerObject is not assigned");
        }
        
        _vp = videoPlayerObject ? videoPlayerObject.GetComponent<VideoPlayer>() : null;
        _raw = videoPlayerObject ? videoPlayerObject.GetComponent<RawImage>() : null;
        _audio = videoPlayerObject ? videoPlayerObject.GetComponent<AudioSource>() : null;

        await SettingVideoObject(videoPlayerObject, setting.structureVideo, _vp, _raw, _audio);
        
        if (_vp != null)
        {
            _vp.isLooping = false;
            _vp.loopPointReached -= OnVideoEnded;
            _vp.loopPointReached += OnVideoEnded;
        }
        
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage3 });
    }
    
    private async void OnVideoEnded(VideoPlayer vp)
    {
        try
        {
            if (_vp != null) _vp.loopPointReached -= OnVideoEnded;
            
            int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 4;
            await LoadSceneAsync(target, new[] { fadeImage1, fadeImage3 });
        }
        catch (Exception e)
        {
            Debug.LogError($"[RSManager] OnVideoEnded Exception: {e}");
        }
    }
    
    protected override void OnDisable()
    {
        if (_vp != null)
        {
            _vp.loopPointReached -= OnVideoEnded;
            _vp.Stop();
        }
    }
}
