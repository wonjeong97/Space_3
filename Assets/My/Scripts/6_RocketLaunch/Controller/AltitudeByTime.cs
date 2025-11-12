using System;
using UnityEngine;
using TMPro;

[Serializable]
public struct AltKey
{
    [Range(0, 59)] public int minutes;
    [Range(0, 59)] public int seconds;
    public float km; // 고도(km)

    // 키의 절대 시간(T+초) 계산
    public float T() { return minutes * 60f + seconds; }
}

/// <summary>
/// T+ 시간에 따른 고도(km) 계산/표시
/// - T- 구간: 0 km 고정
/// - 0~0:51 구간만 easing, 이후는 linear
/// - 마지막 키 이후: 마지막 값 유지
/// </summary>
public sealed class AltitudeByTime : MonoBehaviour
{
    #region Public API

    /// <summary> 외부에서 T+초 입력 시 현재 고도(km) 값을 얻기 위한 헬퍼 </summary>
    public float GetCurrentAltitudeKm(float tPlusSec)
    {
        return Evaluate(tPlusSec);
    }

    #endregion

    #region Serialized Refs

    [Header("Refs")]
    [SerializeField] private CountController countdown;          // 카운트다운/경과시간 제공자
    [SerializeField] private TextMeshProUGUI textAltitude;       // 고도 텍스트(TMP)

    [Header("Ease 설정")]
    [SerializeField] private EasingMode firstSegmentEasing = EasingMode.SmootherStep; // 첫 구간(0~0:51) easing 모드

    // 키프레임 데이터(시간/고도): minutes/seconds -> T+초로 변환하여 내부에서 정렬/보간
    [SerializeField] private AltKey[] keys = new AltKey[]
    {
        new AltKey{ minutes=0,  seconds=0,  km=  0.0f },
        new AltKey{ minutes=0,  seconds=51, km=  0.3f },
        new AltKey{ minutes=2,  seconds=5,  km= 64.5f },
        new AltKey{ minutes=2,  seconds=31, km=100.0f },
        new AltKey{ minutes=3,  seconds=45, km=200.0f },
        new AltKey{ minutes=3,  seconds=56, km=210.0f },
        new AltKey{ minutes=4,  seconds=30, km=261.0f },
        new AltKey{ minutes=4,  seconds=54, km=300.0f },
        new AltKey{ minutes=6,  seconds=13, km=400.0f },
        new AltKey{ minutes=8,  seconds=15, km=500.0f },
        new AltKey{ minutes=12, seconds=14, km=550.0f }, // 이후 고정
    };

    #endregion

    #region Private Cache

    // 정렬된 키 시각/값 캐시 (초/고도)
    private float[] _t; // ascending
    private float[] _v; // same order as _t

    #endregion

    #region Unity Life-Cycle

    /// <summary> 초기화: 키 정렬 및 캐시 </summary>
    private void Awake()
    {
        // 키가 비어있을 수 있으므로 가드
        if (keys == null || keys.Length == 0)
        {
            _t = new[] { 0f };
            _v = new[] { 0f };
            return;
        }

        Array.Sort(keys, (a, b) => a.T().CompareTo(b.T()));

        int n = keys.Length;
        _t = new float[n];
        _v = new float[n];

        for (int i = 0; i < n; i++)
        {
            _t[i] = keys[i].T();
            _v[i] = keys[i].km;
        }
    }

    /// <summary> 프레임 갱신: 카운트 상태에 따라 고도 텍스트 업데이트 </summary>
    private void Update()
    {
        if (!countdown || !textAltitude) return;

        // T- 구간은 0 KM 표기
        if (countdown.IsCountingDown)
        {
            textAltitude.text = "0 KM";
            return;
        }

        float t = countdown.TPlusSeconds;
        float altKm = Evaluate(t);
        textAltitude.text = $"{altKm:F1} KM";
    }

    #endregion

    #region Evaluation

    /// <summary>
    /// 주어진 T+초의 고도(km) 계산.
    /// - 첫 구간(키 0~1)만 easing 적용, 이후 구간은 선형 보간.
    /// </summary>
    private float Evaluate(float tPlusSec)
    {
        if (_t == null || _t.Length == 0) return 0f;

        int n = _t.Length;

        // 범위 밖 처리
        if (tPlusSec <= _t[0]) return _v[0];
        if (tPlusSec >= _t[n - 1]) return _v[n - 1];

        // 정확히 키 타임과 일치하면 해당 값 반환
        int hi = Array.BinarySearch(_t, tPlusSec);
        if (hi >= 0) return _v[hi];

        // 삽입 위치로 구간 결정
        int idx = ~hi;
        int i0 = idx - 1;
        int i1 = idx;

        float t0 = _t[i0], t1 = _t[i1];
        float v0 = _v[i0], v1 = _v[i1];

        float u = Mathf.InverseLerp(t0, t1, tPlusSec);

        // 첫 구간(0~1)만 easing
        if (i0 == 0 && i1 == 1)
        {
            float ue = EasingUtil.Apply(u, firstSegmentEasing);
            return Mathf.Lerp(v0, v1, ue);
        }

        return Mathf.Lerp(v0, v1, u);
    }

    #endregion
}
