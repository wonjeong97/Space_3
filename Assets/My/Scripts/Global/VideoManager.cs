using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class VideoManager : MonoBehaviour
{
    public static VideoManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// StreamingAssets 상대 경로를 file:/// 절대 URL로 변환
    /// 예: "Video/Test/TestVideo.webm" -> "file:///D:/.../StreamingAssets/Video/Test/TestVideo.webm"
    /// </summary>
    private string BuildStreamingUrl(string relative)
    {
        string fullPath = Path.Combine(Application.streamingAssetsPath, relative).Replace("\\", "/");
        Uri uri = new Uri(fullPath);
        return uri.AbsoluteUri;
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
        if (!relativePathPossiblyWebm.EndsWith(".webm", StringComparison.OrdinalIgnoreCase))
            return BuildStreamingUrl(relativePathPossiblyWebm);

        if (IsWebmLikelySupported())
            return BuildStreamingUrl(relativePathPossiblyWebm);

        string mp4Relative = Path.ChangeExtension(relativePathPossiblyWebm, ".mp4");

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        string mp4Full = Path.Combine(Application.streamingAssetsPath, mp4Relative).Replace("\\", "/");
        if (File.Exists(mp4Full))
            return BuildStreamingUrl(mp4Relative);
#endif

        return BuildStreamingUrl(relativePathPossiblyWebm);
    }

    private void OnError(VideoPlayer src, string msg) => Debug.LogError($"[VideoPlayer] error: {msg}");

    private void OnPrepared(VideoPlayer src)
    {
    }

    /// <summary>
    /// VideoPlayer 준비 후 재생. 실패 시 false 반환. (타임아웃/취소 토큰/에러 이벤트 처리 포함)
    /// </summary>
    public async UniTask<bool> PrepareAndPlayAsync(
        VideoPlayer vp,
        string url,
        AudioSource audioSource,
        float volume,
        CancellationToken token,
        double timeoutSeconds = 3.0)
    {
        vp.errorReceived += OnError;
        vp.prepareCompleted += OnPrepared;

        try
        {
            // 1) 공통 세팅 함수
            void Configure(bool waitForFirstFrame, bool skipOnDrop)
            {
                vp.playOnAwake = false;
                vp.source = VideoSource.Url;
                vp.url = url;
                vp.waitForFirstFrame = waitForFirstFrame;
                vp.skipOnDrop = skipOnDrop;

                vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
                vp.EnableAudioTrack(0, true);
                vp.SetTargetAudioSource(0, audioSource);

                audioSource.playOnAwake = false;
                audioSource.loop = true;
                audioSource.volume = Mathf.Clamp01(volume);
            }

            async UniTask<bool> WaitPrepared(double to)
            {
                double start = Time.realtimeSinceStartupAsDouble;
                while (!vp.isPrepared)
                {
                    token.ThrowIfCancellationRequested();

                    // 일부 플랫폼/파일에서 isPrepared가 늦는 경우 보조 신호 허용
                    bool firstFrameArrived = (vp.frame > 0) || (vp.texture != null) || (vp.width > 0 && vp.height > 0);
                    if (firstFrameArrived) break;

                    if (Time.realtimeSinceStartupAsDouble - start > to)
                    {
                        Debug.LogError($"[VideoPlayer] prepare timeout: {url}");
                        return false;
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }

                return true;
            }

            async UniTask CleanupPlayer()
            {
                try
                {
                    vp.Stop();
                }
                catch
                {
                }

                try
                {
                    vp.frame = 0;
                }
                catch
                {
                }

                vp.clip = null;
                vp.url = null;
                await UniTask.Yield(PlayerLoopTiming.Update, token); // 한 프레임 비워주기
            }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            // 경로 존재 확인
            try
            {
                Uri u = new (url);
                string localPath = u.LocalPath;
                if (!File.Exists(localPath))
                    Debug.LogError($"[VideoManager] File not found: {localPath}");
            }
            catch
            {
            }
#endif

            // ── Attempt #1: 기본값
            Configure(waitForFirstFrame: true, skipOnDrop: true);
            vp.Prepare();
            if (await WaitPrepared(timeoutSeconds))
            {
                vp.Play();
                if (audioSource.volume > 0f) audioSource.Play();
                return true;
            }

            // ── Attempt #2: 클린 리셋 + waitForFirstFrame=false
            await CleanupPlayer();
            Configure(waitForFirstFrame: false, skipOnDrop: true);
            vp.Prepare();
            if (await WaitPrepared(timeoutSeconds))
            {
                vp.Play();
                if (audioSource.volume > 0f) audioSource.Play();
                return true;
            }

            // ── Attempt #3: 킥스타트 (Play→몇 프레임→Pause 후 준비 확인)
            await CleanupPlayer();
            Configure(waitForFirstFrame: false, skipOnDrop: false);
            // 일부 케이스에서 Prepare가 멈춰있는 듯 보일 때 Play가 파이프라인을 깨웁니다.
            vp.Prepare();
            // 짧게 기다렸다가 Play/Pause 킥
            double kickStart = Time.realtimeSinceStartupAsDouble;
            while (!vp.isPrepared && Time.realtimeSinceStartupAsDouble - kickStart < 0.5)
                await UniTask.Yield(PlayerLoopTiming.Update, token);

            if (!vp.isPrepared)
            {
                vp.Play();
                // 3~5 프레임 정도 대기
                for (int i = 0; i < 5 && !vp.isPrepared; i++)
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                vp.Pause();
            }

            if (await WaitPrepared(timeoutSeconds))
            {
                vp.Play();
                if (audioSource.volume > 0f) audioSource.Play();
                return true;
            }

            // 모두 실패
            return false;
        }
        finally
        {
            vp.errorReceived -= OnError;
            vp.prepareCompleted -= OnPrepared;
        }
    }

    /// <summary>
    /// RawImage + VideoPlayer 조합에서 버튼 크기에 맞는 RenderTexture를 생성/연결
    /// 반환값: 생성한 RenderTexture (필요시 해제 책임은 호출자 혹은 파괴 시점에서 처리)
    /// </summary>
    public RenderTexture WireRawImageAndRenderTexture(VideoPlayer vp, RawImage raw, Vector2Int size)
    {
        int rtW = Mathf.Max(2, size.x);
        int rtH = Mathf.Max(2, size.y);
        RenderTexture rTex = new RenderTexture(rtW, rtH, 0);
        rTex.Create();

        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.targetTexture = rTex;
        if (raw != null) raw.texture = rTex;

        return rTex;
    }

    /// <summary>
    /// 비디오를 처음부터 다시 재생 (준비 완료를 기다린 뒤 Play)
    /// </summary>
    public async UniTask RestartFromStartAsync(VideoPlayer vp, CancellationToken token, double timeoutSeconds = 10.0)
    {
        if (vp == null)
        {
            Debug.LogWarning("[VideoManager] VideoPlayer is null.");
            return;
        }

        bool hasSource = (vp.clip != null) || !string.IsNullOrEmpty(vp.url);
        if (!hasSource)
        {
            Debug.LogWarning("[VideoManager] No clip/url set on VideoPlayer.");
            return;
        }

        vp.Stop();
        vp.Prepare();

        double start = Time.realtimeSinceStartupAsDouble;
        while (!vp.isPrepared)
        {
            token.ThrowIfCancellationRequested();

            if (Time.realtimeSinceStartupAsDouble - start > timeoutSeconds)
            {
                Debug.LogError("[VideoPlayer] prepare timeout on RestartFromStartAsync");
                return;
            }

            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        vp.time = 0.0;

        try
        {
            vp.frame = 0;
        }
        catch
        {
            // 플랫폼/소스에 따라 불가한 경우가 있으므로 무시
        }

        vp.Play();

        // 필요 시, 실제 재생 시작을 한 프레임 기다리고 싶다면:
        // while (!vp.isPlaying) { await UniTask.Yield(PlayerLoopTiming.Update, token); }
        // await UniTask.WaitForEndOfFrame(cancellationToken: token);
    }
    
    public RenderTexture EnsureRenderTexture(VideoPlayer vp, RawImage raw, Vector2Int size, bool reuseIfSame)
    {
        int w = Mathf.Max(2, size.x);
        int h = Mathf.Max(2, size.y);

        RenderTexture existing = vp != null ? vp.targetTexture : null;
        bool canReuse = reuseIfSame && existing != null && existing.width == w && existing.height == h;

        if (canReuse)
        {
            // 화면에 마지막 프레임 유지
            vp.renderMode = VideoRenderMode.RenderTexture;
            return existing;
        }

        // 새로 생성 (화면의 texture는 지금 당장 바꾸지 말 것!)
        RenderTexture newRT = new RenderTexture(w, h, 0);
        newRT.Create();

        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.targetTexture = newRT;

        // raw.texture는 여기서 건드리지 않는다 → 첫 프레임 나올 때 스왑
        return newRT;
    }
}