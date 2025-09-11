using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

public static class UIUtility
{
    /// <summary>두 취소 토큰을 결합해 필요 시 링크된 토큰 반환</summary>
    public static CancellationToken MergeTokens(CancellationToken a, CancellationToken b)
    {
        bool aCan = a.CanBeCanceled;
        bool bCan = b.CanBeCanceled;
        if (aCan && bCan)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(a, b);
            return linked.Token; // 호출 측에서 Dispose를 하지 않는 일시적 사용을 전제로 함
        }

        return aCan ? a : (bCan ? b : CancellationToken.None);
    }

    /// <summary>취소 토큰을 지원해 Addressables 핸들을 대기하고 결과 반환</summary>
    public static async Task<T> AwaitWithCancellation<T>(AsyncOperationHandle<T> handle, CancellationToken token)
    {
        TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
        using (token.Register(() => tcs.TrySetResult(true)))
        {
            Task completed = await Task.WhenAny(handle.Task, tcs.Task);
            if (completed != handle.Task)
                throw new OperationCanceledException(token);
            return await handle.Task;
        }
    }
    
    /// <summary>StreamingAssets 하위 경로에서 Texture2D 로드</summary>
    public static Texture2D LoadTextureFromStreamingAssets(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;
        string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);
        if (!File.Exists(fullPath)) return null;

        byte[] fileData = File.ReadAllBytes(fullPath);
        var texture = new Texture2D(2, 2);
        texture.LoadImage(fileData);
        return texture;
    }

    /// <summary>타입 T 컴포넌트를 가져오거나 없으면 추가해서 반환</summary>
    public static T GetOrAdd<T>(GameObject go) where T : Component
    {
        if (!go) return null;
        if (go.TryGetComponent<T>(out var c)) return c;
        return go.AddComponent<T>();
    }

    /// <summary>RectTransform 기본 속성 적용(size, anchoredPos, rotation)</summary>
    public static void ApplyRect(RectTransform rt, Vector2? size = null, Vector2? anchoredPos = null,
        Vector3? rotation = null)
    {
        if (!rt) return;
        if (size.HasValue) rt.sizeDelta = size.Value;
        if (anchoredPos.HasValue) rt.anchoredPosition = anchoredPos.Value;
        if (rotation.HasValue) rt.localRotation = Quaternion.Euler(rotation.Value);
    }
}