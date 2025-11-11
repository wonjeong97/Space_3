using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class 
    UICreator : MonoBehaviour
{
    public static UICreator Instance { get; private set; }

    private readonly List<GameObject> _instances = new List<GameObject>();
    private readonly Dictionary<string, AsyncOperationHandle> _assetCache = new Dictionary<string, AsyncOperationHandle>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);
    }

    /// <summary>Addressables 에셋 로드를 캐시해 중복 로드 방지</summary>
    private async UniTask<T> LoadAssetWithCacheAsync<T>(string key, CancellationToken token) where T : Object
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
    public async UniTask ApplyFontAsync(TextMeshProUGUI uiText, string fontKey, string textValue, float fontSize,
                                        Color fontColor, TextAlignmentOptions alignment, CancellationToken token)
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
}
