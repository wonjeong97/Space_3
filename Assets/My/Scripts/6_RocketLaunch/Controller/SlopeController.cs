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
    [SerializeField] private bool enableRotateX = false;
    [SerializeField] private bool enableRotateY = true;
    [SerializeField] private Transform rocketTransform;
    [SerializeField] private bool useLocalRotation = true;
    [SerializeField] private bool smoothRotation = false;
    [SerializeField] private float rotationLerpSpeed = 10f;
    [SerializeField] private float rotateRatio;

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
    
    [Header("Rocket FollowCam Link")]
    [SerializeField] private RocketFollowCam rocketFollowCam;
    [SerializeField] private float camOffsetPerDeg = 1f;
    // =========================================

    // ===== 외부 아날로그 입력(Arduino THROTTLE) =====
    [Header("External Analog Input")]
    [SerializeField] private bool useExternalAnalog = true;
    [SerializeField] private int analogMin = 0;
    [SerializeField] private int analogMax = 1000;
    [SerializeField] private float slopeAtAnalogMin = 90f;
    [SerializeField] private float slopeAtAnalogMax = 49.5f;
    [SerializeField] private float analogEpsilon = 0.05f;

    [Header("Throttle Stream Control")]
    [SerializeField] private bool controlThrottleStream = true;

    // 내부 상태
    private bool _throttleOnSent;
    private bool _throttleOffSent;
    private bool _lastLocked;
    private Vector3 _rocketBaseEuler;
    private float _lastSlopeForCamera;

    public float CurrentSlopeDeg { get; private set; }
    public bool IsInputLocked => _inputLocked;
    private bool _inputLocked;

    // -> 시작 상태 초기화
    private void Start()
    {
        CurrentSlopeDeg = Mathf.Clamp(startDeg, minDeg, maxDeg);
        _inputLocked = false;

        _lastLocked = _inputLocked;
        _throttleOnSent = false;
        _throttleOffSent = false;

        if (rocketTransform)
            _rocketBaseEuler = useLocalRotation ? rocketTransform.localEulerAngles : rocketTransform.eulerAngles;

        _lastSlopeForCamera = CurrentSlopeDeg;   // 카메라 연동 기준값 초기화

        UpdateLabel();
        ApplyRocketRotationImmediate();
        ApplyPointerRotationImmediate();
    }

    // -> 매 프레임 입력과 회전/포인터 보간 처리
    private void Update()
    {
        // 1) 카운트다운 중이면 입력 차단
        if (CountController.Instance != null && CountController.Instance.IsCountingDown)
            return;

        // 2) 게이트: T+ inputEnableAtSeconds 이전에는 입력 차단
        if (CountController.Instance != null && CountController.Instance.TPlusSeconds < inputEnableAtSeconds)
            return;

        // 3) 외부 입력 실제 활성 여부: 게이트 통과 후에만 고려
        bool externalActive = useExternalAnalog && ArduinoInputManager.Instance != null && ArduinoInputManager.Instance.SerialPort.IsOpen;

        // 3-1) 외부 입력이 활성일 때만 THROTTLE ON 1회 전송
        if (externalActive && controlThrottleStream && !_throttleOnSent)
        {
            ArduinoInputManager.Instance?.Send("THROTTLE ON");
            _throttleOnSent = true;
        }

        // 4) 외부 아날로그 입력 처리 또는 키보드 처리
        if (_inputLocked)
        {
            if (smoothRotation) LerpRocketRotationToTarget();
            if (smoothPointer)  LerpPointerToTarget();
        }
        else
        {
            bool changed = false;

            if (externalActive)
            {
                // 외부 아날로그 입력으로 갱신
                int analogValue = 0;
                bool hasValue = false;

                ArduinoInputManager mgr = ArduinoInputManager.Instance;
                if (mgr != null)
                {
                    analogValue = mgr.LastThrottleValue;
                    hasValue = true;
                }

                if (hasValue)
                {
                    float t = Mathf.InverseLerp(analogMin, analogMax, analogValue); // 0..1
                    float mapped = Mathf.Lerp(slopeAtAnalogMin, slopeAtAnalogMax, t);

                    if (Mathf.Abs(mapped - CurrentSlopeDeg) > analogEpsilon)
                    {
                        CurrentSlopeDeg = Mathf.Clamp(mapped, minDeg, maxDeg);
                        changed = true;
                    }
                }
            }
            else
            {
                if (LaunchManager.Instance)
                {
                    LaunchManager.Instance.CanInput = true;
                    LaunchManager.Instance.InputReceived = false;
                }
                // 아두이노가 없으면 키보드 입력 사용
                if (Input.GetKeyDown(KeyCode.Alpha1))   changed |= TryApplyDelta(-stepPerPress);
                if (Input.GetKeyDown(KeyCode.Alpha2)) changed |= TryApplyDelta(stepPerPress);

                if (Input.GetKey(KeyCode.Alpha1))   changed |= TryApplyDelta(stepWhileHeldPerSec * Time.deltaTime);
                if (Input.GetKey(KeyCode.Alpha2)) changed |= TryApplyDelta(-stepWhileHeldPerSec * Time.deltaTime);
            }

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

        // 5) 목표각 자동 잠금
        if (autoLockOnTarget && !_inputLocked && LaunchManager.Instance)
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
                LaunchManager.Instance.PauseInactivityTimer();
            }
        }

        // 6) 잠금 전이 시 THROTTLE OFF 1회 전송(외부 입력일 때만)
        if (controlThrottleStream && externalActive)
        {
            if (!_lastLocked && _inputLocked && !_throttleOffSent)
            {
                ArduinoInputManager.Instance?.Send("THROTTLE OFF");
                _throttleOffSent = true;
            }
        }
        _lastLocked = _inputLocked;
        
        // === 기울기 변화량을 카메라 offset.y에 반영 ===
        if (rocketFollowCam != null)
        {
            float delta = CurrentSlopeDeg - _lastSlopeForCamera;
            if (!Mathf.Approximately(delta, 0f))
            {
                // 기울기 변화량(증가/감소)에 비례해서 offset.y를 변경
                rocketFollowCam.SubOffsetY(delta * camOffsetPerDeg);
                _lastSlopeForCamera = CurrentSlopeDeg;
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

    // -> "SLP n º" 표기
    private void UpdateLabel()
    {
        if (!textSlope) return;
        textSlope.text = $"SLP {CurrentSlopeDeg.ToString($"F{Mathf.Max(0, decimals)}")} º";
    }

    // -> 목표 회전값 계산
    private Vector3 GetTargetRocketEuler()
    {
        float targetAngle = Mathf.Clamp(90f - CurrentSlopeDeg, 0f, 90f);
        Vector3 euler = _rocketBaseEuler;

        if (enableRotateX) euler.x = _rocketBaseEuler.x + targetAngle;
        if (enableRotateY) euler.y = _rocketBaseEuler.y + targetAngle;

        return euler;
    }

    // -> 즉시 로켓 회전 적용
    private void ApplyRocketRotationImmediate()
    {
        if (!rocketTransform) return;
        if (!enableRotateX && !enableRotateY) return;

        Vector3 euler = GetTargetRocketEuler();
        if (useLocalRotation) rocketTransform.localEulerAngles = euler;
        else rocketTransform.eulerAngles = euler;
    }

    // -> 보간하여 로켓 회전 적용
    private void LerpRocketRotationToTarget()
    {
        if (!rocketTransform) return;
        if (!enableRotateX && !enableRotateY) return;

        Vector3 current = useLocalRotation ? rocketTransform.localEulerAngles : rocketTransform.eulerAngles;
        float targetAngle = Mathf.Clamp(90f - CurrentSlopeDeg, 0f, 90f);

        float dstX = enableRotateX ? (_rocketBaseEuler.x + targetAngle * rotateRatio) : current.x;
        float dstY = enableRotateY ? (_rocketBaseEuler.y + targetAngle) : current.y;

        float t = Mathf.Clamp01(rotationLerpSpeed * Time.deltaTime);
        current.x = Mathf.LerpAngle(current.x, dstX, t);
        current.y = Mathf.LerpAngle(current.y, dstY, t);

        if (useLocalRotation) rocketTransform.localEulerAngles = current;
        else rocketTransform.eulerAngles = current;
    }

    // ===== 바늘 회전 관련 =====

    // -> 현재 SLP를 바늘 Z 회전으로 선형 매핑
    private float GetPointerTargetZ()
    {
        float t = Mathf.InverseLerp(minDeg, maxDeg, CurrentSlopeDeg);
        float z = Mathf.Lerp(pointerMinZ, pointerMaxZ, t);
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

    // -> 외부에서 각도 세팅
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

    // -> 입력 잠금
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

    // -> 입력 잠금 해제
    public void UnlockInput()
    {
        _inputLocked = false;
    }

    // -> 자동 잠금 파라미터 설정
    public void ConfigureAutoLock(float targetDeg, float toleranceDeg, bool enable, bool snapOnLock = true)
    {
        autoLockTargetDeg = targetDeg;
        autoLockToleranceDeg = Mathf.Max(0f, toleranceDeg);
        autoLockOnTarget = enable;
        snapToTargetOnLock = snapOnLock;
    }

    // -> 외부 아날로그 사용 on/off
    public void EnableExternalAnalog(bool on, bool sendThrottleOffWhenDisabling = true)
    {
        useExternalAnalog = on;

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
