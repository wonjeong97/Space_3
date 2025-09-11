using System;
using System.Threading;
using UnityEngine;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    private Settings jsonSetting;
    private CancellationTokenSource cts;
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            cts = new CancellationTokenSource();
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        try
        {
            if (UICreator.Instance == null)
            {
                Debug.LogError("[UIManager] UI_Creator is null. Place UI_Creator in the scene.");
                return;
            }

            if (JsonLoader.Instance.settings == null)
            {
                Debug.LogError("[UIManager] Settings are not loaded yet.");
                return;
            }

            jsonSetting = JsonLoader.Instance.settings;
            InitUI();
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning("[UIManager] UI initialization canceled.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[UIManager] UI initialization failed: {e}");
        }
    }

    private void OnDestroy()
    {
        try
        {
            cts?.Cancel();
        }
        catch
        {
        }

        cts?.Dispose();
        cts = null;

        // Ensure creator cleans up any remaining instances and cached assets   
        if (UICreator.Instance != null)
        {
            UICreator.Instance.DestroyAllTrackedInstances();
        }
    }

    /// <summary>초기 UI(캔버스/배경/아이들 페이지) 생성 및 연결</summary>
    private void InitUI(CancellationToken token = default)
    {
        CancellationToken ct = UIUtility.MergeTokens(cts.Token, token); // 내부 CTS와 외부 토큰 병합
        try
        {   
            
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning("[UIManager] InitUI canceled."); // 취소 로그
            throw; // 취소 전파
        }
        catch (Exception e)
        {
            Debug.LogError($"[UIManager] InitUI failed: {e}"); // 예외 로그
            throw; // 상위에 알림
        }
    }

    /// <summary>동적으로 생성된 인스턴스를 모두 해제하고 초기 UI 재구성</summary>
    public void ClearAllDynamic()
    {
        UICreator.Instance.DestroyAllTrackedInstances();
        InitUI();
    }
}