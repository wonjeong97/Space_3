using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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

    private readonly List<GameObject> instances = new();
    private readonly Dictionary<string, AsyncOperationHandle> assetCache = new();

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

    /// <summary>Addressables로 프리팹 비동기 인스턴스화</summary>
    private async Task<GameObject> InstantiateAsync(string key, Transform parent, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); // 즉시 취소 검사
        AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(key, parent); // 인스턴스화 시작
        try
        {
            // 취소 토큰을 지원해 완료/취소 중 먼저 끝나는 쪽을 대기
            GameObject go = await UIUtility.AwaitWithCancellation(handle, token);

            if (go) instances.Add(go); // 성공 시 추적 목록에 등록
            return go; // 생성된 인스턴스 반환
        }
        catch (OperationCanceledException)
        {
            // 취소된 경우: 이미 생성이 끝났다면 Addressables 인스턴스 해제
            if (handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded && handle.Result)
                Addressables.ReleaseInstance(handle.Result);
            throw;
        }
        catch (Exception)
        {
            // 일반 예외: 생성이 끝났다면 인스턴스 해제 후 null 반환
            if (handle.IsValid() && handle.Result)
                Addressables.ReleaseInstance(handle.Result);
            return null;
        }
    }

    /// <summary>Addressables 에셋 로드를 캐시해 중복 로드 방지</summary>
    private async Task<T> LoadAssetWithCacheAsync<T>(string key, CancellationToken token) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(key)) return null; // 유효하지 않은 키 방어

        // 캐시에 있으면 즉시 반환(핸들 유효성 확인)
        if (assetCache.TryGetValue(key, out AsyncOperationHandle existing))
        {
            return existing.IsValid() ? (T)existing.Result : null;
        }

        token.ThrowIfCancellationRequested(); // 즉시 취소 검사

        // Addressables 로드 시작
        AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(key);

        // 취소 토큰을 지원하여 완료/취소 중 먼저 끝나는 쪽 대기
        T asset = await UIUtility.AwaitWithCancellation(handle, token);

        // 핸들을 캐시에 저장(나중에 일괄 Release용)
        assetCache[key] = handle;

        return asset; // 로드된 에셋 반환
    }

    /// <summary>Addressables 에셋 캐시 전부 해제하고 비우기</summary>
    private void ReleaseAllCachedAssets()
    {
        foreach (KeyValuePair<string, AsyncOperationHandle> kv in assetCache) // 캐시 항목 순회
        {
            if (kv.Value.IsValid()) Addressables.Release(kv.Value); // 유효한 핸들만 Release
        }

        assetCache.Clear(); // 캐시 딕셔너리 비우기
    }

    /// <summary>추적 중인 Addressables 인스턴스 전부 해제</summary>
    public void DestroyAllTrackedInstances()
    {
        for (int i = instances.Count - 1; i >= 0; --i) // 뒤에서부터 해제(인덱스 안전)
        {
            GameObject go = instances[i];
            if (go != null) Addressables.ReleaseInstance(go); // 유효한 것만 Release
        }

        instances.Clear(); // 추적 목록 비우기
    }

    /// <summary>추적 중인 특정 인스턴스 해제 시도 후 성공 여부 반환</summary>
    public bool DestroyTrackedInstance(GameObject go)
    {
        if (go == null) return false; // 유효성 검사

        int idx = instances.IndexOf(go); // 추적 목록에서 위치 조회
        if (idx >= 0)
        {
            Addressables.ReleaseInstance(go); // Addressables 인스턴스 해제
            instances.RemoveAt(idx); // 추적 목록에서 제거
            return true; // 해제 성공
        }

        return false; // 추적 목록에 없으면 실패
    }

    // ---------- Font/Material helpers (creation-time concerns) ----------

    /// <summary>폰트 키를 FontMap 기준으로 해석해 매핑된 키 반환</summary>
    private static string ResolveFontKey(string key)
    {
        Settings settings = JsonLoader.Instance?.settings; // Settings 참조
        FontMaps fontMap = settings?.fontMap; // FontMap 참조
        if (fontMap == null || string.IsNullOrEmpty(key)) return key; // 매핑 불가 시 원본 키 반환

        FieldInfo field = typeof(FontMaps).GetField(key); // 키 이름과 동일한 필드 조회(리플렉션)
        if (field != null)
        {
            string mapped = field.GetValue(fontMap) as string; // 필드 값 읽기
            return string.IsNullOrEmpty(mapped) ? key : mapped; // 매핑 값이 비어있으면 원본 유지
        }

        return key; // 필드 없음: 원본 키 반환
    }

    /// <summary>폰트 키 매핑과 에셋 로드를 거쳐 TMP 텍스트 속성 적용</summary>
    private async Task ApplyFontAsync(TextMeshProUGUI uiText, string fontKey, string textValue,
        float fontSize, Color fontColor, TextAlignmentOptions alignment, CancellationToken token)
    {
        if (!uiText || string.IsNullOrEmpty(fontKey)) return; // 방어 코드

        string mapped = ResolveFontKey(fontKey); // FontMap 기준 키 매핑
        TMP_FontAsset font = await LoadAssetWithCacheAsync<TMP_FontAsset>(mapped, token); // 폰트 에셋 로드(캐시 사용)
        if (!font) return; // 로드 실패 시 종료

        token.ThrowIfCancellationRequested(); // 즉시 취소 검사

        uiText.font = font; // 폰트 적용
        uiText.fontSize = fontSize; // 폰트 크기 적용
        uiText.color = fontColor; // 폰트 색상 적용
        uiText.alignment = alignment; // 정렬 적용
        uiText.text = textValue; // 텍스트 내용 적용
    }

    /// <summary>타깃 이미지에 Addressable로 로드한 머티리얼을 적용함 </summary>
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

    // ---------- Public creation APIs ----------

    /// <summary>캔버스 프리팹을 Addressables로 비동기 생성해 반환</summary>
    public async Task<GameObject> CreateCanvasAsync(CancellationToken token = default)
    {
        return await InstantiateAsync("Prefabs/CanvasPrefab.prefab", null, token);
    }

    /// <summary>배경 이미지를 생성하고 RectTransform 기본값 설정 후 반환</summary>
    public async Task<GameObject> CreateBackgroundImageAsync(ImageSetting setting, GameObject parent,
        CancellationToken token)
    {
        GameObject go = await CreateSingleImageAsync(setting, parent, token); // 이미지 생성
        if (go != null && go.TryGetComponent(out RectTransform rt))
        {
            rt.anchorMin = new Vector2(0f, 1f); // 좌상단 기준 고정
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero; // 부모의 좌상단 위치
            rt.localRotation = Quaternion.Euler(setting.rotation); // 회전 적용
            rt.sizeDelta = setting.size; // 사이즈 적용
        }

        return go; // 생성된 배경 이미지 반환
    }

    /// <summary>여러 Text 항목을 비동기로 생성하고 모두 완료될 때까지 대기</summary>
    private async Task CreateTextsAsync(TextSetting[] settings, GameObject parent, CancellationToken token)
    {
        if (settings == null || settings.Length == 0) return; // 설정이 없으면 종료

        List<Task> tasks = new List<Task>(settings.Length); // 작업 리스트(초기 용량 예약)
        foreach (TextSetting s in settings) // 각 텍스트 설정에 대해
            tasks.Add(CreateSingleTextAsync(s, parent, token)); // 생성 작업 추가

        await Task.WhenAll(tasks); // 모든 생성 작업 완료 대기
    }

    /// <summary>단일 Text 프리팹 생성 후 TMP 속성과 RectTransform 적용</summary>
    public async Task<GameObject> CreateSingleTextAsync(TextSetting setting, GameObject parent, CancellationToken token)
    {
        GameObject go = await InstantiateAsync("Prefabs/TextPrefab.prefab", parent.transform, token); // 프리팹 인스턴스화
        if (go == null) return null; // 생성 실패 방어
        go.name = setting.name; // 이름 지정

        if (go.TryGetComponent(out TextMeshProUGUI uiText)) // TMP 컴포넌트가 있으면
        {
            // 폰트/텍스트/크기/색/정렬 적용(폰트 로드는 캐시 사용)
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

        if (go.TryGetComponent(out RectTransform rt)) // RectTransform 있으면
        {
            // 상단 기준 좌표 보정: Unity UI는 아래가 +Y인 경우가 있어 화면 상단 정렬 시 -Y 보정
            UIUtility.ApplyRect(rt,
                size: null,
                anchoredPos: new Vector2(setting.position.x, -setting.position.y),
                rotation: setting.rotation);
        }

        return go;
    }

    /// <summary>여러 Image 항목을 비동기로 생성하고 모두 완료될 때까지 대기</summary>
    private async Task CreateImagesAsync(ImageSetting[] images, GameObject parent, CancellationToken token)
    {
        if (images == null || images.Length == 0) return; // 설정이 없으면 종료

        List<Task> tasks = new List<Task>(images.Length); // 병렬 실행 작업 리스트 준비
        foreach (ImageSetting img in images) // 각 이미지에 대해
            tasks.Add(CreateSingleImageAsync(img, parent, token)); // 생성 작업 추가

        await Task.WhenAll(tasks); // 모든 이미지 생성 완료 대기
    }

    /// <summary>단일 Image 프리팹 생성 후 스프라이트/색/타입 및 RectTransform 적용</summary>
    public async Task<GameObject> CreateSingleImageAsync(ImageSetting setting, GameObject parent,
        CancellationToken token)
    {
        GameObject go = await InstantiateAsync("Prefabs/ImagePrefab.prefab", parent.transform, token); // 프리팹 인스턴스화
        if (go == null) return null; // 생성 실패 방어
        go.name = setting.name; // 이름 지정

        if (go.TryGetComponent(out Image image)) // Image 컴포넌트가 있으면
        {
            Texture2D texture = UIUtility.LoadTextureFromStreamingAssets(setting.sourceImage); // 스트리밍 텍스처 로드
            if (texture != null)
            {
                // 런타임 스프라이트 생성(중심 pivot 0.5,0.5)
                image.sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f)
                );
            }

            image.color = setting.color; // 색상 적용
            image.type = (Image.Type)setting.type; // 이미지 타입 적용
        }

        if (go.TryGetComponent(out RectTransform rt)) // RectTransform 있으면
        {
            // UI 좌표 보정: 위쪽 원점 기준을 위해 y에 음수 부호 적용
            UIUtility.ApplyRect(rt,
                size: setting.size,
                anchoredPos: new Vector2(setting.position.x, -setting.position.y),
                rotation: setting.rotation
            );
        }

        return go; // 생성된 이미지 반환
    }

    /// <summary>여러 Button 항목을 비동기로 생성하고 모두 완료될 때까지 대기</summary>
    private async Task<List<(GameObject button, GameObject addImage)>> CreateButtonsAsync(
        ButtonSetting[] settings, GameObject parent, CancellationToken token)
    {
        // 결과 리스트 준비
        List<(GameObject button, GameObject addImage)> results =
            new List<(GameObject button, GameObject addImage)>();

        if (settings == null || settings.Length == 0) return results; // 설정 없으면 빈 리스트 반환

        // 병렬 실행 작업 리스트 준비(초기 용량 예약)
        List<Task<(GameObject button, GameObject addImage)>> tasks =
            new List<Task<(GameObject button, GameObject addImage)>>(settings.Length);

        // 각 버튼 생성 작업 추가
        foreach (ButtonSetting s in settings)
            tasks.Add(CreateSingleButtonAsync(s, parent, token));

        // 모든 버튼 생성 완료 대기
        (GameObject button, GameObject addImage)[] created = await Task.WhenAll(tasks);

        // 생성 결과 병합
        results.AddRange(created);

        return results; // 생성된 버튼 목록 반환
    }

    /// <summary>단일 Button 프리팹 생성 후 배경(비디오/이미지), 텍스트, 추가 이미지, RectTransform 적용 및 클릭 사운드 연결</summary>
    public async Task<(GameObject button, GameObject addImage)> CreateSingleButtonAsync(
        ButtonSetting setting, GameObject parent, CancellationToken token)
    {
        // 버튼 프리팹 인스턴스화
        GameObject go = await InstantiateAsync("Prefabs/ButtonPrefab.prefab", parent.transform, token);
        if (go == null) return (null, null);
        go.name = setting.name;

        // 주요 컴포넌트 참조
        RectTransform rectTransform = go.GetComponent<RectTransform>();
        RawImage raw = go.GetComponent<RawImage>();
        VideoPlayer vp = go.GetComponent<VideoPlayer>();
        Button button = go.GetComponent<Button>();
        AudioSource audioSource = UIUtility.GetOrAdd<AudioSource>(go);

        // 위치/사이즈/회전 적용
        if (rectTransform != null)
        {
            UIUtility.ApplyRect(
                rectTransform,
                size: setting.size,
                anchoredPos: new Vector2(setting.position.x, -setting.position.y),
                rotation: setting.rotation
            );
        }

        // 배경 비디오 우선 적용(가능하면)
        bool videoApplied = false;
        if (vp != null &&
            setting.buttonBackgroundVideo != null &&
            !string.IsNullOrEmpty(setting.buttonBackgroundVideo.fileName))
        {
            // 비디오 출력 대상 연결(RenderTexture를 RawImage에 연결)
            VideoManager.Instance.WireRawImageAndRenderTexture(
                vp,
                raw,
                new Vector2Int(Mathf.RoundToInt(setting.size.x), Mathf.RoundToInt(setting.size.y))
            );

            // 재생 준비 및 볼륨/오디오 연결
            string url = VideoManager.Instance.ResolvePlayableUrl(setting.buttonBackgroundVideo.fileName);
            bool ok = await VideoManager.Instance.PrepareAndPlayAsync(
                vp, url, audioSource, setting.buttonBackgroundVideo.volume, token
            );
            videoApplied = ok;
        }

        // 비디오가 없거나 실패하면 정적 이미지 적용
        if (!videoApplied && raw != null && setting.buttonBackgroundImage != null)
        {
            Texture2D tex = UIUtility.LoadTextureFromStreamingAssets(setting.buttonBackgroundImage.sourceImage);
            if (tex != null) raw.texture = tex;
            raw.color = setting.buttonBackgroundImage.color;
        }

        // 버튼 텍스트(TMP) 적용
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

            if (textComp.TryGetComponent(out RectTransform textRT))
            {
                UIUtility.ApplyRect(
                    textRT,
                    size: null,
                    anchoredPos: new Vector2(setting.buttonText.position.x, setting.buttonText.position.y),
                    rotation: setting.buttonText.rotation
                );
            }
        }

        // 추가 이미지(아이콘 등) 생성/배치
        GameObject addImgGo = null;
        if (setting.buttonAdditionalImage != null &&
            !string.IsNullOrEmpty(setting.buttonAdditionalImage.sourceImage))
        {
            addImgGo = await CreateSingleImageAsync(setting.buttonAdditionalImage, go, token);
            if (addImgGo != null && addImgGo.TryGetComponent(out RectTransform addRT))
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

        // 클릭 사운드 연결
        if (button != null)
        {
            string soundKey = setting.buttonSound;
            if (!string.IsNullOrEmpty(soundKey))
                button.onClick.AddListener(() => { AudioManager.Instance?.Play(soundKey); });
        }

        return (go, addImgGo);
    }

    /// <summary>VideoPlayer 프리팹 생성 후 RenderTexture/오디오 연결 및 재생 준비</summary>
    public async Task<GameObject> CreateVideoPlayerAsync(VideoSetting setting, GameObject parent,
        CancellationToken token)
    {
        if (setting == null || string.IsNullOrEmpty(setting.fileName) || VideoManager.Instance == null)
            return null; // 설정/파일명/비디오 매니저 검사

        token.ThrowIfCancellationRequested(); // 즉시 취소 검사

        GameObject go =
            await InstantiateAsync("Prefabs/VideoPlayerPrefab.prefab", parent.transform, token); // 프리팹 인스턴스화
        if (go == null) return null; // 생성 실패 방어
        go.name = setting.name; // 이름 지정

        VideoPlayer vp = go.GetComponent<VideoPlayer>(); // 비디오 플레이어
        RawImage raw = go.GetComponent<RawImage>(); // 출력 대상
        AudioSource audioSource = UIUtility.GetOrAdd<AudioSource>(go); // 오디오 소스 확보

        if (vp == null)
        {
            Debug.LogError("[UICreator] Video prefab missing VideoPlayer component"); // 필수 컴포넌트 누락
            return go;
        }

        if (go.TryGetComponent(out RectTransform rt)) // RectTransform 적용
        {
            UIUtility.ApplyRect(
                rt,
                size: setting.size,
                anchoredPos: new Vector2(setting.position.x, -setting.position.y),
                rotation: Vector3.zero
            );
        }

        // RenderTexture를 생성/바인딩하여 RawImage로 출력
        VideoManager.Instance.WireRawImageAndRenderTexture(
            vp,
            raw,
            new Vector2Int(Mathf.RoundToInt(setting.size.x), Mathf.RoundToInt(setting.size.y))
        );

        // 재생 준비(파일 경로 해석 → 준비/재생, 볼륨/오디오 연결)
        string url = VideoManager.Instance.ResolvePlayableUrl(setting.fileName);
        bool ok = await VideoManager.Instance.PrepareAndPlayAsync(vp, url, audioSource, setting.volume, token);

        if (!ok)
            Debug.LogError($"[UICreator] Failed to prepare video: {url}");

        return go; // 생성된 비디오 플레이어 반환
    }

    /// <summary>여러 팝업 설정 중 지정 인덱스 팝업 생성 요청을 내부 메서드로 위임</summary>
    public Task<GameObject> CreatePopupsAsync(PopupSetting[] allPopups, int index, GameObject parent,
        UnityAction<GameObject> onClose = null, CancellationToken token = default)
        => CreatePopupAsync(allPopups, index, parent, onClose, token);

    /// <summary>지정 인덱스의 팝업을 생성하고 배경/텍스트/이미지/버튼을 구성해 반환</summary>
    private async Task<GameObject> CreatePopupAsync(
        PopupSetting[] allPopups, int index,
        GameObject parent, UnityAction<GameObject> onClose, CancellationToken token)
    {
        if (allPopups == null || index < 0 || index >= allPopups.Length) return null; // 인덱스/배열 유효성 검사
        PopupSetting setting = allPopups[index]; // 대상 팝업 설정

        // 팝업 루트 생성 및 부모 연결
        GameObject popupRoot = new GameObject(string.IsNullOrEmpty(setting.name) ? "GeneratedPopup" : setting.name);
        popupRoot.transform.SetParent(parent.transform, false);
        // popupRoot.AddComponent<PopupObject>(); // 비활성화 시 정리 담당

        // 팝업 배경 생성(없으면 루트만 반환)
        GameObject popupBg = await CreateBackgroundImageAsync(setting.popupBackgroundImage, popupRoot, token);
        if (!popupBg) return popupRoot;
        popupBg.transform.SetAsLastSibling(); // 배경을 뒤로 배치

        // 텍스트/이미지 병렬 생성
        List<Task> pending = new List<Task>(2)
        {
            CreateTextsAsync(setting.popupTexts, popupBg, token),
            CreateImagesAsync(setting.popupImages, popupBg, token)
        };
        await Task.WhenAll(pending);

        // 닫기 버튼이 있으면 연결
        if (setting.popupCloseButton != null)
        {
            (GameObject btnGo, GameObject _) = await CreateSingleButtonAsync(setting.popupCloseButton, popupBg, token);
            if (btnGo != null && btnGo.TryGetComponent(out Button btn))
            {
                btn.onClick.AddListener(() =>
                {
                    onClose?.Invoke(popupRoot); // 외부 콜백
                    popupRoot.SetActive(false); // 비활성화 → PopupObject.OnDisable에서 정리
                });
            }
        }

        return popupRoot; // 완성된 팝업 루트 반환
    }

    /// <summary>페이지 루트를 생성하고 RectTransform 설정 후 하위 요소들(텍스트/이미지/버튼) 병렬 생성</summary>
    public async Task<GameObject> CreatePageAsync(PageSetting page, GameObject parent, CancellationToken token)
    {
        // 1) 페이지 루트 GO 생성 및 부모 연결
        var pageRoot = new GameObject(string.IsNullOrEmpty(page.name) ? "GeneratedPage" : page.name);
        pageRoot.transform.SetParent(parent.transform, false);

        // 2) RectTransform 기본값 설정(좌상단 기준, 화면 상단 정렬을 위해 y에 음수 보정)
        var rt = pageRoot.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f); // 좌상단 고정
        rt.anchoredPosition = new Vector2(page.position.x, -page.position.y); // 상단 기준 좌표 보정
        rt.sizeDelta = page.size; // 페이지 영역 크기

        // 3) 하위 요소 생성 작업을 병렬로 수행
        var jobs = new List<Task>(4)
        {
            CreateTextsAsync(page.texts, pageRoot, token), // 텍스트 생성
            CreateImagesAsync(page.images, pageRoot, token), // 이미지 생성
            CreateButtonsAsync(page.buttons, pageRoot, token) // 버튼 생성
            // TODO: CreateKeyboardsAsync / CreateVideosAsync
        };

        await Task.WhenAll(jobs); // 모든 생성 작업 완료 대기
        return pageRoot; // 완성된 페이지 루트 반환
    }

    public async Task<GameObject> CreateEffectAsync(EffectSetting setting, GameObject parent, CancellationToken token = default)
    {
        var go = await InstantiateAsync("Prefabs/EffectPrefab.prefab", parent.transform, token);
        go.name = setting.name;

        if (go.TryGetComponent(out RectTransform rt) && (parent.TryGetComponent(out RectTransform parentRect)))
        {
            UIUtility.ApplyRect(
                rt,
                size: setting.size,
                anchoredPos: new Vector2(setting.position.x, -setting.position.y)
            );
        }

        return go;
    }

    public async Task<GameObject> CreateGameObjectAsync(GameObjectSetting setting, GameObject parent, CancellationToken token = default)
    {
        var go = await InstantiateAsync("Prefabs/GameObjectPrefab.prefab", parent.transform, token);
        go.name = setting.name;

        if (go.TryGetComponent(out Transform trans))
        {
            trans.parent = parent.transform;
            trans.position = setting.position;
            trans.localScale = setting.size;
            trans.rotation = Quaternion.Euler(setting.rotation);
        }

        return go;
    }
}