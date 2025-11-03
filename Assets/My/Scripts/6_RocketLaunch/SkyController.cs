using UnityEngine;

/// <summary>
/// CountController의 T+ 시간에 따라 하늘(또는 지정한 Transform)의 Y를 제어하는 컨트롤러.
/// 1) 기본 선형 매핑: T+가 minTPlus일 때 startY, maxTPlus일 때 endY가 되도록 선형 보간
/// 2) 세그먼트 기반 이동: 특정 T+ 구간 동안 "시작 시점의 Y → targetY"로 자동 보간(속도 입력 불필요)
///    - 세그먼트가 활성화된 동안에는 세그먼트 보간이 선형 매핑보다 우선 적용됨
/// </summary>
public sealed class SkyController : MonoBehaviour
{
    // =======================
    // Refs
    // =======================
    [Header("Refs")]
    [SerializeField] private CountController countController;
    [SerializeField] private Transform target; // 비워두면 본인 Transform 사용

    // =======================
    // Linear Mapping (기본)
    // =======================
    [Header("Linear Mapping by T+")]
    [Tooltip("선형 매핑 기능 사용 여부")]
    [SerializeField] private bool useLinearMapping = true;

    [Tooltip("T+가 minTPlus일 때의 Y 위치")]
    [SerializeField] private float startY = 0f;

    [Tooltip("T+가 maxTPlus일 때의 Y 위치(보통 더 작은 값, 즉 아래쪽)")]
    [SerializeField] private float endY = -500f;

    [Tooltip("이 값 이하의 T+에서는 startY 유지")]
    [SerializeField] private float minTPlus = 0f;

    [Tooltip("이 값 이상의 T+에서는 endY 유지")]
    [SerializeField] private float maxTPlus = 240f;

    [Header("Options")]
    [Tooltip("로컬 좌표로 이동할지 여부. 체크 해제 시 월드 좌표로 이동")]
    [SerializeField] private bool useLocalPosition = true;

    [Tooltip("선형 매핑에서만 사용. 0이면 즉시 반영, 값이 클수록 천천히 따라감")]
    [Range(0f, 0.5f)]
    [SerializeField] private float smoothDamp = 0.05f;

    private float _velocityY; // SmoothDamp 내부 상태

    // =======================
    // Timed Move Segments (자동 보간)
    // =======================
    [System.Serializable]
    public struct MoveSegment
    {
        [Tooltip("세그먼트가 동작할 T+ 시작(초)")]
        public float startTPlus;

        [Tooltip("세그먼트가 동작할 T+ 종료(초), 포함 범위")]
        public float endTPlus;

        [Tooltip("이 구간 종료 시점에 도달해야 하는 목표 Y")]
        public float targetY;

        [Tooltip("세그먼트 진입 시 즉시 targetY로 스냅")]
        public bool snapOnEnter;

        [Tooltip("한 번만 실행하고 다시는 트리거되지 않음")]
        public bool triggerOnce;
    }

    [Header("Timed Move Segments (by T+)")]
    [Tooltip("세그먼트가 활성화되면 선형 매핑보다 우선 적용됩니다.")]
    [SerializeField] private MoveSegment[] moveSegments = new MoveSegment[0];

    // 세그먼트 실행 상태
    private int _activeSegmentIndex = -1;  // 현재 활성 세그먼트
    private bool[] _consumedSegments;      // triggerOnce 처리용
    private float _segmentEnterTPlus;      // 세그먼트 진입 순간의 T+
    private float _segmentStartY;          // 세그먼트 진입 순간의 현재 Y

    // =======================
    // Unity lifecycle
    // =======================
    private void Reset()
    {
        if (target == null) target = transform;

        Vector3 p = useLocalPosition ? target.localPosition : target.position;
        startY = p.y;
        endY = startY - 500f;
    }

    private void Awake()
    {
        if (target == null) target = transform;

        if (countController == null)
        {
            LogUtil.LogWarn(nameof(SkyController), nameof(Awake), "countController가 지정되지 않음. 동일 오브젝트에서 검색합니다.");
            countController = GetComponent<CountController>();
        }
    }

