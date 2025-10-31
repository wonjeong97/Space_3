using UnityEngine;

public class LauncherAnimEvent : MonoBehaviour
{
    [Header("Detach Settings")]
    [Tooltip("떼어낼 자식 Transform. 비워두면 API로 직접 지정하거나 이름으로 찾으세요.")]
    [SerializeField] private Transform childToDetach;

    [Tooltip("새 부모. 비워두면 씬 루트로 이동.")]
    [SerializeField] private Transform reparentTo;

    [Tooltip("부모 변경 시 월드 좌표/회전/스케일 유지")]
    [SerializeField] private bool keepWorldPosition = true;
    
    [SerializeField] private GameObject launcherObj;

    private static void Log(string method, string msg)
    {
        Debug.Log($"[LauncherAnimEvent] {method}-> {msg}");
    }

    private static void LogWarn(string method, string msg)
    {
        Debug.LogWarning($"[LauncherAnimEvent] {method}-> {msg}");
    }

    private static void LogError(string method, string msg)
    {
        Debug.LogError($"[LauncherAnimEvent] {method}-> {msg}");
    }

    // ======================
    // Public API
    // ======================

    /// <summary>인스펙터에서 지정한 childToDetach를 현재 부모에서 떼고 reparentTo(또는 씬 루트)로 보낸다.</summary>
    public void DetachChildNow()
    {
        if (childToDetach == null)
        {
            LogWarn(nameof(DetachChildNow), "childToDetach is null");
            return;
        }
        InternalDetach(childToDetach);
    }

    // ======================
    // Internal
    // ======================

    /// <summary>부모를 reparentTo(또는 null로 씬 루트)로 변경한다.</summary>
    private void InternalDetach(Transform child)
    {
        if (child.parent != transform && child.parent != null)
        {
            LogWarn(nameof(InternalDetach), $"'{child.name}' is not a direct child of '{name}'. Detaching anyway.");
        }

        Transform newParent = reparentTo != null ? reparentTo : null; // null -> 씬 루트
        child.SetParent(newParent, keepWorldPosition);

        string parentName = newParent != null ? newParent.name : "Scene Root";
        Log(nameof(InternalDetach), $"Detached '{child.name}' -> {parentName}");
    }

    public void DeactivateLauncher()
    {
        if (launcherObj != null) launcherObj.SetActive(false);
    }
}
