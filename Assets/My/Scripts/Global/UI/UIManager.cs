using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    private Settings _jsonSetting;
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

    private async void Start()
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

            _jsonSetting = JsonLoader.Instance.settings;

            // 초기 UI 구성 (씬 파괴/수동 취소 모두 대응)
            await InitUIAsync(cts.Token);
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
            if (cts != null) cts.Cancel();
        }
        catch
        {
            // 무시
        }

        if (cts != null)
        {
            cts.Dispose();
            cts = null;
        }

        // 생성된 UI/캐시 정리
        if (UICreator.Instance != null)
        {
            UICreator.Instance.DestroyAllTrackedInstances();
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// 초기 UI(캔버스/배경/아이들 페이지) 생성 및 연결 (UniTask)
    /// 외부 토큰이 전달되면 내부 cts와 링크하여 둘 중 하나라도 취소되면 종료.
    /// </summary>
    private async UniTask InitUIAsync(CancellationToken token = default)
    {
        CancellationToken linked = cts.Token;
        CancellationTokenSource linkedCts = null;

        // 외부 토큰이 있으면 내부 cts와 링크
        if (token.CanBeCanceled)
        {
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, token);
            linked = linkedCts.Token;
        }

        try
        {
            // 여기서부터 실제 초기 UI 구성 로직을 넣으세요.
            // 예시: Addressables 로드, Canvas/이미지 생성, 폰트/비디오 준비 등
            linked.ThrowIfCancellationRequested();

            // 프레임 하나 양보 (필요 시)
            await UniTask.Yield(PlayerLoopTiming.Update, linked);

            // 예시) 생성 절차가 비동기라면 이런 식으로 진행
            // GameObject canvas1 = await UICreator.Instance.CreateCanvasAsync(jsonSetting.canvas1Setting, linked);
            // GameObject bg = await UICreator.Instance.CreateImageAsync(jsonSetting.backgroundSetting, canvas1, linked);
            // await FadeManager.Instance.FadeOutAsync(jsonSetting.fadeTime, false, linked);

            // TODO: 실제 프로젝트 로직으로 채우기
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning("[UIManager] InitUI canceled.");
            throw;
        }
        catch (Exception e)
        {
            Debug.LogError($"[UIManager] InitUI failed: {e}");
            throw;
        }
        finally
        {
            // 링크드 CTS 정리
            if (linkedCts != null) linkedCts.Dispose();
        }
    }

    /// <summary>
    /// 동적으로 생성된 인스턴스를 모두 해제하고 초기 UI 재구성
    /// </summary>
    public void ClearAllDynamic()
    {
        if (UICreator.Instance != null)
        {
            UICreator.Instance.DestroyAllTrackedInstances();
        }

        // 재초기화는 fire-and-forget로 돌리되, 내부에서 취소/예외는 자체 처리
        InitUIAsync(cts.Token).Forget();
    }

    /// <summary>
    /// 외부에서 강제 취소가 필요할 때 호출 (예: 씬 전환 직전)
    /// </summary>
    public void CancelAll()
    {
        if (cts != null && !cts.IsCancellationRequested)
        {
            cts.Cancel();
            cts.Dispose();
            cts = new CancellationTokenSource();
        }
    }
}