    private void Start()
    {
        _consumedSegments = (moveSegments != null && moveSegments.Length > 0)
            ? new bool[moveSegments.Length]
            : new bool[0];

        // 시작 시 선형 매핑 기준으로 초기 위치 스냅(옵션)
        float tPlus = GetCurrentTPlus();
        if (useLinearMapping)
        {
            float alpha = 0f;
            if (maxTPlus > minTPlus) alpha = Mathf.InverseLerp(minTPlus, maxTPlus, tPlus);
            float initY = Mathf.Lerp(startY, endY, alpha);
            SnapY(initY);
        }
    }

    private void Update()
    {
        float tPlus = GetCurrentTPlus();

        // 1) 세그먼트 우선 적용
        bool appliedBySegment = UpdateSegmentsAutoLerp(tPlus);

        // 2) 세그먼트가 적용되지 않은 경우에만 선형 매핑 적용
        if (!appliedBySegment && useLinearMapping)
        {
            float alpha = 0f;
            if (maxTPlus > minTPlus) alpha = Mathf.InverseLerp(minTPlus, maxTPlus, tPlus);
            float yTarget = Mathf.Lerp(startY, endY, alpha);
            StepY(yTarget);
        }
    }

    // =======================
    // Public API
    // =======================
    public void SnapToTPlus(float tPlus)
    {
        float alpha = (maxTPlus > minTPlus) ? Mathf.InverseLerp(minTPlus, maxTPlus, tPlus) : 0f;
        float y = Mathf.Lerp(startY, endY, alpha);
        SnapY(y);
        LogUtil.Log(nameof(SkyController), nameof(SnapToTPlus), $"tPlus={tPlus}, y={y}");
    }

    public void SetRange(float newStartY, float newEndY, float newMinTPlus, float newMaxTPlus, bool snapNow = false)
    {
        startY = newStartY;
        endY = newEndY;
        minTPlus = newMinTPlus;
        maxTPlus = newMaxTPlus;

        if (snapNow)
        {
            float tPlus = GetCurrentTPlus();
            SnapToTPlus(tPlus);
        }

        LogUtil.Log(nameof(SkyController), nameof(SetRange), $"[{startY} -> {endY}] over T+ [{minTPlus} -> {maxTPlus}]");
    }

    public void ResetSegments(bool clearConsumed = true)
    {
        _activeSegmentIndex = -1;
        if (clearConsumed && _consumedSegments != null)
        {
            for (int i = 0; i < _consumedSegments.Length; i++) _consumedSegments[i] = false;
        }
    }

    // =======================
    // Internals
    // =======================
    private float GetCurrentTPlus()
    {
        if (countController == null) return 0f;

        try
        {
            return Mathf.Max(0f, countController.TPlusSeconds);
        }
        catch (System.SystemException e)
        {
            LogUtil.LogError(nameof(SkyController), nameof(GetCurrentTPlus), $"T+ 조회 실패: {e.Message}");
            return 0f;
        }
    }

    private float GetCurrentY()
    {
        return useLocalPosition ? target.localPosition.y : target.position.y;
    }

    private void SetY(float y)
    {
        if (useLocalPosition)
        {
            Vector3 lp = target.localPosition;
            lp.y = y;
            target.localPosition = lp;
        }
        else
        {
            Vector3 p = target.position;
            p.y = y;
            target.position = p;
        }
    }

    private void SnapY(float y)
    {
        SetY(y);
        _velocityY = 0f;
    }

    // 선형 매핑에서만 사용되는 부드러운 추적(SmoothDamp)
    private void StepY(float targetYValue)
    {
        if (smoothDamp <= 0f)
        {
            SnapY(targetYValue);
            return;
        }

        if (useLocalPosition)
        {
            Vector3 lp = target.localPosition;
            float newY = Mathf.SmoothDamp(lp.y, targetYValue, ref _velocityY, smoothDamp);
            if (!Mathf.Approximately(newY, lp.y))
            {
                lp.y = newY;
                target.localPosition = lp;
            }
        }
        else
        {
            Vector3 p = target.position;
            float newY = Mathf.SmoothDamp(p.y, targetYValue, ref _velocityY, smoothDamp);
            if (!Mathf.Approximately(newY, p.y))
            {
                p.y = newY;
                target.position = p;
            }
        }
    }

