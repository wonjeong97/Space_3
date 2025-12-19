using System;
using TMPro;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary> 키 사이 보간에 사용할 이징 종류 </summary>
public enum EasingMode
{
    SmoothStep,
    SmootherStep,
    EaseInCubic,
    EaseOutCubic,
    EaseInOutCubic
}

/// <summary> 선형 u(0..1)를 이징 u(0..1)로 변환 </summary>
public static class EasingUtil
{
    public static float Apply(float u, EasingMode mode)
    {
        u = Mathf.Clamp01(u);
        switch (mode)
        {
            case EasingMode.SmoothStep:     return u * u * (3f - 2f * u);
            case EasingMode.SmootherStep:   return u * u * u * (u * (6f * u - 15f) + 10f);
            case EasingMode.EaseInCubic:    return u * u * u;
            case EasingMode.EaseOutCubic:   { float x = 1f - u; return 1f - x * x * x; }
            case EasingMode.EaseInOutCubic: return (u < 0.5f) ? 4f*u*u*u : 1f - Mathf.Pow(-2f*u + 2f, 3f)*0.5f;
            default:                        return u;
        }
    }
}

public class CountController : MonoBehaviour
{   
    public static CountController Instance;
    
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI textCountdown;
    [Tooltip("체크포인트 안내 문구를 출력할 TMP(선택)")]
    [SerializeField] private TextMeshProUGUI textHint;

    [Header("Speed")]
    [SerializeField] private float deltaTimeSpeed = 5f;

    [Header("Checkpoint (T+ 0:51)")]
    [SerializeField] private SlopeController slope;      // 현재 SLP 확인/설정용
    [SerializeField] private float checkpointSeconds = 51f;   // 0:51
    [SerializeField] private float requiredSlopeDeg = 49.5f;  // 목표 SLP
    [SerializeField] private float slopeToleranceDeg = 0.2f;  // 허용 오차(±)
    
    [Header("Checkpoint Timeout")]
    [Tooltip("체크포인트에서 이 시간(초) 동안 목표에 도달하지 못하면 강제로 진행")]
    [SerializeField] private float checkpointTimeout = 15f; 

    [Tooltip("체크포인트 대기 중 T+ 표시를 0:51에 고정할지 여부")]
    [SerializeField] private bool lockTimeAtCheckpoint = true;
    [Tooltip("외부 클래스에서 T+ 시간을 고정할지 여부")]
    [SerializeField] private bool lockTimeOnExternalHold = true;
    
    private bool _externalHold;          // 외부(다른 컴포넌트)에서 건 홀드
    private float _externalHoldTimeSnap; // 외부 홀드 시 표시/내부 시간을 고정할 값
    public  bool  IsExternallyHolding => _externalHold;

    public float DeltaTimeSpeed
    {
        get => deltaTimeSpeed;
        set => deltaTimeSpeed = value;
    }

    // -> SLP 자동 스케줄 키들
    [Serializable] private struct SlopeKey { public int m; public int s; public float deg; public float T() => m * 60f + s; }
    [SerializeField] private SlopeKey[] slopeKeys = new SlopeKey[]
    {
        new SlopeKey{ m=0,  s=51, deg=49.5f },
        new SlopeKey{ m=2,  s=5,  deg=40.5f },
        new SlopeKey{ m=2,  s=31, deg=41.0f },
        new SlopeKey{ m=3,  s=45, deg=21.6f },
        new SlopeKey{ m=3,  s=56, deg=22.0f },
        new SlopeKey{ m=4,  s=30, deg=21.6f },
        new SlopeKey{ m=4,  s=54, deg=19.1f },
        new SlopeKey{ m=6,  s=13, deg=12.1f },
        new SlopeKey{ m=8,  s=15, deg=5.0f  },
        new SlopeKey{ m=12, s=14, deg=1.6f  },
    };

    public float TPlusSeconds { get; private set; }
    public float TMinusSeconds { get; private set; }
    public bool  IsCountingDown { get; private set; } = true;

    // 내부 상태 -> 체크포인트
    private bool _checkpointArmed;    // T+가 시작된 뒤 체크포인트 감시 중
    private bool _checkpointHolding;  // 체크포인트에 도달하여 멈춘 상태
    private bool _checkpointCleared;  // 조건을 만족해 해제됨
    private float _checkpointTimer;   // 체크포인트 타임아웃 체크용

