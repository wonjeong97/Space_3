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
    [SerializeField] private AudioSource sfxSource; // 로켓/배경용
    [SerializeField] private AudioSource crossSource; // 크로스페이드용 내부 소스
    [SerializeField] private AudioSource bgmSource; // BGM 전용

    private readonly Dictionary<string, AudioClip> _clipCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SoundSetting> _soundMap = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource _loadCts;
    private CancellationToken _destroyToken;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _destroyToken = this.GetCancellationTokenOnDestroy();
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.spatialBlend = 0f;
        }

        if (bgmSource == null)
        {
            bgmSource = gameObject.AddComponent<AudioSource>();
            bgmSource.playOnAwake = false;
            bgmSource.loop = true; // 기본적으로 루프
            bgmSource.spatialBlend = 0f;
        }

        crossSource = gameObject.AddComponent<AudioSource>();
        crossSource.playOnAwake = false;
        crossSource.loop = false;
        crossSource.spatialBlend = 0f;
        crossSource.volume = 0f;
    }

    private void Start()
    {
        // 설정 파일에서 사운드 매핑 구성
        Settings s = JsonLoader.Instance?.settings;
        if (s?.sounds != null)
        {
            foreach (SoundSetting setting in s.sounds)
            {
                if (!string.IsNullOrEmpty(setting.key))
                {
                    _soundMap[setting.key] = setting;
                }
            }
        }

        _loadCts = new CancellationTokenSource();
        PreloadButtonIfAnyAsync(_loadCts.Token).Forget();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (_loadCts != null)
        {
            try
            {
                _loadCts.Cancel();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SoundManager] OnDestroy-> _loadCts 취소 중 예외: {e.Message}");
            }

            _loadCts.Dispose();
            _loadCts = null;
        }
    }

    // ==============================================================
    // 퍼블릭 API
    // ==============================================================

    /// <summary> 사운드 키로 재생 (Settings.sounds 기준) </summary>
    public async UniTaskVoid PlayByKey(string key, bool loop = false)
    {
        await UniTask.SwitchToMainThread();

        if (this == null || _destroyToken.IsCancellationRequested)
        {
            return;
        }

        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if (!_soundMap.TryGetValue(key, out SoundSetting ss))
        {
            Debug.LogWarning($"[SoundManager] PlayByKey-> 미등록 키: {key}");
            return;
        }

        AudioClip clip;
        try
        {
            clip = await GetOrLoadClipAsync(ss.clipPath, _destroyToken);
        }
        catch (OperationCanceledException)
        {
            // 씬 전환/파괴로 인한 취소
            return;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SoundManager] PlayByKey-> 클립 로드 중 예외: {e}");
            return;
        }

        if (clip == null)
        {
            return;
        }

        if (this == null || _destroyToken.IsCancellationRequested)
        {
            return;
        }

        if (sfxSource == null)
        {
            Debug.LogError("[SoundManager] PlayByKey-> rocketSource가 할당되지 않음");
            return;
        }

        sfxSource.loop = loop;
        sfxSource.clip = clip;
        sfxSource.volume = Mathf.Clamp01(ss.volume <= 0f ? 1f : ss.volume);
        sfxSource.Play();
    }

    /// <summary> 경로로 직접 재생 </summary>
    public async UniTaskVoid PlayByPath(string relativePath, float volume = 1f, bool loop = false)
    {
        await UniTask.SwitchToMainThread();

        if (this == null || _destroyToken.IsCancellationRequested)
        {
            return;
        }

        if (string.IsNullOrEmpty(relativePath))
        {
            return;
        }

        AudioClip clip;
        try
        {
            clip = await GetOrLoadClipAsync(relativePath, _destroyToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SoundManager] PlayByPath-> 클립 로드 중 예외: {e}");
            return;
        }

        if (clip == null)
        {
            return;
        }

        if (this == null || _destroyToken.IsCancellationRequested)
        {
            return;
        }

        if (sfxSource == null)
        {
            Debug.LogError("[SoundManager] PlayByPath-> rocketSource가 할당되지 않음");
            return;
        }

        sfxSource.loop = loop;
        sfxSource.clip = clip;
        sfxSource.volume = Mathf.Clamp01(volume);
        sfxSource.Play();
    }

    /// <summary> 사운드 키로 지정 음원 크로스페이드 </summary>
    public async UniTaskVoid CrossFadeByKey(string key, float duration = 1f, float targetVolume = 1f, bool loop = true)
    {
        await UniTask.SwitchToMainThread();

        if (this == null || _destroyToken.IsCancellationRequested)
        {
            return;
        }

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

        AudioClip newClip;
        try
        {
            newClip = await GetOrLoadClipAsync(ss.clipPath, _destroyToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SoundManager] CrossFadeByKey-> 클립 로드 중 예외: {e}");
            return;
        }

        if (newClip == null)
        {
            Debug.LogError($"[SoundManager] CrossFadeByKey-> 로드 실패: {ss.clipPath}");
            return;
        }

        if (this == null || _destroyToken.IsCancellationRequested)
        {
            return;
        }

        float fadeTime = Mathf.Max(0.1f, duration);
        float t = 0f;

        crossSource.clip = newClip;
        crossSource.volume = 0f;
        crossSource.loop = loop;
        crossSource.Play();

        float originalVol = sfxSource != null && sfxSource.isPlaying ? sfxSource.volume : 0f;
        float targetVol = Mathf.Clamp01(targetVolume);

        Debug.Log($"[SoundManager] CrossFadeByKey-> {key} ({ss.clipPath}) 로 {fadeTime:F2}초 동안 전환");

        while (t < fadeTime && !_destroyToken.IsCancellationRequested && this != null)
        {
            t += Time.deltaTime;
            float progress = t / fadeTime;

            if (sfxSource != null && sfxSource.isPlaying)
            {
                sfxSource.volume = Mathf.Lerp(originalVol, 0f, progress);
            }

            crossSource.volume = Mathf.Lerp(0f, targetVol, progress);
            await UniTask.Yield();
        }

        if (sfxSource != null)
        {
            if (sfxSource.isPlaying)
            {
                sfxSource.Stop();
            }

            sfxSource.clip = newClip;
            sfxSource.volume = targetVol;
            sfxSource.loop = loop;
            sfxSource.Play();
        }

        crossSource.Stop();
        crossSource.clip = null;
        crossSource.volume = 0f;
    }

    /// <summary> 사운드 키로 BGM 재생 (Settings.sounds 기준) - bgmSource를 사용하므로 씬 전환과 무관하게 유지됨 </summary>
    public async UniTaskVoid PlayBGMByKey(string key, bool loop = true, float volumeScale = 1f)
    {
        await UniTask.SwitchToMainThread();

        if (this == null || _destroyToken.IsCancellationRequested)
        {
            return;
        }

        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if (bgmSource != null && bgmSource.isPlaying)
        {
            // 이미 BGM이 재생 중이면 무시
            return;
        }

        if (!_soundMap.TryGetValue(key, out SoundSetting ss))
        {
            Debug.LogWarning($"[SoundManager] PlayBGMByKey-> 미등록 키: {key}");
            return;
        }

        AudioClip clip;
        try
        {
            clip = await GetOrLoadClipAsync(ss.clipPath, _destroyToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SoundManager] PlayBGMByKey-> 클립 로드 중 예외: {e}");
            return;
        }

        if (clip == null)
        {
            return;
        }

        if (this == null || _destroyToken.IsCancellationRequested)
        {
            return;
        }

        if (bgmSource == null)
        {
            Debug.LogError("[SoundManager] PlayBGMByKey-> bgmSource가 할당되지 않음");
            return;
        }

        float baseVolume = ss.volume <= 0f ? 1f : ss.volume;
        float finalVolume = Mathf.Clamp01(baseVolume * Mathf.Max(0f, volumeScale));

        bgmSource.loop = loop;
        bgmSource.clip = clip;
        bgmSource.volume = finalVolume;
        bgmSource.Play();
    }

    /// <summary> 현재 재생 중인 BGM 정지 </summary>
    public void StopBGM()
    {
        if (bgmSource == null)
        {
            return;
        }

        if (bgmSource.isPlaying)
        {
            bgmSource.Stop();
        }

        bgmSource.clip = null;
    }

    /// <summary> 현재 재생 중인 BGM 일시정지 </summary>
    public void PauseBGM()
    {
        if (bgmSource == null)
        {
            return;
        }

        if (bgmSource.isPlaying)
        {
            bgmSource.Pause();
        }
    }

    /// <summary> 일시정지된 BGM 다시 재생 </summary>
    public void ResumeBGM()
    {
        if (bgmSource == null)
        {
            return;
        }

        if (bgmSource.clip != null && !bgmSource.isPlaying)
        {
            bgmSource.UnPause();
        }
    }

    /// <summary> 사운드 키로 BGM 크로스페이드 (bgmSource 기준) </summary>
    public async UniTaskVoid CrossFadeBGMByKey(string key, float duration = 1f, float volumeScale = 1f, bool loop = true)
    {
        await UniTask.SwitchToMainThread();

        if (this == null || _destroyToken.IsCancellationRequested)
        {
            return;
        }

        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if (!_soundMap.TryGetValue(key, out SoundSetting ss))
        {
            Debug.LogWarning($"[SoundManager] CrossFadeBGMByKey-> 미등록 키: {key}");
            return;
        }

        AudioClip newClip;
        try
        {
            newClip = await GetOrLoadClipAsync(ss.clipPath, _destroyToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SoundManager] CrossFadeBGMByKey-> 클립 로드 중 예외: {e}");
            return;
        }

        if (newClip == null)
        {
            return;
        }

        if (this == null || _destroyToken.IsCancellationRequested)
        {
            return;
        }

        if (bgmSource == null)
        {
            Debug.LogError("[SoundManager] CrossFadeBGMByKey-> bgmSource가 할당되지 않음");
            return;
        }

        if (crossSource == null)
        {
            Debug.LogError("[SoundManager] CrossFadeBGMByKey-> crossSource가 할당되지 않음");
            return;
        }

        float baseVolume = ss.volume <= 0f ? 1f : ss.volume;
        float targetVol = Mathf.Clamp01(baseVolume * Mathf.Max(0f, volumeScale));

        float fadeTime = Mathf.Max(0.1f, duration);
        float t = 0f;

        // 새 BGM은 crossSource에서 서서히 페이드인
        crossSource.clip = newClip;
        crossSource.volume = 0f;
        crossSource.loop = loop;
        crossSource.Play();

        float originalVol = bgmSource.isPlaying ? bgmSource.volume : 0f;

        while (t < fadeTime && !_destroyToken.IsCancellationRequested && this != null)
        {
            t += Time.deltaTime;
            float progress = t / fadeTime;

            if (bgmSource.isPlaying)
            {
                bgmSource.volume = Mathf.Lerp(originalVol, 0f, progress);
            }

            crossSource.volume = Mathf.Lerp(0f, targetVol, progress);

            await UniTask.Yield();
        }

        if (bgmSource != null)
        {
            if (bgmSource.isPlaying)
            {
                bgmSource.Stop();
            }

            bgmSource.clip = newClip;
            bgmSource.volume = targetVol;
            bgmSource.loop = loop;
            bgmSource.Play();
        }

        crossSource.Stop();
        crossSource.clip = null;
        crossSource.volume = 0f;
    }


    // ==============================================================
    // 내부 유틸
    // ==============================================================

    private async UniTask<AudioClip> GetOrLoadClipAsync(string relativePath, CancellationToken token)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return null;
        }

        if (_clipCache.TryGetValue(relativePath, out AudioClip cached))
        {
            return cached;
        }

        await UniTask.SwitchToMainThread();

        string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);
        if (!File.Exists(fullPath))
        {
            Debug.LogError($"[SoundManager] 파일 없음: {fullPath}");
            return null;
        }

        using UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(new Uri(fullPath).AbsoluteUri, GuessAudioTypeByExtension(fullPath));

        await req.SendWebRequest().ToUniTask(cancellationToken: token);

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[SoundManager] 로드 실패: {fullPath} -> {req.error}");
            return null;
        }

        AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
        if (clip != null)
        {
            _clipCache[relativePath] = clip;
        }

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
        if (s?.buttonSound == null)
        {
            return;
        }

        string relPath = s.buttonSound.clipPath;
        if (string.IsNullOrEmpty(relPath) || _clipCache.ContainsKey(relPath))
        {
            return;
        }

        AudioClip clip;
        try
        {
            clip = await GetOrLoadClipAsync(relPath, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SoundManager] PreloadButtonIfAnyAsync-> 예외: {e}");
            return;
        }

        if (clip != null && !_clipCache.ContainsKey(relPath))
        {
            _clipCache.Add(relPath, clip);
        }
    }
}