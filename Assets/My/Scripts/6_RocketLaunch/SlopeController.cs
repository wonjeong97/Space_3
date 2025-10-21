using TMPro;
using UnityEngine;

/// <summary>
/// 기울기(Slope) 컨트롤러
/// - 시작값 90도
/// - 화살표 ↑/↓ 로 증가/감소
/// - TMP에 "SLP n º" 포맷
/// - CountController가 T+ 0:51 이전이면 입력 차단
/// - 목표각 도달 시 자동 잠금(옵션)
/// - SLP 변경 시 로켓 rotation.x = 90 - SLP 로 동기화
/// </summary>
public class SlopeController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI textSlope;

    [Header("Range & Start")]
    [SerializeField] private float minDeg = 0f;
    [SerializeField] private float maxDeg = 90f;
    [SerializeField] private float startDeg = 90f;

    [Header("Input Sensitivity")]
    [SerializeField] private float stepPerPress = 1f;           // 1회 키 입력 당 변화(도)
    [SerializeField] private float stepWhileHeldPerSec = 30f;   // 홀드 시 초당 변화(도/초)

    [Header("Display")]
    [SerializeField] private int decimals = 1;                   // 소수 1자리로 표시

    [Header("Auto Lock On Target")]
    [SerializeField] private bool autoLockOnTarget = true;
    [SerializeField] private float autoLockTargetDeg = 49.5f;
    [SerializeField] private float autoLockToleranceDeg = 0.2f;
    [SerializeField] private bool snapToTargetOnLock = true;

    [Header("Rocket Rotation Link")]
    [SerializeField] private Transform rocketTransform;
    [SerializeField] private bool useLocalRotation = true;
    [SerializeField] private bool smoothRotation = false;
    [SerializeField] private float rotationLerpSpeed = 10f;

    [Header("Enable Input Timing")]
    [Tooltip("이 시점(T+초) 이전에는 입력을 받지 않음")]
    [SerializeField] private float inputEnableAtSeconds = 51f;  // T+ 0:51

    public float CurrentSlopeDeg { get; private set; }
    public bool IsInputLocked => _inputLocked;

    private bool _inputLocked;

    private void Start()
    {
        CurrentSlopeDeg = Mathf.Clamp(startDeg, minDeg, maxDeg);
        _inputLocked = false;
        UpdateLabel();
        ApplyRocketRotationImmediate();
    }

    private void Update()
    {
        // 1) CountController가 없으면 자유 입력, 있으면 T+이고 inputEnableAtSeconds 도달 전까지 차단
        if (CountController.Instance)
        {
            if (CountController.Instance.IsCountingDown) return;                       // T- 차단
            if (CountController.Instance.TPlusSeconds < inputEnableAtSeconds) return;   // T+ 0:51 이전 차단
        }

        // 2) 잠금 상태면 입력 무시(부드러운 회전만 보간)
        if (_inputLocked)
        {
            if (smoothRotation) LerpRocketRotationToTarget();
            return;
        }

        bool changed = false;

        // 3) 단발 입력
        if (Input.GetKeyDown(KeyCode.UpArrow))   changed |= TryApplyDelta(stepPerPress);
        if (Input.GetKeyDown(KeyCode.DownArrow)) changed |= TryApplyDelta(-stepPerPress);

        // 4) 홀드 입력
        if (Input.GetKey(KeyCode.UpArrow))   changed |= TryApplyDelta(stepWhileHeldPerSec * Time.deltaTime);
        if (Input.GetKey(KeyCode.DownArrow)) changed |= TryApplyDelta(-stepWhileHeldPerSec * Time.deltaTime);

        // 5) 변경 반영
        if (changed)
        {
            UpdateLabel();
            if (smoothRotation) LerpRocketRotationToTarget();
            else ApplyRocketRotationImmediate();
        }
        else
        {
            if (smoothRotation) LerpRocketRotationToTarget();
        }

        // 6) 목표각 자동 잠금
        if (autoLockOnTarget && !_inputLocked)
        {
            if (Mathf.Abs(CurrentSlopeDeg - autoLockTargetDeg) <= Mathf.Max(0f, autoLockToleranceDeg))
            {
                if (snapToTargetOnLock)
                {
                    CurrentSlopeDeg = Mathf.Clamp(autoLockTargetDeg, minDeg, maxDeg);
                    UpdateLabel();
                    if (smoothRotation) LerpRocketRotationToTarget();
                    else ApplyRocketRotationImmediate();
                }
                _inputLocked = true;
            }
        }
    }

    // -> 변화량 적용
    private bool TryApplyDelta(float deltaDeg)
    {
        if (Mathf.Approximately(deltaDeg, 0f)) return false;
        float next = Mathf.Clamp(CurrentSlopeDeg + deltaDeg, minDeg, maxDeg);
        if (!Mathf.Approximately(next, CurrentSlopeDeg))
        {
            CurrentSlopeDeg = next;
            return true;
        }
        return false;
    }

    // -> "SLP n º" 표기 (소수 1자리)
    private void UpdateLabel()
    {
        if (!textSlope) return;
        textSlope.text = $"SLP {CurrentSlopeDeg.ToString($"F{Mathf.Max(0, decimals)}")} º";
    }

    // -> rotX = 90 - SLP (즉, SLP 90 -> rotX 0, SLP 0 -> rotX 90)
    private float GetTargetRocketEulerX()
    {
        float targetX = 90f - CurrentSlopeDeg;
        return Mathf.Clamp(targetX, 0f, 90f);
    }

    private void ApplyRocketRotationImmediate()
    {
        if (!rocketTransform) return;
        Vector3 euler = useLocalRotation ? rocketTransform.localEulerAngles : rocketTransform.eulerAngles;
        euler.x = GetTargetRocketEulerX();
        if (useLocalRotation) rocketTransform.localEulerAngles = euler;
        else rocketTransform.eulerAngles = euler;
    }

    private void LerpRocketRotationToTarget()
    {
        if (!rocketTransform) return;
        Vector3 euler = useLocalRotation ? rocketTransform.localEulerAngles : rocketTransform.eulerAngles;
        float targetX = GetTargetRocketEulerX();
        float newX = Mathf.LerpAngle(euler.x, targetX, Mathf.Clamp01(rotationLerpSpeed * Time.deltaTime));
        euler.x = newX;
        if (useLocalRotation) rocketTransform.localEulerAngles = euler;
        else rocketTransform.eulerAngles = euler;
    }

    public void SetSlopeDeg(float degrees, bool forceWhenLocked = false)
    {
        if (_inputLocked && !forceWhenLocked) return;
        float clamped = Mathf.Clamp(degrees, minDeg, maxDeg);
        if (!Mathf.Approximately(clamped, CurrentSlopeDeg))
        {
            CurrentSlopeDeg = clamped;
            UpdateLabel();
            if (smoothRotation) LerpRocketRotationToTarget();
            else ApplyRocketRotationImmediate();
        }
    }

    public void LockInput(bool snapToTarget = false)
    {
        _inputLocked = true;
        if (snapToTarget && autoLockOnTarget)
        {
            CurrentSlopeDeg = Mathf.Clamp(autoLockTargetDeg, minDeg, maxDeg);
            UpdateLabel();
            if (smoothRotation) LerpRocketRotationToTarget();
            else ApplyRocketRotationImmediate();
        }
    }

    public void UnlockInput()
    {
        _inputLocked = false;
    }

    public void ConfigureAutoLock(float targetDeg, float toleranceDeg, bool enable, bool snapOnLock = true)
    {
        autoLockTargetDeg = targetDeg;
        autoLockToleranceDeg = Mathf.Max(0f, toleranceDeg);
        autoLockOnTarget = enable;
        snapToTargetOnLock = snapOnLock;
    }
}
