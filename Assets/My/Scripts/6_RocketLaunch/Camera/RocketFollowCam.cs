using UnityEngine;

/// <summary>
/// 로켓(또는 지정한 타겟)이 Y축으로 올라갈 때 카메라가 일정 거리(offset)를 유지하며 따라가는 카메라 컨트롤러.
/// - 타겟의 위치 + 초기 offset을 기준으로 위치를 갱신.
/// - followX/Y/Z 플래그로 어떤 축을 따라갈지 선택 가능.
/// </summary>
public sealed class RocketFollowCam : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;              // 따라갈 로켓

    [Header("Offset")]
    [Tooltip("시작 시 타겟과의 현재 거리를 자동으로 offset으로 사용할지 여부")]
    [SerializeField] private bool useInitialOffset = true;

    [Tooltip("타겟 기준 카메라 위치 오프셋(직접 지정하고 싶을 때 사용)")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 10f, -20f);

    [Header("Follow Axes")]
    [Tooltip("X축 이동을 타겟에 맞출지 여부")]
    [SerializeField] private bool followX = false;

    [Tooltip("Y축 이동을 타겟에 맞출지 여부 (로켓 상승을 따라감)")]
    [SerializeField] private bool followY = true;

    [Tooltip("Z축 이동을 타겟에 맞출지 여부")]
    [SerializeField] private bool followZ = false;

    [Header("Smoothing")]
    [Tooltip("위치 보정에 사용할 SmoothDamp 시간. 0이면 즉시 위치로 스냅")]
    [Range(0f, 1f)]
    [SerializeField] private float smoothTime = 0.15f;

    [Header("Look At")]
    [Tooltip("항상 타겟을 바라볼지 여부")]
    [SerializeField] private bool lookAtTarget = true;

    [Tooltip("타겟을 볼 때 추가로 줄 오프셋 (예: 로켓의 조금 위를 보고 싶을 때)")]
    [SerializeField] private Vector3 lookAtOffset = Vector3.zero;

    private Vector3 _velocity;  // SmoothDamp 내부용

    // ============================
    // Unity lifecycle
    // ============================
    private void Reset()
    {
        // 에디터에서 컴포넌트 추가 시 기본값 설정
        if (target == null)
        {
            // 같은 오브젝트에 로켓이 붙어있을 일은 거의 없으니 기본은 null 유지
        }

        offset = new Vector3(0f, 10f, -20f);
        followX = false;
        followY = true;
        followZ = false;
    }

    private void Awake()
    {
        if (target == null)
        {
            LogWarn(nameof(Awake), "target이 지정되지 않았습니다. 인스펙터에서 로켓 Transform을 연결해 주세요.");
        }
    }

    private void Start()
    {
        if (target == null) return;

        // 시작 시 현재 거리로 offset 설정
        if (useInitialOffset)
        {
            offset = transform.position - target.position;
            Log(nameof(Start), $"초기 offset 설정: {offset}");
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 currentPos = transform.position;
        Vector3 desiredPos = target.position + offset;

        // 축별로 따라갈지 말지 선택
        if (!followX) desiredPos.x = currentPos.x;
        if (!followY) desiredPos.y = currentPos.y;
        if (!followZ) desiredPos.z = currentPos.z;

        if (smoothTime <= 0f)
        {
            transform.position = desiredPos;
        }
        else
        {
            transform.position = Vector3.SmoothDamp(currentPos, desiredPos, ref _velocity, smoothTime);
        }

        if (lookAtTarget)
        {
            Vector3 lookTarget = target.position + lookAtOffset;
            transform.LookAt(lookTarget);
        }
    }

    // ============================
    // Public API
    // ============================
    /// <summary>
    /// 런타임에 타겟을 바꾸고 싶을 때 사용.
    /// </summary>
    public void SetTarget(Transform newTarget, bool recalcOffset = true)
    {
        target = newTarget;

        if (target == null)
        {
            LogWarn(nameof(SetTarget), "새 타겟이 null 입니다.");
            return;
        }

        if (recalcOffset)
        {
            offset = transform.position - target.position;
            Log(nameof(SetTarget), $"타겟 변경 및 offset 재계산: {offset}");
        }
        else
        {
            Log(nameof(SetTarget), "타겟 변경 (offset 유지)");
        }
    }

    // ============================
    // Logging helpers
    // ============================
    private static void Log(string method, string msg)
    {
        Debug.Log($"[RocketFollowCam] {method}-> {msg}");
    }

    private static void LogWarn(string method, string msg)
    {
        Debug.LogWarning($"[RocketFollowCam] {method}-> {msg}");
    }

    private static void LogError(string method, string msg)
    {
        Debug.LogError($"[RocketFollowCam] {method}-> {msg}");
    }
}
