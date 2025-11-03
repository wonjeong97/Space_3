using UnityEngine;

/// <summary> 프로젝트 전역 공통 로깅 유틸리티. </summary>
public static class LogUtil
{
    public static void Log(string className, string method, string msg)
        => Debug.Log($"[{className}] {method}-> {msg}");

    public static void LogWarn(string className, string method, string msg)
        => Debug.LogWarning($"[{className}] {method}-> {msg}");

    public static void LogError(string className, string method, string msg)
        => Debug.LogError($"[{className}] {method}-> {msg}");
}