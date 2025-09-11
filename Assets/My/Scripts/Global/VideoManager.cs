using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Video;

public class VideoManager : MonoBehaviour
{
    public static VideoManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(gameObject); }
    }

    /// <summary>
    /// StreamingAssets 상대 경로를 file:/// 절대 URL로 변환
    /// 예: "Video/Test/TestVideo.webm" -> "file:///D:/.../StreamingAssets/Video/Test/TestVideo.webm"
    /// </summary>
    private string BuildStreamingUrl(string relative)
    {
        var fullPath = Path.Combine(Application.streamingAssetsPath, relative).Replace("\\", "/");
        return new Uri(fullPath).AbsoluteUri;
    }

    /// <summary>
    /// 현재 런타임에서 webm 재생 가능성이 높은지 보수적으로 판단
    /// - Windows(Editor/Player): false (Media Foundation 경로에서 webm 문제 잦음)
    /// - Android, Linux: true (대체로 가능. 단, 디바이스/코덱에 따라 차이)
    /// - 그 외: 보수적으로 false
    /// </summary>
    private bool IsWebmLikelySupported()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        return false;
#elif UNITY_ANDROID || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
        return true;
#else
        return false;
#endif
    }

    /// <summary>
    /// 상대 경로(대개 .webm)를 받아, 런타임이 webm을 못 돌릴 것 같으면 .mp4로 대체 시도
    /// </summary>
    public string ResolvePlayableUrl(string relativePathPossiblyWebm)
    {
        // webm 아니면 그대로
        if (!relativePathPossiblyWebm.EndsWith(".webm", StringComparison.OrdinalIgnoreCase))
            return BuildStreamingUrl(relativePathPossiblyWebm);

        // webm 지원 OK → 그대로
        if (IsWebmLikelySupported())
            return BuildStreamingUrl(relativePathPossiblyWebm);

        // webm 지원 X → 같은 이름의 mp4가 있으면 그걸로 대체
        var mp4Relative = Path.ChangeExtension(relativePathPossiblyWebm, ".mp4");

        // 주의: Android에선 File.Exists로 StreamingAssets 확인이 불가.
        // 따라서 에디터/윈도우에서만 실제 파일 체크. (에디터/윈도우에서만 폴백이 필요하므로 충분)
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        var mp4Full = Path.Combine(Application.streamingAssetsPath, mp4Relative).Replace("\\", "/");
        if (File.Exists(mp4Full))
            return BuildStreamingUrl(mp4Relative);
#endif

        // 대체 실패 → 원본 webm URL 그대로(에러가 나더라도 로깅으로 확인)
        return BuildStreamingUrl(relativePathPossiblyWebm);
    }
    void OnError(VideoPlayer src, string msg) => Debug.LogError($"[VideoPlayer] error: {msg}");
    void OnPrepared(VideoPlayer src) { }

    /// <summary>
    /// VideoPlayer 준비 후 재생. 실패 시 false 반환. (타임아웃/취소 토큰/에러 이벤트 처리 포함)
    /// </summary>
    public async Task<bool> PrepareAndPlayAsync(
        VideoPlayer vp,
        string url,
        AudioSource audioSource,
        float volume,
        CancellationToken token,
        double timeoutSeconds = 10.0)
    {
        vp.errorReceived += OnError;
        vp.prepareCompleted += OnPrepared;

        try
        {
            vp.source = VideoSource.Url;
            vp.url = url;
            vp.isLooping = true;
            vp.playOnAwake = false;

            vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
            vp.EnableAudioTrack(0, true);
            vp.SetTargetAudioSource(0, audioSource);
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.volume = Mathf.Clamp01(volume);

            vp.Prepare();

            var start = Time.realtimeSinceStartupAsDouble;
            while (!vp.isPrepared)
            {
                if (token.IsCancellationRequested) return false;
                if (Time.realtimeSinceStartupAsDouble - start > timeoutSeconds)
                {
                    Debug.LogError("[VideoPlayer] prepare timeout");
                    return false;
                }
                await Task.Yield();
            }

            vp.Play();
            if (audioSource.volume > 0f) audioSource.Play();
            return true;
        }
        finally
        {
            // 이벤트 중복 등록 방지
            vp.errorReceived -= OnError;
            vp.prepareCompleted -= OnPrepared;
        }
    }

    /// <summary>
    /// RawImage + VideoPlayer 조합에서 버튼 크기에 맞는 RenderTexture를 생성/연결
    /// 반환값: 생성한 RenderTexture (필요시 해제 책임은 호출자 혹은 파괴 시점에서 처리)
    /// </summary>
    public RenderTexture WireRawImageAndRenderTexture(VideoPlayer vp, UnityEngine.UI.RawImage raw, Vector2Int size)
    {
        int rtW = Mathf.Max(2, size.x);
        int rtH = Mathf.Max(2, size.y);
        var rTex = new RenderTexture(rtW, rtH, 0);
        rTex.Create();

        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.targetTexture = rTex;
        if (raw) raw.texture = rTex;

        return rTex;
    }

    public IEnumerator RestartFromStart(VideoPlayer vp)
    {
        // 소스가 없으면 조기 종료
        if (!vp.clip && string.IsNullOrEmpty(vp.url))
        {
            Debug.LogWarning("[VideoPlayerManager] No clip/url set on VideoPlayer.");
            yield break;
        }
        vp.Stop();
        vp.Prepare();
        while (!vp.isPrepared)
            yield return null;

        vp.time = 0.0;

        try { vp.frame = 0; } catch { /* 플랫폼/소스에 따라 예외 무시 */ }

        vp.Play();

        // (선택) 첫 프레임이 실제로 표시될 때까지 잠깐 대기하고 싶다면:
        // while (!vp.isPlaying) yield return null;
        // yield return new WaitForEndOfFrame();
    }
}