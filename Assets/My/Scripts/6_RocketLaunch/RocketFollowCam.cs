using UnityEngine;

[RequireComponent(typeof(Camera))]
public class RocketFollowCam : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Follow Position Axes")]
    [SerializeField] private bool followY = true;
    [SerializeField] private bool followX = false;
    [SerializeField] private bool followZ = false;
    [SerializeField] private float gainY = 1f;

    [Header("Offsets")]
    [SerializeField] private Vector3 worldOffset = Vector3.zero;
    [SerializeField] private float lookUpOffset = 0f;

    [Header("Upright in View")]
    [SerializeField] private bool alignCameraUpWithTargetUp = true;
    [SerializeField] private Transform customUpSource;

    [Header("Smoothing")]
    [SerializeField] private bool smooth = true;
    [SerializeField, Range(0.1f, 20f)] private float smoothPosSpeed = 6f;
    [SerializeField, Range(0.1f, 20f)] private float smoothRotSpeed = 6f;

    [Header("Recentering")]
    [SerializeField, Range(0f, 1f)] private float targetViewportX = 0.5f;
    [SerializeField, Range(0f, 1.5f)] private float recenterStrength = 1.0f;

    [Header("Final Roll Animation")]
    [Tooltip("T+12:14 이후 카메라의 roll(Z)이 목표 각도로 천천히 변화")]
    [SerializeField] private float rollStartTime = 12f * 60f + 14f;
    [SerializeField] private float targetRollDeg = 90f;
    [SerializeField] private float rollLerpSpeed = 0.2f; // 낮을수록 천천히

    private Camera _cam;
    private Vector3 _baseCamPos;
    private Quaternion _baseCamRot;
    private Vector3 _baseTargetPos;

    private void Start()
    {
        _cam = GetComponent<Camera>();
        if (!target)
        {
            Debug.LogWarning("[RocketFollowCam] Target not assigned.");
            enabled = false;
            return;
        }

        _baseCamPos = transform.position;
        _baseCamRot = transform.rotation;
        _baseTargetPos = target.position;
    }

    private void LateUpdate()
    {
        if (!target) return;

        // --- 1) 위치 계산 (기본 Y 따라가기) ---
        Vector3 delta = target.position - _baseTargetPos;
        Vector3 tracked = new Vector3(
            followX ? delta.x : 0f,
            followY ? delta.y * gainY : 0f,
            followZ ? delta.z : 0f
        );
        Vector3 desiredPos = _baseCamPos + tracked + worldOffset;

        // --- 2) 회전 계산 (타깃 바라보기) ---
        Vector3 lookPoint = target.position + new Vector3(0f, lookUpOffset, 0f);
        Vector3 upRef = (customUpSource ? customUpSource.up : target.up);
        Quaternion desiredRot = Quaternion.LookRotation(lookPoint - desiredPos,
                                                        alignCameraUpWithTargetUp ? upRef : Vector3.up);

        // --- 3) 수평 리센터링 ---
        Vector3 fwd = desiredRot * Vector3.forward;
        float dist = Vector3.Project(lookPoint - desiredPos, fwd).magnitude;
        float halfHeight = dist * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfWidth = halfHeight * _cam.aspect;
        Vector3 vp = _cam.WorldToViewportPoint(lookPoint);
        float errX = vp.x - targetViewportX;
        float worldShift = -errX * (2f * halfWidth) * recenterStrength;
        Vector3 rightDesired = desiredRot * Vector3.right;
        desiredPos += rightDesired * worldShift;
        
        // --- 4) 카메라 Z-roll 보간 (T+12:14 이후) ---
        float tPlus = CountController.Instance ? CountController.Instance.TPlusSeconds : 0f;
        if (tPlus >= rollStartTime)
        {   
            // 현재 회전에서 Euler Z만 목표 각도로 천천히 이동
            Vector3 e = desiredRot.eulerAngles;
            float currentZ = NormalizeAngle(e.z);
            float nextZ = Mathf.LerpAngle(currentZ, targetRollDeg, rollLerpSpeed * Time.deltaTime);
            e.z = nextZ;
            desiredRot = Quaternion.Euler(e);
        }

        // --- 5) 스무딩 적용 ---
        if (smooth)
        {
            float tp = Mathf.Clamp01(smoothPosSpeed * Time.deltaTime);
            float tr = Mathf.Clamp01(smoothRotSpeed * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desiredPos, tp);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, tr);
        }
        else
        {
            transform.position = desiredPos;
            transform.rotation = desiredRot;
        }
    }

    private static float NormalizeAngle(float deg)
    {
        deg = Mathf.Repeat(deg + 180f, 360f) - 180f;
        return deg;
    }
}
