using System.Collections;
using UnityEngine;

public sealed class RocketFollowCam : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Offset")]
    [SerializeField] private bool useInitialOffset = true;
    [SerializeField] private Vector3 offset = new Vector3(0f, 10f, -20f);

    [Header("Follow Axes")]
    [SerializeField] private bool followX = false;
    [SerializeField] private bool followY = true;
    [SerializeField] private bool followZ = false;

    [Header("Smoothing")]
    [Range(0f, 1f)]
    [SerializeField] private float smoothTime = 0.15f;

    [Header("Look At")]
    [SerializeField] private bool lookAtTarget = true;
    [SerializeField] private Vector3 lookAtOffset = Vector3.zero;

    private Vector3 _velocity;
    private Coroutine _lookAtOffsetRoutine; // LookAtOffset 보간용

    public Vector3 Offset
    {
        get => offset;
        set => offset = value;
    }

    public Vector3 LookAtOffset
    {
        get => lookAtOffset;
        set => lookAtOffset = value;
    }

    private void Reset()
    {
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
    
    ///<Summary>offset의 Y 값에 delta를 뺀다.</Summary>
    public void SubOffsetY(float deltaY)
    {
        offset.y -= deltaY;
        LaunchManager.Instance.VerticalCamera.fieldOfView += deltaY / 3;
    }


    ///<Summary>런타임에 타겟을 변경하고, 필요시 offset을 재계산한다.</Summary>
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

    ///<Summary>LookAtOffset을 duration 동안 현재 값에서 targetOffset까지 선형 보간한다.</Summary>
    public void LerpLookAtOffset(Vector3 targetOffset, float duration)
    {
        if (duration <= 0f)
        {
            lookAtOffset = targetOffset;
            return;
        }

        if (_lookAtOffsetRoutine != null)
        {
            StopCoroutine(_lookAtOffsetRoutine);
            _lookAtOffsetRoutine = null;
        }

        _lookAtOffsetRoutine = StartCoroutine(CoLerpLookAtOffset(targetOffset, duration));
    }

    // LookAtOffset 보간 코루틴
    private IEnumerator CoLerpLookAtOffset(Vector3 targetOffset, float duration)
    {
        Vector3 start = lookAtOffset;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            lookAtOffset = Vector3.Lerp(start, targetOffset, t);
            yield return null;
        }

        lookAtOffset = targetOffset;
        _lookAtOffsetRoutine = null;
    }

    private void Log(string method, string msg)
    {
        Debug.Log($"[RocketFollowCam] {method}-> {msg}");
    }

    private void LogWarn(string method, string msg)
    {
        Debug.LogWarning($"[RocketFollowCam] {method}-> {msg}");
    }

    private void LogError(string method, string msg)
    {
        Debug.LogError($"[RocketFollowCam] {method}-> {msg}");
    }
}
