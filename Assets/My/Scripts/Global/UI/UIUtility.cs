using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

public static class UIUtility
{
    /// <summary> Addressables 핸들을 취소 토큰과 함께 대기 후 결과 반환 </summary>
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

    /// <summary> StreamingAssets 하위 경로에서 Texture2D 로드 (동기) </summary>
    public static Texture2D LoadTextureFromStreamingAssets(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;

        string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);

        if (!File.Exists(fullPath)) return null;

        byte[] fileData = File.ReadAllBytes(fullPath);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        bool ok = texture.LoadImage(fileData);
        if (!ok) return null;
        return texture;
    }

    /// <summary> 타입 T 컴포넌트를 가져오거나 없으면 추가해서 반환 </summary>
    public static T GetOrAdd<T>(GameObject go) where T : Component
    {
        if (!go) return null;

        T component;
        if (go.TryGetComponent<T>(out component)) return component;

        component = go.AddComponent<T>();
        return component;
    }

    /// <summary> RectTransform 기본 속성 적용(size, anchoredPos, rotation, scale) </summary>
    public static void ApplyRect(RectTransform rt, Vector2? size = null, Vector2? anchoredPos = null, Vector3? rotation = null, Vector3? scale = null)
    {
        if (!rt) return;

        if (size.HasValue) rt.sizeDelta = size.Value;
        if (anchoredPos.HasValue) rt.anchoredPosition = anchoredPos.Value;
        if (rotation.HasValue) rt.localRotation = Quaternion.Euler(rotation.Value);
        if (scale.HasValue) rt.localScale = scale.Value;
    }
}
