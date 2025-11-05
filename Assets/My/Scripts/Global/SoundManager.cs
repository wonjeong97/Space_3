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

    [Header("Audio Sources")]
    [SerializeField] private AudioSource buttonSource; // 버튼용
    [SerializeField] private AudioSource rocketSource; // 로켓/배경용
    [SerializeField] private AudioSource _crossSource; // 크로스페이드용 내부 소스

    private readonly Dictionary<string, AudioClip> _clipCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SoundSetting> _soundMap = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource _loadCts;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(gameObject); return; }

        DontDestroyOnLoad(gameObject);

        // AudioSource 기본 세팅
        if (buttonSource == null)
        {
            buttonSource = gameObject.AddComponent<AudioSource>();
            buttonSource.playOnAwake = false;
            buttonSource.loop = false;
            buttonSource.spatialBlend = 0f;
        }
        if (rocketSource == null)
        {
            rocketSource = gameObject.AddComponent<AudioSource>();
            rocketSource.playOnAwake = false;
            rocketSource.loop = false;
            rocketSource.spatialBlend = 0f;
        }
        _crossSource = gameObject.AddComponent<AudioSource>();
        _crossSource.playOnAwake = false;
        _crossSource.loop = false;
        _crossSource.spatialBlend = 0f;
        _crossSource.volume = 0f;

        // 설정 파일에서 사운드 매핑 구성
        Settings s = JsonLoader.Instance?.settings;
        if (s?.sounds != null)
        {
            foreach (SoundSetting setting in s.sounds)
            {
                if (!string.IsNullOrEmpty(setting.key))
                    _soundMap[setting.key] = setting;
            }
        }

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

    // ==============================================================
    // 퍼블릭 API
    // ==============================================================

    /// <summary> 버튼 효과음 재생 </summary>
    public async UniTaskVoid PlayButton(bool loop = false)
    {
        await UniTask.SwitchToMainThread();

        Settings s = JsonLoader.Instance?.settings;
        if (s?.buttonSound == null)
        {
            Debug.LogWarning("[SoundManager] PlayButton-> Settings.buttonSound 미설정");
            return;
        }

        string relPath = s.buttonSound.clipPath;
        float volume = Mathf.Clamp01(s.buttonSound.volume <= 0f ? 1f : s.buttonSound.volume);

        AudioClip clip = await GetOrLoadClipAsync(relPath, this.GetCancellationTokenOnDestroy());
        if (clip == null) return;

        buttonSource.loop = loop;
        buttonSource.clip = clip;
        buttonSource.volume = volume;
        buttonSource.Play();
    }

    /// <summary> 사운드 키로 재생 (Settings.sounds 기준) </summary>
    public async UniTaskVoid PlayByKey(string key, bool loop = false)
    {
        await UniTask.SwitchToMainThread();

        if (string.IsNullOrEmpty(key)) return;
        if (!_soundMap.TryGetValue(key, out SoundSetting ss))
        {
            Debug.LogWarning($"[SoundManager] PlayByKey-> 미등록 키: {key}");
            return;
        }

        AudioClip clip = await GetOrLoadClipAsync(ss.clipPath, this.GetCancellationTokenOnDestroy());
        if (clip == null) return;

        rocketSource.loop = loop;
        rocketSource.clip = clip;
        rocketSource.volume = Mathf.Clamp01(ss.volume <= 0f ? 1f : ss.volume);
        rocketSource.Play();
    }

    /// <summary> 경로로 직접 재생 </summary>
    public async UniTaskVoid PlayByPath(string relativePath, float volume = 1f, bool loop = false)
    {
        await UniTask.SwitchToMainThread();
        if (string.IsNullOrEmpty(relativePath)) return;

        AudioClip clip = await GetOrLoadClipAsync(relativePath, this.GetCancellationTokenOnDestroy());
        if (clip == null) return;

        rocketSource.loop = loop;
        rocketSource.clip = clip;
        rocketSource.volume = Mathf.Clamp01(volume);
        rocketSource.Play();
    }

    /// <summary> 사운드 키로 지정 음원 크로스페이드 </summary>
    public async UniTaskVoid CrossFadeByKey(string key, float duration = 1f, float targetVolume = 1f, bool loop = false)
    {
        await UniTask.SwitchToMainThread();

        if (string.IsNullOrEmpty(key))
        {
            Debug.LogWarning("[SoundManager] CrossFadeByKey-> key 비어 있음");
            return;
        }

        if (!_soundMap.TryGetValue(key, out SoundSetting ss))
        {
            Debug.LogWarning($"[SoundManager] CrossFadeByKey-> '{key}' 미등록");
            return;
        }

        AudioClip newClip = await GetOrLoadClipAsync(ss.clipPath, this.GetCancellationTokenOnDestroy());
        if (newClip == null)
        {
            Debug.LogError($"[SoundManager] CrossFadeByKey-> 로드 실패: {ss.clipPath}");
            return;
        }

        float fadeTime = Mathf.Max(0.1f, duration);
        float t = 0f;

        _crossSource.clip = newClip;
        _crossSource.volume = 0f;
        _crossSource.loop = loop;
        _crossSource.Play();

        float originalVol = rocketSource.isPlaying ? rocketSource.volume : 0f;
        float targetVol = Mathf.Clamp01(targetVolume);

        Debug.Log($"[SoundManager] CrossFadeByKey-> {key} ({ss.clipPath}) 로 {fadeTime:F2}초 동안 전환");

        while (t < fadeTime)
        {
            t += Time.deltaTime;
            float progress = t / fadeTime;

            if (rocketSource.isPlaying)
                rocketSource.volume = Mathf.Lerp(originalVol, 0f, progress);

            _crossSource.volume = Mathf.Lerp(0f, targetVol, progress);
            await UniTask.Yield();
        }

        if (rocketSource.isPlaying) rocketSource.Stop();

        rocketSource.clip = newClip;
        rocketSource.volume = targetVol;
        rocketSource.loop = loop;
        rocketSource.Play();

        _crossSource.Stop();
        _crossSource.clip = null;
        _crossSource.volume = 0f;
    }

    // ==============================================================
    // 내부 유틸
    // ==============================================================

    private async UniTask<AudioClip> GetOrLoadClipAsync(string relativePath, CancellationToken token)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;

        if (_clipCache.TryGetValue(relativePath, out AudioClip cached))
            return cached;

        await UniTask.SwitchToMainThread();
        string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);
        if (!File.Exists(fullPath))
        {
            Debug.LogError($"[SoundManager] 파일 없음: {fullPath}");
            return null;
        }

        using UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(new Uri(fullPath).AbsoluteUri, GuessAudioTypeByExtension(fullPath));
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
        if (clip != null) _clipCache[relativePath] = clip;
        return clip;
    }

    private static AudioType GuessAudioTypeByExtension(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".mp3") return AudioType.MPEG;
        if (ext == ".wav") return AudioType.WAV;
        if (ext == ".ogg") return AudioType.OGGVORBIS;
        return AudioType.UNKNOWN;
    }

    private async UniTask PreloadButtonIfAnyAsync(CancellationToken token)
    {
        Settings s = JsonLoader.Instance?.settings;
        if (s?.buttonSound == null) return;

        string relPath = s.buttonSound.clipPath;
        if (string.IsNullOrEmpty(relPath) || _clipCache.ContainsKey(relPath)) return;

        AudioClip clip = await GetOrLoadClipAsync(relPath, token);
        if (clip != null && !_clipCache.ContainsKey(relPath))
            _clipCache.Add(relPath, clip);
    }
}
