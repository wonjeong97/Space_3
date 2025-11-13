using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

[Serializable]
public class RSSetting
{
    public float transitionTime;
    public ImageSetting subImage;
    public ImageSetting[] explainImages;
    public VideoSetting structureVideo;
}

/// <summary> 우주발사체의 구조와 기능 씬 관리 매니저 </summary>
public sealed class RSManager : SceneManager_Base<RSSetting>
{
    // ===== JSON 경로 =====
    protected override string JsonPath => "JSON/RSSetting.json";

    #region Serialized Refs

    [Header("UI")]
    [SerializeField] private GameObject videoPlayerObject;   // 비디오 플레이어 + RawImage + AudioSource가 붙은 오브젝트
    [SerializeField] private List<GameObject> explainImageObjs; // (비활성화됨) 이미지 시퀀스 오브젝트 목록
    [SerializeField] private GameObject subImage;
    
    #endregion

    #region Settings / State
    
    private AudioSource _audio;   // 비디오 오디오 소스
    private float _crossTime;     // 이미지 크로스 페이드 시간(현재 미사용)
    private int _index;           // 이미지 시퀀스 인덱스(현재 미사용)
    private RawImage _raw;        // 비디오 출력용 RawImage
    private VideoPlayer _vp;      // 비디오 플레이어

    #endregion

    #region Initialization

    /// <summary> 초기화: 비디오 준비/바인딩 → 첫 프레임 보장 → 페이드 인 </summary>
    protected override async UniTask Init()
    {
        // (참고) 이미지 크로스페이드는 현재 미사용. 필요 시 _crossTime 및 explainImageObjs 로직 활성화
        _crossTime = Mathf.Max(0f, setting.transitionTime);
        
        if (!videoPlayerObject)
        {
            Debug.LogError("[RSManager] Init-> 비디오 플레이어 오브젝트가 지정되지 않았습니다");
            return;
        }
        
        SettingImageObject(subImage, setting.subImage);
        
        _vp = videoPlayerObject.GetComponent<VideoPlayer>();
        _raw = videoPlayerObject.GetComponent<RawImage>();
        _audio = videoPlayerObject.GetComponent<AudioSource>();

        try
        {
            await SettingVideoObject(videoPlayerObject, setting.structureVideo, _vp, _raw, _audio);
        }
        catch (Exception e)
        {
            Debug.LogError($"[RSManager] Init-> 비디오 세팅 중 예외: {e}");
            return;
        }

        if (_vp != null)
        {
            try
            {
                _vp.isLooping = false;
                _vp.loopPointReached -= OnVideoEnded;
                _vp.loopPointReached += OnVideoEnded;
                BindInactivityPolicyToVideo(_vp, false, DestroyToken);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RSManager] Init-> VideoPlayer 바인딩 중 예외: {e.Message}");
            }
        }

        // 카메라 회전 시작(세로 디스플레이)
        TurnCamera3Async(DestroyToken).Forget();

        // 첫 프레임이 렌더될 때까지 잠깐 대기(깜빡임 방지)
        try
        {
            CancellationToken destroyToken = DestroyToken;
            await WaitFirstFrameAsync(_vp, _raw, destroyToken, 2.0);
            await UniTask.Delay(TimeSpan.FromMilliseconds(50), cancellationToken: destroyToken);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RSManager] Init-> 첫 프레임 대기 중 예외: {e.Message}");
        }

        // 페이드 인
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage2, fadeImage3 });
    }

    #endregion

    #region Video Flow

    /// <summary> 비디오가 끝나면 다음 씬으로 전환 </summary>
    private async void OnVideoEnded(VideoPlayer vp)
    {
        try
        {
            if (_vp) _vp.loopPointReached -= OnVideoEnded;

            int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 4;
            await LoadSceneAsync(target, new[] { fadeImage1, fadeImage2, fadeImage3 });
        }
        catch (Exception e)
        {
            Debug.LogError($"[RSManager] OnVideoEnded-> 예외: {e}");
        }
    }

    #endregion

    #region Unity Life-Cycle

    /// <summary> 비활성화: 이벤트 해제 및 비디오 정지 </summary>
    protected override void OnDisable()
    {   
        base.OnDisable();
        
        if (_vp)
        {
            try { _vp.loopPointReached -= OnVideoEnded; }
            catch (Exception e) { Debug.LogWarning($"[RSManager] OnDisable-> 이벤트 해제 중 예외: {e.Message}"); }

            try { _vp.Stop(); }
            catch (Exception e) { Debug.LogWarning($"[RSManager] OnDisable-> 비디오 정지 중 예외: {e.Message}"); }
        }
    }

    #endregion

    #region Debug Skip

    /// <summary> 디버그 스킵: 영상 정지 후 즉시 다음 씬으로 이동 </summary>
    protected override void OnDebugSkip()
    {
        try
        {
            if (_vp)
            {
                try { _vp.loopPointReached -= OnVideoEnded; } catch (Exception e) { Debug.LogWarning($"[RSManager] OnDebugSkip-> 이벤트 해제 중 예외: {e.Message}"); }
                try { if (_vp.isPlaying) _vp.Stop(); } catch (Exception e) { Debug.LogWarning($"[RSManager] OnDebugSkip-> 비디오 정지 중 예외: {e.Message}"); }
            }

            // OnVideoEnded와 동일 흐름으로 이동
            OnVideoEnded(_vp);
        }
        catch (Exception e)
        {
            Debug.LogError($"[RSManager] OnDebugSkip-> 예외: {e}");
        }
    }

    #endregion
}
