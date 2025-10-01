using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// - StreamingAssets/Audio 폴더에서 JSON 설정에 따라 오디오 파일 로드
/// - key 기반 PlayOneShot 지원
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    private readonly Dictionary<string, AudioClip> _soundMap = new Dictionary<string, AudioClip>();
    private readonly Dictionary<string, float> _soundVolumeMap = new Dictionary<string, float>();
    private AudioSource _sfxSource;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            //DontDestroyOnLoad(gameObject);

            _sfxSource = gameObject.GetComponent<AudioSource>();
            if (_sfxSource == null) _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;

            // UniTask fire-and-forget
            LoadSoundsFromSettingsAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    /// <summary> JSON Settings에서 사운드 목록을 읽어와 로드 (UniTask) </summary>
    private async UniTaskVoid LoadSoundsFromSettingsAsync(CancellationToken token)
    {
        _soundMap.Clear();
        _soundVolumeMap.Clear();

        Settings settings = JsonLoader.Instance.settings;
        if (settings?.sounds == null) return;

        foreach (SoundSetting entry in settings.sounds)
        {
            token.ThrowIfCancellationRequested();

            string fullPath = Path.Combine(Application.streamingAssetsPath, "Audio", entry.clipPath).Replace("\\", "/");
            string url = "file://" + fullPath;
            string ext = Path.GetExtension(fullPath).ToLower();

            AudioType type = AudioType.WAV;
            if (ext == ".ogg") type = AudioType.OGGVORBIS;
            else if (ext == ".mp3") type = AudioType.MPEG;

            using (var www = UnityWebRequestMultimedia.GetAudioClip(url, type))
            {
                var op = www.SendWebRequest();
                await op.ToUniTask(cancellationToken: token); // ← UniTask로 대체

                if (www.result == UnityWebRequest.Result.Success)
                {
                    var clip = DownloadHandlerAudioClip.GetContent(www);
                    clip.name = entry.key;
                    _soundMap[entry.key] = clip;
                    _soundVolumeMap[entry.key] = entry.volume;
                }
                else
                {
                    Debug.LogWarning($"[AudioManager] Load failed: {entry.clipPath} - {www.error}");
                }
            }
        }
    }

    /// <summary> 키 기반 사운드 재생 API </summary>
    public bool Play(string key, float? volumeOverride = null)
    {
        if (!_sfxSource) return false;
        if (!_soundMap.TryGetValue(key, out AudioClip clip) || clip == null)
        {
            Debug.LogWarning($"[AudioManager] Key not found: {key}");
            return false;
        }

        float vol = volumeOverride ?? (_soundVolumeMap.GetValueOrDefault(key, 1f));
        _sfxSource.PlayOneShot(clip, Mathf.Clamp01(vol));
        return true;
    }

    /// <summary> 현재 재생 중인 모든 사운드 정지 </summary>
    public void StopAll()
    {
        if (_sfxSource && _sfxSource.isPlaying) _sfxSource.Stop();
    }
}
