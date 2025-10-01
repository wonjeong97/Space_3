using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Networking;

public static class UIUtility
{
    /// <summary>
    /// Addressables 핸들을 취소 토큰과 함께 대기 후 결과 반환 (UniTask)
    /// - UniTask.Addressables 패키지 없이도 동작하는 기본 구현.
    /// - 취소 발생 시 OperationCanceledException 전파.
    /// - 실패 시 Addressables의 OperationException 전파.
    /// </summary>
    public static async UniTask<T> AwaitWithCancellation<T>(AsyncOperationHandle<T> handle, CancellationToken token)
    {
        // 취소 또는 완료까지 프레임 단위 대기
        await UniTask.WaitUntil(() => handle.IsDone, cancellationToken: token);

        if (handle.Status == AsyncOperationStatus.Failed)
        {
            Exception ex = handle.OperationException ?? new Exception("Addressables operation failed.");
            throw ex;
        }

        return handle.Result;
    }

    /// <summary>
    /// StreamingAssets 하위 경로에서 Texture2D 로드 (동기)
    /// - 에디터/데스크톱 환경 중심. Android 등에서는 동작하지 않을 수 있음.
    /// - 상위 코드 호환을 위해 기존 시그니처 유지
    /// </summary>
    public static Texture2D LoadTextureFromStreamingAssets(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;

        string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);

#if UNITY_ANDROID && !UNITY_EDITOR
        // Android에선 파일 시스템으로 직접 접근이 어려움. null 반환.
        // 필요 시 LoadTextureFromStreamingAssetsAsync 사용 권장.
        return null;
#else
        if (!File.Exists(fullPath)) return null;

        byte[] fileData = File.ReadAllBytes(fullPath);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        bool ok = texture.LoadImage(fileData);
        if (!ok) return null;
        return texture;
#endif
    }

    /// <summary>
    /// StreamingAssets 하위 경로에서 Texture2D 로드 (취소 가능, 크로스 플랫폼 안전, UniTask)
    /// - Android 포함 모든 플랫폼에서 동작을 목표로 UnityWebRequest 사용
    /// </summary>
    public static async UniTask<Texture2D> LoadTextureFromStreamingAssetsAsync(string relativePath, CancellationToken token)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;

        string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath).Replace("\\", "/");
        string url = fullPath;
        if (!fullPath.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            url = "file://" + fullPath;
        }

        using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(url))
        {
            UnityWebRequestAsyncOperation op = req.SendWebRequest();
            // 요청 진행을 프레임 단위로 기다리며 취소 대응
            while (!op.isDone)
            {
                token.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogWarning($"[UIUtility] Texture load failed: {relativePath} - {req.error}");
                return null;
            }

            DownloadHandlerTexture dht = (DownloadHandlerTexture)req.downloadHandler;
            Texture2D tex = dht?.texture;
            return tex;
        }
    }

    /// <summary>
    /// 타입 T 컴포넌트를 가져오거나 없으면 추가해서 반환
    /// </summary>
    public static T GetOrAdd<T>(GameObject go) where T : Component
    {
        if (!go) return null;

        T component;
        if (go.TryGetComponent<T>(out component)) return component;

        component = go.AddComponent<T>();
        return component;
    }

    /// <summary>
    /// RectTransform 기본 속성 적용(size, anchoredPos, rotation)
    /// </summary>
    public static void ApplyRect(
        RectTransform rt,
        Vector2? size = null,
        Vector2? anchoredPos = null,
        Vector3? rotation = null)
    {
        if (!rt) return;

        if (size.HasValue) rt.sizeDelta = size.Value;
        if (anchoredPos.HasValue) rt.anchoredPosition = anchoredPos.Value;
        if (rotation.HasValue) rt.localRotation = Quaternion.Euler(rotation.Value);
    }
}
