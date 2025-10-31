using UnityEngine;

[RequireComponent(typeof(Camera))]
public class RocketFollowCam : MonoBehaviour
{
    public enum RotationMode
    {
        LookAtWithTargetUp,   // 타겟을 바라보되 up=target.up으로 롤을 따라감
        CopyTargetWithOffset  // 타겟 회전을 기준으로 초기 상대 회전 오프셋 유지
    }

    [Header("Follow target")]
    [SerializeField] private Transform target;           // 따라갈 단일 오브젝트
    [SerializeField, Tooltip("카메라가 target을 따라갈 때 위치 스무딩(초)")]
    private float positionSmoothTime = 0.2f;
    [SerializeField, Tooltip("카메라가 target 회전을 따라갈 때 회전 스무딩(초). 0이면 즉시 회전")]
    private float rotationSmoothTime = 0.12f;

    [Header("Rotation mode")]
    [SerializeField] private RotationMode rotationMode = RotationMode.LookAtWithTargetUp;
    [SerializeField, Tooltip("LookAtWithTargetUp 모드일 때, 타겟을 바라볼 지점의 오프셋(타겟 로컬 좌표)")]
    private Vector3 lookAtLocalOffset = Vector3.zero;

    [Header("Distance/offset")]
    [SerializeField, Tooltip("타겟 로컬 좌표계에서의 초기 위치 오프셋. Start에서 자동 계산됨")]
    private Vector3 initialLocalOffset; // 디자이너가 고정값을 직접 넣어도 됨
    [SerializeField, Tooltip("초기 상대 회전 오프셋(타겟 기준). CopyTargetWithOffset에서 사용")]
    private Quaternion initialRotOffset = Quaternion.identity;
    
    [SerializeField] private bool bFollowTarget = false;

    private Camera _cam;
    private Vector3 _posVelocity;   // SmoothDamp 내부 속도

    // -> 초기화: 카메라 참조 확보 및 오프셋 계산
    private void Start()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null || target == null) return;

        // 초기 위치 오프셋(타겟 로컬) 계산
        initialLocalOffset = target.InverseTransformPoint(transform.position);

        // 초기 회전 오프셋(타겟 기준) 계산
        initialRotOffset = Quaternion.Inverse(target.rotation) * transform.rotation;
    }

    // -> LateUpdate: 타겟 로컬 오프셋을 기준으로 위치 추종 -> 선택한 회전 모드로 회전 추종
    private void LateUpdate()
    {
        if (!_cam || !target || !bFollowTarget) return;

        // 1) 위치 추종: 타겟 로컬 오프셋을 월드로 변환하여 스무딩 이동
        Vector3 desiredPos = target.TransformPoint(initialLocalOffset);
        transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref _posVelocity, positionSmoothTime);

        // 2) 회전 추종: 모드에 따라 목표 회전 계산
        Quaternion targetRot = transform.rotation;

        if (rotationMode == RotationMode.CopyTargetWithOffset)
        {
            // 타겟 회전 * 초기 상대 회전 오프셋
            targetRot = target.rotation * initialRotOffset;
        }
        else // RotationMode.LookAtWithTargetUp
        {
            // 타겟을 바라보되, up 벡터로 target.up을 사용해 롤을 따라감
            Vector3 lookPoint = target.TransformPoint(lookAtLocalOffset);
            Vector3 fwd = (lookPoint - transform.position);
            if (fwd.sqrMagnitude > 1e-6f)
            {
                targetRot = Quaternion.LookRotation(fwd, target.up);
            }
        }

        ApplyRotation(targetRot);
    }

    // -> 회전 적용(스무딩 또는 즉시)
    private void ApplyRotation(Quaternion targetRot)
    {
        if (rotationSmoothTime <= 0f)
        {
            transform.rotation = targetRot;
            return;
        }

        float t = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, rotationSmoothTime));
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);
    }

    // -> 현재 배치에서 오프셋을 다시 캡처(에디터 우클릭 메뉴)
    [ContextMenu("Re-capture Offsets From Current Pose")]
    private void ReCaptureOffsets()
    {
        if (target == null) return;
        initialLocalOffset = target.InverseTransformPoint(transform.position);
        initialRotOffset   = Quaternion.Inverse(target.rotation) * transform.rotation;
    }

    // -> 타겟 기준으로 한 발짝 뒤로/앞으로 이동하는 유틸(디자인 편의)
    [ContextMenu("Nudge Backward (Local Z +1)")]
    private void NudgeBackward()
    {
        if (target == null) return;
        initialLocalOffset += new Vector3(0f, 0f, 1f);
    }

    [ContextMenu("Nudge Forward (Local Z -1)")]
    private void NudgeForward()
    {
        if (target == null) return;
        initialLocalOffset += new Vector3(0f, 0f, -1f);
    }
}
