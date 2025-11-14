using System;
using TMPro;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary> 키 사이 보간에 사용할 이징 종류(기존) </summary>
public enum EasingMode
{
    SmoothStep,
    SmootherStep,
    EaseInCubic,
    EaseOutCubic,
    EaseInOutCubic
}

/// <summary> 선형 u(0..1)를 이징 u(0..1)로 변환(기존) </summary>
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

    [Header("Checkpoint (T+ 0:51 에서 정지 후 SLP 맞추면 진행)")]
    [SerializeField] private SlopeController slope;      // 현재 SLP 확인/설정용
    [SerializeField] private float checkpointSeconds = 51f;   // 0:51
    [SerializeField] private float requiredSlopeDeg = 49.5f;  // 목표 SLP
    [SerializeField] private float slopeToleranceDeg = 0.2f;  // 허용 오차(±)
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

    // -> SLP 자동 스케줄 키들(체크포인트 통과 후 사용자 입력 없이 자동 진행)
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

    // 내부 상태 -> 자동 SLP
    private bool _autoSlopeEnabled;   // 체크포인트 통과 후 자동 진행 on
    private float[] _slpT;            // 초 단위 시간 캐시
    private float[] _slpV;            // 각도 캐시

    // -> 싱글턴 설정 및 스케줄 캐시 구성
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);

        BuildSlopeCaches();
        TMinusSeconds = 11f;
    }

    /// <summary>
    /// 카운트다운 시작 -> 0 도달 시 T+ 진행.
    /// T+ 0:51에 도달하면 SLP를 requiredSlopeDeg±tolerance로 맞출 때까지 정지한다.
    /// 체크포인트 통과 후에는 SLP를 키프레임에 따라 자동으로 선형 보간하여 진행하고, 사용자 입력은 받지 않는다.
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
                if (_externalHold) // 외부 홀드 여부
                {
                    deltaPlus = 0f; // 시간 진행 중단

                    // 표기 고정(옵션)
                    if (lockTimeOnExternalHold)
                    {
                        TPlusSeconds = _externalHoldTimeSnap;
                        time = _externalHoldTimeSnap; // 카운터 텍스트도 해당 값으로 고정
                    }
                }

                // 체크포인트 로직
                if (_checkpointArmed && !_checkpointCleared)
                {
                    // 체크포인트 진입 판정: 0:51에 도달하거나 초과하는 순간 진입
                    if (!_checkpointHolding && (TPlusSeconds + deltaPlus) >= checkpointSeconds)
                    {
                        _checkpointHolding = true;

                        // 안내 문구 출력
                        UpdateHint(true);
                        LaunchManager.Instance?.FadeInStagePublicAsync(2).Forget(); // 2. 피치 기동 이미지 페이드 인
                        if (!firedGreenLed)
                        {
                            firedGreenLed = true;
                            LaunchManager.Instance?.PublicStartBlinkGreen(500, 160);
                        }
                        
                        // 스로틀 버튼 애니메이션 실행
                        LaunchManager.Instance?.AnimateThrottleY(-110f, 0f, 0.8f, 0.2f);
                        LaunchManager.Instance?.SetGuideText("스로틀을 올리세요.");
                        LaunchManager.Instance?.ResumeInactivityTimer();
                        
                        // 표기/내부 시간 고정
                        if (lockTimeAtCheckpoint)
                        {
                            TPlusSeconds = checkpointSeconds;
                            time = TPlusSeconds; // T+ 표기를 정확히 0:51로 고정
                        }

                        // 이후 deltaPlus는 0으로 강제
                        deltaPlus = 0f;
                    }

                    // 이미 홀딩 중이면 deltaPlus를 0으로 유지(정지)
                    if (_checkpointHolding)
                    {
                        deltaPlus = 0f;
                        
                        // Slope 조건 검사(오차 포함)
                        bool ok = false;
                        if (slope != null)
                        {
                            float diff = Mathf.Abs(slope.CurrentSlopeDeg - requiredSlopeDeg);
                            ok = (diff <= Mathf.Max(0f, slopeToleranceDeg));
                        }

                        if (ok)
                        {
                            LaunchManager.Instance?.StopAnimateThrottleY(); // 스로틀 애니메이션 해제
                            LaunchManager.Instance?.SetGuideText("");
                            
                            _checkpointCleared = true;
                            _checkpointHolding = false;
                            UpdateHint(false);
                            LaunchManager.Instance?.PublicStopLedEffects();
                            LedStrip.Range(0, 9, 255, 0, 0);

                            // -> 이후는 자동 SLP 진행 모드로 전환 + 입력 잠금(스냅)
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

                // -> 자동 SLP 진행: 키프레임을 선형 보간하여 매 프레임 적용
                if (_autoSlopeEnabled && slope != null)
                {
                    float scheduled = EvaluateScheduledSlope(TPlusSeconds);
                    slope.SetSlopeDeg(scheduled, forceWhenLocked: true);
                }
            }
        }
    }

    /// <summary> 체크포인트 안내 문구 표시/숨김 </summary>
    private void UpdateHint(bool show)
    {
        if (!textHint) return;

        if (show)
        {
            // 예: "SLP를 49.5º로 맞추세요"
            textHint.text = $"Set SLP to {requiredSlopeDeg:F1} º";
            textHint.gameObject.SetActive(true);
        }
        else
        {
            textHint.gameObject.SetActive(false);
        }
    }

    /// <summary> SLP 키 스케줄 캐시를 구성(시간 오름차순 정렬) </summary>
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

    /// <summary> 주어진 T+초에서 스케줄된 SLP 각도를 반환(선형 보간, 양끝 고정) </summary>
    private float EvaluateScheduledSlope(float tPlusSec)
    {
        if (_slpT == null || _slpT.Length == 0) return 0f;
        int n = _slpT.Length;

        if (tPlusSec <= _slpT[0]) return _slpV[0];
        if (tPlusSec >= _slpT[n - 1]) return _slpV[n - 1];

        int hi = Array.BinarySearch(_slpT, tPlusSec);
        if (hi >= 0) return _slpV[hi];

        int idx = ~hi;              // 삽입 위치
        int i0 = idx - 1;           // 앞 키
        int i1 = idx;               // 뒤 키

        float t0 = _slpT[i0];
        float t1 = _slpT[i1];
        float v0 = _slpV[i0];
        float v1 = _slpV[i1];

        float u = Mathf.InverseLerp(t0, t1, tPlusSec);
        return Mathf.Lerp(v0, v1, u);
    }
    
    ///<summary> 외부 홀드 시작 </summary>
    public void BeginExternalHold()
    {
        if (_externalHold) return;
        _externalHold = true;

        if (lockTimeOnExternalHold)
        {
            _externalHoldTimeSnap = TPlusSeconds;
        }
    }

    ///<summary> 외부 홀드 해제 </summary>
    public void EndExternalHold()
    {
        _externalHold = false;
    }
}
