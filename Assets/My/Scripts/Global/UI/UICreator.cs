using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;
using UnityEngine.Video;

public class UICreator : MonoBehaviour
{
    public static UICreator Instance { get; private set; }

    private readonly List<GameObject> _instances = new List<GameObject>();
    private readonly Dictionary<string, AsyncOperationHandle> _assetCache = new Dictionary<string, AsyncOperationHandle>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        DestroyAllTrackedInstances();
        ReleaseAllCachedAssets();
    }

    // ------------------------------------------------------------------
    // Addressables helpers
    // ------------------------------------------------------------------

    /// <summary>Addressables로 프리팹 비동기 인스턴스화</summary>
    private async UniTask<GameObject> InstantiateAsync(string key, Transform parent, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(key, parent);
        try
        {
            GameObject go = await UIUtility.AwaitWithCancellation(handle, token);
            if (go != null) _instances.Add(go);
            return go;
        }
        catch (OperationCanceledException)
        {
            if (handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
                Addressables.ReleaseInstance(handle.Result);
            throw;
        }
        catch (Exception)
        {
            if (handle.IsValid() && handle.Result != null)
                Addressables.ReleaseInstance(handle.Result);
            return null;
        }
    }

    /// <summary>Addressables 에셋 로드를 캐시해 중복 로드 방지</summary>
    private async UniTask<T> LoadAssetWithCacheAsync<T>(string key, CancellationToken token) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(key)) return null;

        if (_assetCache.TryGetValue(key, out AsyncOperationHandle existing))
        {
            return existing.IsValid() ? (T)existing.Result : null;
        }

        token.ThrowIfCancellationRequested();

        AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(key);
        T asset = await UIUtility.AwaitWithCancellation(handle, token);

        _assetCache[key] = handle;
        return asset;
    }

    /// <summary>Addressables 에셋 캐시 전부 해제하고 비우기</summary>
    private void ReleaseAllCachedAssets()
    {
        foreach (KeyValuePair<string, AsyncOperationHandle> kv in _assetCache)
        {
            if (kv.Value.IsValid()) Addressables.Release(kv.Value);
        }
        _assetCache.Clear();
    }

    /// <summary>추적 중인 Addressables 인스턴스 전부 해제</summary>
    public void DestroyAllTrackedInstances()
    {
        for (int i = _instances.Count - 1; i >= 0; --i)
        {
            GameObject go = _instances[i];
            if (go != null) Addressables.ReleaseInstance(go);
        }
        _instances.Clear();
    }

    /// <summary>추적 중인 특정 인스턴스 해제 시도 후 성공 여부 반환</summary>
    public bool DestroyTrackedInstance(GameObject go)
    {
        if (go == null) return false;

        int idx = _instances.IndexOf(go);
        if (idx >= 0)
        {
            Addressables.ReleaseInstance(go);
            _instances.RemoveAt(idx);
            return true;
        }

        return false;
    }

    // ------------------------------------------------------------------
    // Font / Material helpers
    // ------------------------------------------------------------------

    /// <summary>폰트 키를 FontMap 기준으로 해석해 매핑된 키 반환</summary>
    private static string ResolveFontKey(string key)
    {
        Settings settings = JsonLoader.Instance != null ? JsonLoader.Instance.settings : null;
        FontMaps fontMap = settings?.fontMap;
        if (fontMap == null || string.IsNullOrEmpty(key)) return key;

        FieldInfo field = typeof(FontMaps).GetField(key);
        if (field != null)
        {
            string mapped = field.GetValue(fontMap) as string;
            return string.IsNullOrEmpty(mapped) ? key : mapped;
        }
        return key;
    }

    /// <summary>폰트 키 매핑과 에셋 로드를 거쳐 TMP 텍스트 속성 적용</summary>
    public async UniTask ApplyFontAsync(
        TextMeshProUGUI uiText,
        string fontKey,
        string textValue,
        float fontSize,
        Color fontColor,
        TextAlignmentOptions alignment,
        CancellationToken token)
    {
        if (!uiText || string.IsNullOrEmpty(fontKey)) return;

        string mapped = ResolveFontKey(fontKey);
        TMP_FontAsset font = await LoadAssetWithCacheAsync<TMP_FontAsset>(mapped, token);
        if (font == null) return;

        token.ThrowIfCancellationRequested();

        uiText.font = font;
        uiText.fontSize = fontSize;
        uiText.color = fontColor;
        uiText.alignment = alignment;
        uiText.text = textValue;
    }

    /// <summary>타깃 이미지에 Addressable로 로드한 머티리얼을 적용함 (콜백 방식 유지)</summary>
    public void LoadMaterialAndApply(Image targetImage, string materialKey)
    {
        if (targetImage == null || string.IsNullOrEmpty(materialKey)) return;
        Addressables.LoadAssetAsync<Material>(materialKey).Completed += handle =>
        {
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                targetImage.material = handle.Result;
            }
            else
            {
                Debug.LogWarning($"[UIManager] Material load failed: {materialKey}");
            }
        };
    }

    // ------------------------------------------------------------------
    // Public creation APIs
    // ------------------------------------------------------------------

    /// <summary>캔버스 프리팹을 Addressables로 비동기 생성해 반환</summary>
    public async UniTask<GameObject> CreateCanvasAsync(CancellationToken token = default)
    {
        return await InstantiateAsync("Prefabs/CanvasPrefab.prefab", null, token);
    }

    /// <summary>배경 이미지를 생성하고 RectTransform 기본값 설정 후 반환</summary>
    public async UniTask<GameObject> CreateBackgroundImageAsync(ImageSetting setting, GameObject parent, CancellationToken token)
    {
        GameObject go = await CreateSingleImageAsync(setting, parent, token);
        if (go != null && go.TryGetComponent<RectTransform>(out RectTransform rt))
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.Euler(setting.rotation);
            rt.sizeDelta = setting.size;
        }
        return go;
    }

    /// <summary>여러 Text 항목을 비동기로 생성하고 모두 완료될 때까지 대기</summary>
    private async UniTask CreateTextsAsync(TextSetting[] settings, GameObject parent, CancellationToken token)
    {
        if (settings == null || settings.Length == 0) return;

        List<UniTask> tasks = new List<UniTask>(settings.Length);
        for (int i = 0; i < settings.Length; i++)
        {
            TextSetting s = settings[i];
            tasks.Add(CreateSingleTextAsync(s, parent, token).AsUniTask());
        }

        await UniTask.WhenAll(tasks);
    }

    /// <summary>단일 Text 프리팹 생성 후 TMP 속성과 RectTransform 적용</summary>
    public async UniTask<GameObject> CreateSingleTextAsync(TextSetting setting, GameObject parent, CancellationToken token)
    {
        GameObject go = await InstantiateAsync("Prefabs/TextPrefab.prefab", parent.transform, token);
        if (go == null) return null;
        go.name = setting.name;

        if (go.TryGetComponent<TextMeshProUGUI>(out TextMeshProUGUI uiText))
        {
            await ApplyFontAsync(
                uiText,
                setting.fontName,
                setting.text,
                setting.fontSize,
                setting.fontColor,
                setting.alignment,
                token
            );
        }

        if (go.TryGetComponent<RectTransform>(out RectTransform rt))
        {
            UIUtility.ApplyRect(
                rt,
                size: null,
                anchoredPos: new Vector2(setting.position.x, -setting.position.y),
                rotation: setting.rotation
            );
        }

        return go;
    }

    /// <summary>여러 Image 항목을 비동기로 생성하고 모두 완료될 때까지 대기</summary>
    private async UniTask CreateImagesAsync(ImageSetting[] images, GameObject parent, CancellationToken token)
    {
        if (images == null || images.Length == 0) return;

        List<UniTask> tasks = new List<UniTask>(images.Length);
        for (int i = 0; i < images.Length; i++)
        {
            ImageSetting img = images[i];
            tasks.Add(CreateSingleImageAsync(img, parent, token).AsUniTask());
        }

        await UniTask.WhenAll(tasks);
    }

    /// <summary>단일 Image 프리팹 생성 후 스프라이트/색/타입 및 RectTransform 적용</summary>
    public async UniTask<GameObject> CreateSingleImageAsync(ImageSetting setting, GameObject parent, CancellationToken token)
    {
        GameObject go = await InstantiateAsync("Prefabs/ImagePrefab.prefab", parent.transform, token);
        if (go == null) return null;
        go.name = setting.name;

        if (go.TryGetComponent<Image>(out Image image))
        {
            Texture2D texture = UIUtility.LoadTextureFromStreamingAssets(setting.sourceImage);
            if (texture != null)
            {
                image.sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f)
                );
            }

            image.color = setting.color;
            image.type = (Image.Type)setting.type;
        }

        if (go.TryGetComponent<RectTransform>(out RectTransform rt))
        {
            UIUtility.ApplyRect(
                rt,
                size: setting.size,
                anchoredPos: new Vector2(setting.position.x, -setting.position.y),
                rotation: setting.rotation
            );
        }

        return go;
    }

    /// <summary>여러 Button 항목을 비동기로 생성하고 모두 완료될 때까지 대기</summary>
    private async UniTask<List<(GameObject button, GameObject addImage)>> CreateButtonsAsync(
        ButtonSetting[] settings, GameObject parent, CancellationToken token)
    {
        List<(GameObject button, GameObject addImage)> results =
            new List<(GameObject button, GameObject addImage)>();

        if (settings == null || settings.Length == 0) return results;

        List<UniTask<(GameObject button, GameObject addImage)>> tasks =
            new List<UniTask<(GameObject button, GameObject addImage)>>(settings.Length);

        for (int i = 0; i < settings.Length; i++)
        {
            ButtonSetting s = settings[i];
            tasks.Add(CreateSingleButtonAsync(s, parent, token));
        }

        (GameObject button, GameObject addImage)[] created = await UniTask.WhenAll(tasks);
        results.AddRange(created);

        return results;
    }

    /// <summary>단일 Button 프리팹 생성 후 배경(비디오/이미지), 텍스트, 추가 이미지, RectTransform 적용 및 클릭 사운드 연결</summary>
    public async UniTask<(GameObject button, GameObject addImage)> CreateSingleButtonAsync(
        ButtonSetting setting, GameObject parent, CancellationToken token)
    {
        GameObject go = await InstantiateAsync("Prefabs/ButtonPrefab.prefab", parent.transform, token);
        if (go == null) return (null, null);
        go.name = setting.name;

        RectTransform rectTransform = go.GetComponent<RectTransform>();
        RawImage raw = go.GetComponent<RawImage>();
        VideoPlayer vp = go.GetComponent<VideoPlayer>();
        Button button = go.GetComponent<Button>();
        AudioSource audioSource = UIUtility.GetOrAdd<AudioSource>(go);

        if (rectTransform != null)
        {
            UIUtility.ApplyRect(
                rectTransform,
                size: setting.size,
                anchoredPos: new Vector2(setting.position.x, -setting.position.y),
                rotation: setting.rotation
            );
        }

        bool videoApplied = false;
        if (vp != null &&
            setting.buttonBackgroundVideo != null &&
            !string.IsNullOrEmpty(setting.buttonBackgroundVideo.fileName))
        {
            VideoManager.Instance.WireRawImageAndRenderTexture(
                vp,
                raw,
                new Vector2Int(Mathf.RoundToInt(setting.size.x), Mathf.RoundToInt(setting.size.y))
            );

            string url = VideoManager.Instance.ResolvePlayableUrl(setting.buttonBackgroundVideo.fileName);
            bool ok = await VideoManager.Instance.PrepareAndPlayAsync(
                vp, url, audioSource, setting.buttonBackgroundVideo.volume, token
            );
            videoApplied = ok;
        }

        if (!videoApplied && raw != null && setting.buttonBackgroundImage != null)
        {
            Texture2D tex = UIUtility.LoadTextureFromStreamingAssets(setting.buttonBackgroundImage.sourceImage);
            if (tex != null) raw.texture = tex;
            raw.color = setting.buttonBackgroundImage.color;
        }

        TextMeshProUGUI textComp = go.GetComponentInChildren<TextMeshProUGUI>(true);
        if (textComp != null && setting.buttonText != null && !string.IsNullOrEmpty(setting.buttonText.text))
        {
            await ApplyFontAsync(
                textComp,
                setting.buttonText.fontName,
                setting.buttonText.text,
                setting.buttonText.fontSize,
                setting.buttonText.fontColor,
                setting.buttonText.alignment,
                token
            );

            if (textComp.TryGetComponent<RectTransform>(out RectTransform textRT))
            {
                UIUtility.ApplyRect(
                    textRT,
                    size: null,
                    anchoredPos: new Vector2(setting.buttonText.position.x, setting.buttonText.position.y),
                    rotation: setting.buttonText.rotation
                );
            }
        }

        GameObject addImgGo = null;
        if (setting.buttonAdditionalImage != null &&
            !string.IsNullOrEmpty(setting.buttonAdditionalImage.sourceImage))
        {
            addImgGo = await CreateSingleImageAsync(setting.buttonAdditionalImage, go, token);
            if (addImgGo != null && addImgGo.TryGetComponent<RectTransform>(out RectTransform addRT))
            {
                UIUtility.ApplyRect(
                    addRT,
                    size: setting.buttonAdditionalImage.size,
                    anchoredPos: new Vector2(
                        setting.buttonAdditionalImage.position.x,
                        -setting.buttonAdditionalImage.position.y
                    ),
                    rotation: setting.buttonAdditionalImage.rotation
                );
            }
        }

        if (button != null)
        {
            string soundKey = setting.buttonSound;
            if (!string.IsNullOrEmpty(soundKey))
                button.onClick.AddListener(() => { AudioManager.Instance?.Play(soundKey); });
        }

        return (go, addImgGo);
    }

    /// <summary>VideoPlayer 프리팹 생성 후 RenderTexture/오디오 연결 및 재생 준비</summary>
    public async UniTask<GameObject> CreateVideoPlayerAsync(VideoSetting setting, GameObject parent, CancellationToken token)
    {
        if (setting == null || string.IsNullOrEmpty(setting.fileName) || VideoManager.Instance == null)
            return null;

        token.ThrowIfCancellationRequested();

        GameObject go = await InstantiateAsync("Prefabs/VideoPlayerPrefab.prefab", parent.transform, token);
        if (go == null) return null;
        go.name = setting.name;

        VideoPlayer vp = go.GetComponent<VideoPlayer>();
        RawImage raw = go.GetComponent<RawImage>();
        AudioSource audioSource = UIUtility.GetOrAdd<AudioSource>(go);

        if (vp == null)
        {
            Debug.LogError("[UICreator] Video prefab missing VideoPlayer component");
            return go;
        }

        if (go.TryGetComponent<RectTransform>(out RectTransform rt))
        {
            UIUtility.ApplyRect(
                rt,
                size: setting.size,
                anchoredPos: new Vector2(setting.position.x, -setting.position.y),
                rotation: Vector3.zero
            );
        }

        VideoManager.Instance.WireRawImageAndRenderTexture(
            vp,
            raw,
            new Vector2Int(Mathf.RoundToInt(setting.size.x), Mathf.RoundToInt(setting.size.y))
        );

        string url = VideoManager.Instance.ResolvePlayableUrl(setting.fileName);
        bool ok = await VideoManager.Instance.PrepareAndPlayAsync(vp, url, audioSource, setting.volume, token);

        if (!ok)
            Debug.LogError($"[UICreator] Failed to prepare video: {url}");

        return go;
    }

    /// <summary>여러 팝업 설정 중 지정 인덱스 팝업 생성 요청을 내부 메서드로 위임</summary>
    public UniTask<GameObject> CreatePopupsAsync(
        PopupSetting[] allPopups,
        int index,
        GameObject parent,
        UnityAction<GameObject> onClose = null,
        CancellationToken token = default)
    {
        return CreatePopupAsync(allPopups, index, parent, onClose, token);
    }

    /// <summary>지정 인덱스의 팝업을 생성하고 배경/텍스트/이미지/버튼을 구성해 반환</summary>
    private async UniTask<GameObject> CreatePopupAsync(
        PopupSetting[] allPopups,
        int index,
        GameObject parent,
        UnityAction<GameObject> onClose,
        CancellationToken token)
    {
        if (allPopups == null || index < 0 || index >= allPopups.Length) return null;

        PopupSetting setting = allPopups[index];

        GameObject popupRoot = new GameObject(string.IsNullOrEmpty(setting.name) ? "GeneratedPopup" : setting.name);
        popupRoot.transform.SetParent(parent.transform, false);

        GameObject popupBg = await CreateBackgroundImageAsync(setting.popupBackgroundImage, popupRoot, token);
        if (popupBg == null) return popupRoot;
        popupBg.transform.SetAsLastSibling();

        List<UniTask> pending = new List<UniTask>(2)
        {
            CreateTextsAsync(setting.popupTexts, popupBg, token),
            CreateImagesAsync(setting.popupImages, popupBg, token)
        };
        await UniTask.WhenAll(pending);

        if (setting.popupCloseButton != null)
        {
            (GameObject btnGo, GameObject _) = await CreateSingleButtonAsync(setting.popupCloseButton, popupBg, token);
            if (btnGo != null && btnGo.TryGetComponent<Button>(out Button btn))
            {
                btn.onClick.AddListener(() =>
                {
                    onClose?.Invoke(popupRoot);
                    popupRoot.SetActive(false);
                });
            }
        }

        return popupRoot;
    }

    /// <summary>페이지 루트를 생성하고 RectTransform 설정 후 하위 요소들(텍스트/이미지/버튼) 병렬 생성</summary>
    public async UniTask<GameObject> CreatePageAsync(PageSetting page, GameObject parent, CancellationToken token)
    {
        GameObject pageRoot = new GameObject(string.IsNullOrEmpty(page.name) ? "GeneratedPage" : page.name);
        pageRoot.transform.SetParent(parent.transform, false);

        RectTransform rt = pageRoot.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(page.position.x, -page.position.y);
        rt.sizeDelta = page.size;

        List<UniTask> jobs = new List<UniTask>(3)
        {
            CreateTextsAsync(page.texts, pageRoot, token),
            CreateImagesAsync(page.images, pageRoot, token),
            CreateButtonsAsync(page.buttons, pageRoot, token).AsUniTask()
        };

        await UniTask.WhenAll(jobs);
        return pageRoot;
    }

    public async UniTask<GameObject> CreateEffectAsync(EffectSetting setting, GameObject parent, CancellationToken token = default)
    {
        GameObject go = await InstantiateAsync("Prefabs/EffectPrefab.prefab", parent.transform, token);
        if (go == null) return null;
        go.name = setting.name;

        if (go.TryGetComponent<RectTransform>(out RectTransform rt) &&
            parent.TryGetComponent<RectTransform>(out RectTransform parentRect))
        {
            UIUtility.ApplyRect(
                rt,
                size: setting.size,
                anchoredPos: new Vector2(setting.position.x, -setting.position.y)
            );
        }

        return go;
    }

    public async UniTask<GameObject> CreateGameObjectAsync(GameObjectSetting setting, GameObject parent, CancellationToken token = default)
    {
        GameObject go = await InstantiateAsync("Prefabs/GameObjectPrefab.prefab", parent.transform, token);
        if (go == null) return null;
        go.name = setting.name;

        if (go.TryGetComponent<Transform>(out Transform trans))
        {
            trans.parent = parent.transform;
            trans.position = setting.position;
            trans.localScale = setting.size;
            trans.rotation = Quaternion.Euler(setting.rotation);
        }

        return go;
    }
}
