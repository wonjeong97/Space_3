using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary> 프로젝트 전역 효과음 재생 매니저 </summary>
public sealed class SoundManager : MonoBehaviour
{
    public static SoundManager Instance;

    [Header("AudioSource")]
    [Tooltip("효과음 재생에 사용할 오디오 소스. 지정하지 않으면 자동 생성.")]
    [SerializeField] private AudioSource oneShotSource;

    private readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SoundSetting> _soundMap = new Dictionary<string, SoundSetting>(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource _loadCts;

    // ------------------------------------------------------------
    // 생명주기: 싱글턴 및 기본 구성
    // ------------------------------------------------------------
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(gameObject); return; }

        DontDestroyOnLoad(gameObject);

        if (oneShotSource == null)
        {
            oneShotSource = gameObject.AddComponent<AudioSource>();
            oneShotSource.playOnAwake = false;
            oneShotSource.loop = false;
            oneShotSource.spatialBlend = 0f; // 2D
        }

        // 사운드 맵 구성
        Settings s = JsonLoader.Instance?.settings;
        if (s != null && s.sounds != null)
        {
            foreach (SoundSetting setting in s.sounds)
            {
                if (string.IsNullOrEmpty(setting.key)) continue;
                _soundMap[setting.key] = setting;
            }
        }

        // 버튼 사운드도 미리 로드 시도
        _loadCts = new CancellationTokenSource();
        _ = PreloadButtonIfAnyAsync(_loadCts.Token);
    }

    private void OnDestroy()
    {
        if (_loadCts != null)
        {
            try { _loadCts.Cancel(); } catch { }
            _loadCts.Dispose();
            _loadCts = null;
        }
    }

    // ------------------------------------------------------------
    // 퍼블릭 API
    // ------------------------------------------------------------

    /// <summary>
    /// Settings.buttonSound에 설정된 버튼 효과음을 1회 재생
    /// </summary>
    public async UniTaskVoid PlayButton()
    {
        // 어떤 스레드에서 호출돼도 메인 스레드로 이동
        await UniTask.SwitchToMainThread();

        Settings s = JsonLoader.Instance?.settings;
        if (s == null || s.buttonSound == null)
        {
            Debug.LogWarning("[SoundManager] Settings.buttonSound 미설정");
            return;
        }

        string relPath = s.buttonSound.clipPath;
        float volume = Mathf.Clamp01(s.buttonSound.volume <= 0f ? 1f : s.buttonSound.volume);

        AudioClip clip = await GetOrLoadClipAsync(relPath, this.GetCancellationTokenOnDestroy());
        if (clip != null) oneShotSource.PlayOneShot(clip, volume);
    }

    /// <summary>
    /// settings.sounds 배열에 등록된 key로 재생 (등록된 volume 사용)
    /// </summary>
    public async UniTaskVoid PlayByKey(string key)
    {
        await UniTask.SwitchToMainThread();

        if (string.IsNullOrEmpty(key)) return;
        if (!_soundMap.TryGetValue(key, out SoundSetting ss))
        {
            Debug.LogWarning($"[SoundManager] 미등록 키: {key}");
            return;
        }

        float volume = Mathf.Clamp01(ss.volume <= 0f ? 1f : ss.volume);
        AudioClip clip = await GetOrLoadClipAsync(ss.clipPath, this.GetCancellationTokenOnDestroy());
        if (clip != null) oneShotSource.PlayOneShot(clip, volume);
    }

    /// <summary>
    /// 지정 파일 상대경로(StreamingAssets 기준)로 재생. 볼륨은 0..1
    /// </summary>
    public async UniTaskVoid PlayByPath(string relativePath, float volume = 1f)
    {
        await UniTask.SwitchToMainThread();

        if (string.IsNullOrEmpty(relativePath)) return;

        float vol = Mathf.Clamp01(volume <= 0f ? 1f : volume);
        AudioClip clip = await GetOrLoadClipAsync(relativePath, this.GetCancellationTokenOnDestroy());
        if (clip != null) oneShotSource.PlayOneShot(clip, vol);
    }

    // ------------------------------------------------------------
    // 내부: 프리로드/로드
    // ------------------------------------------------------------

    /// <summary>
    /// 버튼 사운드가 존재하면 선행 로드
    /// </summary>
    private async UniTask PreloadButtonIfAnyAsync(CancellationToken token)
    {
        Settings s = JsonLoader.Instance?.settings;
        if (s == null || s.buttonSound == null) return;

        string relPath = s.buttonSound.clipPath;
        if (string.IsNullOrEmpty(relPath)) return;

        // 이미 캐시에 있으면 스킵
        if (_clipCache.ContainsKey(relPath)) return;

        AudioClip clip = await LoadClipAsync(relPath, token);
        if (clip != null && !_clipCache.ContainsKey(relPath))
        {
            _clipCache.Add(relPath, clip);
        }
    }

    /// <summary> 캐시된 클립을 우선 반환, 없으면 로드하여 캐시에 저장 </summary>
    private async UniTask<AudioClip> GetOrLoadClipAsync(string relativePath, CancellationToken token)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;

        if (_clipCache.TryGetValue(relativePath, out AudioClip cached))
            return cached;

        // 메인 스레드에서 로드 강제
        await UniTask.SwitchToMainThread();
        AudioClip clip = await LoadClipAsync(relativePath, token);

        if (clip != null && !_clipCache.ContainsKey(relativePath))
            _clipCache.Add(relativePath, clip);

        return clip;
    }

    /// <summary> StreamingAssets 기준 상대 경로로 오디오 파일(mp3/wav/ogg) 로드 </summary>
    private async UniTask<AudioClip> LoadClipAsync(string relativePath, CancellationToken token)
    {
        try
        {
            // 안전하게 메인 스레드에서 실행
            await UniTask.SwitchToMainThread();

            string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[SoundManager] 파일 없음: {fullPath}");
                return null;
            }

            string uri = new Uri(fullPath).AbsoluteUri;
            AudioType audioType = GuessAudioTypeByExtension(fullPath);

            using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
            {
                await req.SendWebRequest().ToUniTask(cancellationToken: token);

#if UNITY_2020_2_OR_NEWER
                if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
                {
                    Debug.LogError($"[SoundManager] 로드 실패: {fullPath} -> {req.error}");
                    return null;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null)
                {
                    Debug.LogError($"[SoundManager] Decode 실패: {fullPath}");
                }
                return clip;
            }
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception e) { Debug.LogError(e); return null; }
    }

    /// <summary> 파일 확장자에 따라 AudioType 추정 </summary>
    private static AudioType GuessAudioTypeByExtension(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".mp3") return AudioType.MPEG;
        if (ext == ".wav") return AudioType.WAV;
        if (ext == ".ogg") return AudioType.OGGVORBIS;
        return AudioType.UNKNOWN;
    }
}