    /// <summary>
    /// 세그먼트 자동 보간 로직:
    /// - 세그먼트 진입 시점 T+와 진입 당시의 Y를 저장
    /// - 세그먼트 구간 동안 시작Y -> targetY를 시간 비율로 선형 보간(속도 입력 불필요)
    /// - snapOnEnter면 진입 즉시 targetY로 스냅
    /// </summary>
    private bool UpdateSegmentsAutoLerp(float tPlus)
    {
        // 현재 활성 세그먼트가 여전히 유효한지 확인
        bool hasActive = false;
        if (_activeSegmentIndex >= 0 && _activeSegmentIndex < moveSegments.Length)
        {
            MoveSegment seg = moveSegments[_activeSegmentIndex];
            if (tPlus >= seg.startTPlus && tPlus <= seg.endTPlus)
            {
                hasActive = true;
            }
        }

        // 활성 세그먼트가 없으면 현재 T+에 맞는 세그먼트를 검색(뒤에서부터 우선)
        if (!hasActive)
        {
            int newIndex = -1;
            for (int i = moveSegments.Length - 1; i >= 0; i--)
            {
                if (_consumedSegments.Length == moveSegments.Length && _consumedSegments[i]) continue;

                MoveSegment segCheck = moveSegments[i];
                if (tPlus >= segCheck.startTPlus && tPlus <= segCheck.endTPlus)
                {
                    newIndex = i;
                    break;
                }
            }

            // 세그먼트 변경 감지
            if (newIndex != _activeSegmentIndex)
            {
                _activeSegmentIndex = newIndex;

                if (_activeSegmentIndex >= 0)
                {
                    MoveSegment segEnter = moveSegments[_activeSegmentIndex];

                    if (segEnter.snapOnEnter)
                    {
                        SnapY(segEnter.targetY);

                        if (segEnter.triggerOnce && _consumedSegments.Length == moveSegments.Length)
                            _consumedSegments[_activeSegmentIndex] = true;

                        LogUtil.Log(nameof(SkyController), nameof(Update),
                            $"세그먼트({_activeSegmentIndex}) 진입 즉시 스냅: T+={tPlus}, targetY={segEnter.targetY}");
                        return true;
                    }
                    else
                    {
                        // 자동 보간용 시작점 기록
                        _segmentEnterTPlus = Mathf.Max(segEnter.startTPlus, tPlus);
                        _segmentStartY = GetCurrentY();

                        LogUtil.Log(nameof(SkyController), nameof(Update),
                            $"세그먼트({_activeSegmentIndex}) 진입: T+={tPlus}, startY={_segmentStartY}, targetY={segEnter.targetY}, 기간=[{segEnter.startTPlus}~{segEnter.endTPlus}]");
                    }
                }
            }
        }

        // 활성 세그먼트가 있으면 시간 비율로 보간 적용
        if (_activeSegmentIndex >= 0 && _activeSegmentIndex < moveSegments.Length)
        {
            MoveSegment seg = moveSegments[_activeSegmentIndex];

            // 세그먼트 종료 처리
            if (tPlus > seg.endTPlus)
            {
                // 종료 시점에 정확히 targetY로 스냅(마무리 보정)
                SnapY(seg.targetY);

                if (seg.triggerOnce && _consumedSegments.Length == moveSegments.Length)
                    _consumedSegments[_activeSegmentIndex] = true;

                _activeSegmentIndex = -1;
                return true;
            }

            // 유효한 기간이 0 이하면 즉시 스냅
            float duration = Mathf.Max(0f, seg.endTPlus - seg.startTPlus);
            if (Mathf.Approximately(duration, 0f))
            {
                SnapY(seg.targetY);
                if (seg.triggerOnce && _consumedSegments.Length == moveSegments.Length)
                    _consumedSegments[_activeSegmentIndex] = true;
                _activeSegmentIndex = -1;
                return true;
            }

            // 현재까지의 진행도(0..1)
            float t0 = Mathf.Clamp(_segmentEnterTPlus, seg.startTPlus, seg.endTPlus);
            float tClamped = Mathf.Clamp(tPlus, seg.startTPlus, seg.endTPlus);
            float elapsed = Mathf.Max(0f, tClamped - t0);
            float remain = Mathf.Max(0f, seg.endTPlus - t0);
            float alpha = (remain <= 0f) ? 1f : Mathf.Clamp01(elapsed / remain);

            // 시작Y -> targetY 선형 보간
            float y = Mathf.Lerp(_segmentStartY, seg.targetY, alpha);
            SetY(y);
            return true;
        }

        return false;
    }
}
