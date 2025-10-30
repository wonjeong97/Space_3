using TMPro;
using UnityEngine;

public class SlopeController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI textSlope;

    [Header("Range & Start")]
    [SerializeField] private float minDeg = 0f;
    [SerializeField] private float maxDeg = 90f;
    [SerializeField] private float startDeg = 90f;

    [Header("Input Sensitivity")]
    [SerializeField] private float stepPerPress = 1f;
    [SerializeField] private float stepWhileHeldPerSec = 30f;

    [Header("Display")]
    [SerializeField] private int decimals = 1;

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
    [SerializeField] private float inputEnableAtSeconds = 51f;

    // ===== 바늘 이미지 회전 표시 =====
    [Header("Slope Pointer (Image)")]
    [Tooltip("기울기 지침 이미지의 RectTransform (인스펙터에 할당)")]
    [SerializeField] private RectTransform imageSlopePointer;

    [Tooltip("SLP=minDeg 일 때의 바늘 Z 회전(도)")]
    [SerializeField] private float pointerMinZ = 0f;

    [Tooltip("SLP=maxDeg 일 때의 바늘 Z 회전(도)")]
    [SerializeField] private float pointerMaxZ = 90f;

    [Tooltip("바늘 회전을 보간할지 여부")]
    [SerializeField] private bool smoothPointer = true;

    [Tooltip("바늘 회전 보간 속도")]
    [SerializeField] private float pointerLerpSpeed = 10f;
    // =========================================

    // ===== 외부 아날로그 입력(Arduino THROTTLE) =====
    [Header("External Analog Input")]
    [SerializeField] private bool useExternalAnalog = true;      // 켜면 외부값으로 SLP를 매 프레임 세팅
    [SerializeField] private int analogMin = 0;                  // 입력 최소값
    [SerializeField] private int analogMax = 1000;               // 입력 최대값
    [SerializeField] private float slopeAtAnalogMin = 90f;       // 입력=analogMin -> SLP
    [SerializeField] private float slopeAtAnalogMax = 49.5f;     // 입력=analogMax -> SLP
    [SerializeField] private float analogEpsilon = 0.05f;        // 불필요한 미세 업데이트 방지
    
    [Header("Throttle Stream Control")]
    [SerializeField] private bool controlThrottleStream = true; // 외부 스트림 제어 on/off
    
    // 내부 상태 플래그
    private bool _throttleOnSent;   // THROTTLE ON을 한 번만 보냄
    private bool _throttleOffSent;  // 잠금 후 OFF를 한 번만 보냄
    private bool _lastLocked;       // 이전 프레임의 잠금 상태
    
    public float CurrentSlopeDeg { get; private set; }
    public bool IsInputLocked => _inputLocked;

    private bool _inputLocked;
    
    private void Start()
    {
        CurrentSlopeDeg = Mathf.Clamp(startDeg, minDeg, maxDeg);
        _inputLocked = false;
        
        // 전이 감지 초기화
        _lastLocked = _inputLocked;
        _throttleOnSent = false;
        _throttleOffSent = false;

        UpdateLabel();                // 텍스트는 필요 없으면 textSlope 비워두면 됨
        ApplyRocketRotationImmediate();
        ApplyPointerRotationImmediate();
    }

    private void Update()
    {
        // 1) 카운트다운 중이면 리턴
        if (CountController.Instance)
        {
            if (CountController.Instance.IsCountingDown) return;
        }

        // 2) T+ 제한 이전에는 입력 받지 않음
        if (CountController.Instance)
        {
            if (CountController.Instance.TPlusSeconds < inputEnableAtSeconds) return;

            // 2-1) T+ inputEnableAtSeconds 도달 시점에 THROTTLE ON을 딱 한 번 전송
            if (controlThrottleStream && useExternalAnalog && !_throttleOnSent)
            {
                ArduinoInputManager.Instance?.Send("THROTTLE ON");
                _throttleOnSent = true;
            }
        }

        // ===== 외부 아날로그 입력으로 SLP 갱신 =====
        if (useExternalAnalog)
        {
            int analogValue = 0;
            bool hasValue = false;

            if (ArduinoInputManager.Instance != null)
            {
                analogValue = ArduinoInputManager.Instance.LastThrottleValue;
                hasValue = true;
            }

            if (hasValue)
            {
                float t = Mathf.InverseLerp(analogMin, analogMax, analogValue); // 0..1
                float mapped = Mathf.Lerp(slopeAtAnalogMin, slopeAtAnalogMax, t);

                if (Mathf.Abs(mapped - CurrentSlopeDeg) > analogEpsilon)
                {
                    CurrentSlopeDeg = Mathf.Clamp(mapped, minDeg, maxDeg);
                    UpdateLabel();

                    if (smoothRotation) LerpRocketRotationToTarget();
                    else ApplyRocketRotationImmediate();

                    if (smoothPointer)  LerpPointerToTarget();
                    else ApplyPointerRotationImmediate();
                }
            }
        }

        // 3) 잠금 상태면 입력 무시 -> 보간만 수행
        if (_inputLocked)
        {
            if (smoothRotation) LerpRocketRotationToTarget();
            if (smoothPointer)  LerpPointerToTarget();
        }
        else
        {
            bool changed = false;

            // 단발 입력
            if (Input.GetKeyDown(KeyCode.UpArrow))   changed |= TryApplyDelta(stepPerPress);
            if (Input.GetKeyDown(KeyCode.DownArrow)) changed |= TryApplyDelta(-stepPerPress);

            // 홀드 입력
            if (Input.GetKey(KeyCode.UpArrow))   changed |= TryApplyDelta(stepWhileHeldPerSec * Time.deltaTime);
            if (Input.GetKey(KeyCode.DownArrow)) changed |= TryApplyDelta(-stepWhileHeldPerSec * Time.deltaTime);

            // 변경 반영
            if (changed)
            {
                UpdateLabel();

                if (smoothRotation) LerpRocketRotationToTarget();
                else ApplyRocketRotationImmediate();

                if (smoothPointer)  LerpPointerToTarget();
                else ApplyPointerRotationImmediate();
            }
            else
            {
                if (smoothRotation) LerpRocketRotationToTarget();
                if (smoothPointer)  LerpPointerToTarget();
            }
        }

        // 4) 목표각 자동 잠금
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

                    if (smoothPointer)  LerpPointerToTarget();
                    else ApplyPointerRotationImmediate();
                }
                _inputLocked = true;
            }
        }

        // 5) 잠금 전이 감지 -> 잠기는 순간 THROTTLE OFF를 1회 전송
        if (controlThrottleStream && useExternalAnalog)
        {
            if (!_lastLocked && _inputLocked && !_throttleOffSent)
            {
                ArduinoInputManager.Instance?.Send("THROTTLE OFF");
                _throttleOffSent = true;
            }
        }
        _lastLocked = _inputLocked;
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

    // -> "SLP n º" 표기 (옵션)
    private void UpdateLabel()
    {
        if (!textSlope) return;
        textSlope.text = $"SLP {CurrentSlopeDeg.ToString($"F{Mathf.Max(0, decimals)}")} º";
    }

    // -> 로켓 회전 목표값(월드/로컬 X). rotX = 90 - SLP
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

    // ===== 바늘 회전 관련 =====

    // -> 현재 SLP를 바늘 Z 회전으로 선형 매핑
    private float GetPointerTargetZ()
    {
        float t = Mathf.InverseLerp(minDeg, maxDeg, CurrentSlopeDeg);     // 0..1
        float z = Mathf.Lerp(pointerMinZ, pointerMaxZ, t);               // minZ..maxZ
        return z;
    }

    // -> 즉시 바늘 회전 적용
    private void ApplyPointerRotationImmediate()
    {
        if (!imageSlopePointer) return;
        Vector3 euler = imageSlopePointer.localEulerAngles;
        euler.z = GetPointerTargetZ();
        imageSlopePointer.localEulerAngles = euler;
    }

    // -> 보간하여 바늘 회전 적용
    private void LerpPointerToTarget()
    {
        if (!imageSlopePointer) return;
        Vector3 euler = imageSlopePointer.localEulerAngles;
        float targetZ = GetPointerTargetZ();
        float newZ = Mathf.LerpAngle(euler.z, targetZ, Mathf.Clamp01(pointerLerpSpeed * Time.deltaTime));
        euler.z = newZ;
        imageSlopePointer.localEulerAngles = euler;
    }
    // ========================

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

            if (smoothPointer)  LerpPointerToTarget();
            else ApplyPointerRotationImmediate();
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

            if (smoothPointer)  LerpPointerToTarget();
            else ApplyPointerRotationImmediate();
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
    
    public void EnableExternalAnalog(bool on, bool sendThrottleOffWhenDisabling = true)
    {
        useExternalAnalog = on;

        // 외부 입력을 끌 때 스트림도 정리(옵션)
        if (!on && controlThrottleStream && sendThrottleOffWhenDisabling)
        {
            if (_throttleOnSent && !_throttleOffSent)
            {
                ArduinoInputManager.Instance?.Send("THROTTLE OFF");
                _throttleOffSent = true;
            }
        }
    }
}
