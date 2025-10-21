using System;
using System.Globalization;
using UnityEngine;
using TMPro;

[Serializable]
public struct VelKey
{
    [Range(0,59)] public int minutes;
    [Range(0,59)] public int seconds;
    public float kmph; // km/h
    public float T() => minutes * 60f + seconds;
}

/// <summary>
/// T+ 시간에 따른 속도(km/h) 계산/표시
/// - T- 구간 -> 0 km/h 고정
/// - 0 -> 0:51 구간만 ease, 이후는 linear
/// - 마지막 키 이후 고정
/// </summary>
public class VelocityByTime : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CountController countdown;
    [SerializeField] private TextMeshProUGUI textVelocity;

    [Header("Format")]
    [SerializeField] private int decimals = 0;

    [Header("Ease 설정 (첫 구간만 적용)")]
    [SerializeField] private EasingMode firstSegmentEasing = EasingMode.SmootherStep;

    // 속도 키: 0, 2:05, 3:56, 4:30, 12:14 (나머지 시각은 보간으로 자동 계산)
    // 0:51, 2:31, 4:54, 6:13, 8:15 등은 키 없이도 구간 보간으로 계산됨.
    [SerializeField] private VelKey[] keys = new VelKey[]
    {
        new VelKey{ minutes=0,  seconds=0,  kmph=    0f },
        new VelKey{ minutes=2,  seconds=5,  kmph= 6195f },
        new VelKey{ minutes=3,  seconds=56, kmph=11599f },
        new VelKey{ minutes=4,  seconds=30, kmph=15270f },
        new VelKey{ minutes=12, seconds=14, kmph=27318f }, // 이후 고정
    };

    private float[] _t;
    private float[] _v;
    private CultureInfo _culture;

    // -> 초기화: 키 정렬/캐시
    private void Awake()
    {
        _culture = CultureInfo.InvariantCulture;
        Array.Sort(keys, (a,b) => a.T().CompareTo(b.T()));
        int n = keys.Length;
        _t = new float[n];
        _v = new float[n];
        for (int i = 0; i < n; i++)
        {
            _t[i] = keys[i].T();
            _v[i] = keys[i].kmph;
        }
    }

    // -> 프레임 갱신: 텍스트 반영
    private void Update()
    {
        if (!countdown || !textVelocity) return;

        if (countdown.IsCountingDown)
        {
            textVelocity.text = "VEL 0 km/h";
            return;
        }

        float t = countdown.TPlusSeconds;
        float kmh = Evaluate(t);
        string fmt = (decimals <= 0) ? "N0" : $"N{decimals}";
        textVelocity.text = $"VEL {kmh.ToString(fmt, _culture)} km/h";
    }

    /// <summary> 외부에서 필요 시 현재 속도(km/h) 얻기 </summary>
    public float GetCurrentVelocityKmh(float tPlusSec) => Evaluate(tPlusSec);

    // -> 주어진 T+초의 속도 계산 (첫 구간만 ease)
    private float Evaluate(float tPlusSec)
    {
        if (_t == null || _t.Length == 0) return 0f;
        int n = _t.Length;

        if (tPlusSec <= _t[0]) return _v[0];
        if (tPlusSec >= _t[n-1]) return _v[n-1];

        int hi = Array.BinarySearch(_t, tPlusSec);
        if (hi >= 0) return _v[hi];

        int idx = ~hi;
        int i0  = idx - 1;
        int i1  = idx;

        float t0 = _t[i0], t1 = _t[i1];
        float v0 = _v[i0], v1 = _v[i1];

        float u = Mathf.InverseLerp(t0, t1, tPlusSec);

        // "첫 구간만 ease":
        // 실제 첫 구간은 0 -> 2:05 이지만, 요구는 "0 -> 0:51"만 ease.
        // 따라서 t가 0:51 이하일 때만 ease, 그 이후는 linear.
        float firstSegmentEnd = 51f; // 0:51
        if (t1 <= firstSegmentEnd) // 현재 구간이 통째로 0..0:51인 경우
        {
            float ue = EasingUtil.Apply(u, firstSegmentEasing);
            return Mathf.Lerp(v0, v1, ue);
        }
        else if (t0 < firstSegmentEnd && tPlusSec <= firstSegmentEnd)
        {
            // 드물지만 키 구간이 0..2:05이고 t가 0..0:51인 경우 -> ease
            float ue = EasingUtil.Apply(u, firstSegmentEasing);
            return Mathf.Lerp(v0, v1, ue);
        }
        else
        {
            // 그 외 전부 선형
            return Mathf.Lerp(v0, v1, u);
        }
    }
}