    // 내부 상태 -> 자동 SLP
    private bool _autoSlopeEnabled;   // 체크포인트 통과 후 자동 진행 on
    private float[] _slpT;            // 초 단위 시간 캐시
    private float[] _slpV;            // 각도 캐시

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);

        BuildSlopeCaches();
        TMinusSeconds = 11f;
    }

    /// <summary>
    /// 카운트다운 시작 -> 0 도달 시 T+ 진행.
    /// </summary>
    public async UniTask RunCountdownAsync()
    {
        float time = 11f; // 카운트다운 시작값
        IsCountingDown = true;

        TPlusSeconds = 0f;
        TMinusSeconds = time;
        
        _checkpointArmed = false;
        _checkpointHolding = false;
        _checkpointCleared = false;
        _autoSlopeEnabled = false;
        _checkpointTimer = 0f;
        bool firedGreenLed = false;

        UpdateHint(false);

        while (true)
        {
            if (!textCountdown) return;

            // -> 카운트 텍스트 갱신
            float displayAbs = Mathf.Abs(time);
            int displayMin = Mathf.FloorToInt(displayAbs / 60f);
            int displaySec = Mathf.FloorToInt(displayAbs % 60f);
            string prefix = IsCountingDown ? "T - " : "T + ";
            string formatted = (displayMin > 0)
                ? $"{prefix}00:{displayMin:00}:{displaySec:00}"
                : $"{prefix}00:00:{displaySec:00}";
            textCountdown.text = formatted;

            await UniTask.Yield();

            // -> 시간 진행
            if (IsCountingDown)
            {
                // T- 구간 -> 1:1 감소
                time -= Time.deltaTime;
                TMinusSeconds = Mathf.Max(0f, time);
                if (time <= 0f)
                {
                    time = 0f;
                    IsCountingDown = false;

                    // T+ 시작 -> 체크포인트 감시 시작
                    TPlusSeconds = 0f;
                    _checkpointArmed = true;
                }
            }
            else
            {
                // T+ 구간
                float deltaPlus = Time.deltaTime * Mathf.Max(0f, deltaTimeSpeed);
                if (_externalHold) 
                {
                    deltaPlus = 0f; 
                    if (lockTimeOnExternalHold)
                    {
                        TPlusSeconds = _externalHoldTimeSnap;
                        time = _externalHoldTimeSnap; 
                    }
                }

                // 체크포인트 로직
                if (_checkpointArmed && !_checkpointCleared)
                {
                    // 체크포인트 진입 판정: 0:51에 도달하거나 초과하는 순간 진입
                    if (!_checkpointHolding && (TPlusSeconds + deltaPlus) >= checkpointSeconds)
                    {
                        _checkpointHolding = true;
                        _checkpointTimer = 0f; // 타이머 초기화

                        UpdateHint(true);
                        LaunchManager.Instance?.FixStageAlpha(1);
                        LaunchManager.Instance?.StartStagePingPong(2);
                        
                        if (!firedGreenLed)
                        {
                            firedGreenLed = true;
                            LaunchManager.Instance?.PublicStartBlinkGreen(500, 160);
                        }
                        
                        LaunchManager.Instance?.AnimateThrottleY(-110f, 0f, 0.8f, 0.2f);
                        LaunchManager.Instance?.SetGuideText("각도 조정기를 올리세요.");
                        
                        // [25. 12.19 수정] 미입력 타이머 재개 코드 제거 (계속 Pause 상태 유지)
                        // LaunchManager.Instance?.ResumeInactivityTimer(); 
                        
                        if (lockTimeAtCheckpoint)
                        {
                            TPlusSeconds = checkpointSeconds;
                            time = TPlusSeconds; 
                        }

                        deltaPlus = 0f;
                    }

                    // 이미 홀딩 중이면 deltaPlus를 0으로 유지(정지)하고 조건 검사
                    if (_checkpointHolding)
                    {
                        deltaPlus = 0f;
                        _checkpointTimer += Time.deltaTime; // 타임아웃 타이머 진행
                        
                        bool ok = false;
                        if (slope != null)
                        {
                            float diff = Mathf.Abs(slope.CurrentSlopeDeg - requiredSlopeDeg);
                            ok = (diff <= Mathf.Max(0f, slopeToleranceDeg));
                        }

                        // [25. 12. 19 수정] 타임아웃 발생 시 강제 진행 처리
                        if (!ok && _checkpointTimer >= checkpointTimeout)
                        {
                            // 1. 아두이노 스로틀 끄기
                            try { ArduinoInputManager.Instance?.Send("THROTTLE OFF"); } catch {}

                            // 2. 목표 각도까지 부드럽게 이동 (2초)
                            if (slope != null)
                            {
                                float startDeg = slope.CurrentSlopeDeg;
                                float duration = 2.0f;
                                float elapsed = 0f;

                                while (elapsed < duration)
                                {
                                    elapsed += Time.deltaTime;
                                    float t = Mathf.Clamp01(elapsed / duration);
                                    // SmoothStep 이징
                                    float easedT = t * t * (3f - 2f * t);
                                    float val = Mathf.Lerp(startDeg, requiredSlopeDeg, easedT);
                                    slope.SetSlopeDeg(val, forceWhenLocked: true);
                                    
                                    await UniTask.Yield(); // 메인 루프 일시 대기 (시간은 흐르지 않음)
                                }
                                slope.SetSlopeDeg(requiredSlopeDeg, forceWhenLocked: true);
                            }
                            
                            ok = true; // 조건 만족으로 처리
                        }

                        if (ok)
                        {
                            LaunchManager.Instance?.StopAnimateThrottleY(); 
                            LaunchManager.Instance?.SetGuideText("");
                            
                            _checkpointCleared = true;
                            _checkpointHolding = false;
                            UpdateHint(false);
                            LaunchManager.Instance?.PublicStopLedEffects();
                            LedStrip.Range(0, 9, 255, 0, 0);
                            
                            LaunchManager.Instance?.FixStageAlpha(2);

                            _autoSlopeEnabled = true;
                            if (slope != null)
                            {
                                slope.LockInput(true);
                                slope.EnableExternalAnalog(false);
                            }
                        }
                    }
                }

                // 시간 누적
                TPlusSeconds += deltaPlus;
                time += deltaPlus;

                // 자동 SLP 진행
                if (_autoSlopeEnabled && slope != null)
                {
                    float scheduled = EvaluateScheduledSlope(TPlusSeconds);
                    slope.SetSlopeDeg(scheduled, forceWhenLocked: true);
                }
            }
        }
    }

    private void UpdateHint(bool show)
    {
        if (!textHint) return;
        if (show)
        {
            textHint.text = $"Set SLP to {requiredSlopeDeg:F1} º";
            textHint.gameObject.SetActive(true);
        }
        else
        {
            textHint.gameObject.SetActive(false);
        }
    }

    private void BuildSlopeCaches()
    {
        if (slopeKeys == null || slopeKeys.Length == 0)
        {
            _slpT = Array.Empty<float>();
            _slpV = Array.Empty<float>();
            return;
        }

        Array.Sort(slopeKeys, (a, b) => a.T().CompareTo(b.T()));
        int n = slopeKeys.Length;
        _slpT = new float[n];
        _slpV = new float[n];

        for (int i = 0; i < n; i++)
        {
            _slpT[i] = slopeKeys[i].T();
            _slpV[i] = slopeKeys[i].deg;
        }
    }

    private float EvaluateScheduledSlope(float tPlusSec)
    {
        if (_slpT == null || _slpT.Length == 0) return 0f;
        int n = _slpT.Length;

        if (tPlusSec <= _slpT[0]) return _slpV[0];
        if (tPlusSec >= _slpT[n - 1]) return _slpV[n - 1];

        int hi = Array.BinarySearch(_slpT, tPlusSec);
        if (hi >= 0) return _slpV[hi];

        int idx = ~hi;              
        int i0 = idx - 1;           
        int i1 = idx;               

        float t0 = _slpT[i0];
        float t1 = _slpT[i1];
        float v0 = _slpV[i0];
        float v1 = _slpV[i1];

        float u = Mathf.InverseLerp(t0, t1, tPlusSec);
        return Mathf.Lerp(v0, v1, u);
    }
    
    public void BeginExternalHold()
    {
        if (_externalHold) return;
        _externalHold = true;

        if (lockTimeOnExternalHold)
        {
            _externalHoldTimeSnap = TPlusSeconds;
        }
    }

    public void EndExternalHold()
    {
        _externalHold = false;
    }
}