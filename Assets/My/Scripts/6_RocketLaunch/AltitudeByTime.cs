using System;
using UnityEngine;
using TMPro;

[Serializable]
public struct AltKey
{
    [Range(0,59)] public int minutes;
    [Range(0,59)] public int seconds;
    public float km; // 고도(km)
    public float T() => minutes * 60f + seconds;
}

/// <summary>
/// T+ 시간에 따른 고도(km) 계산/표시
/// - T- 구간 -> 0 km 고정
/// - 0 -> 0:51 구간만 ease, 이후는 linear
/// - 마지막 키 이후 고정
/// </summary>
public class AltitudeByTime : MonoBehaviour
{   
    public float GetCurrentAltitudeKm(float tPlusSec) { return Evaluate(tPlusSec); }
    
    [Header("Refs")]
    [SerializeField] private CountController countdown;
    [SerializeField] private TextMeshProUGUI textAltitude;

    [Header("Ease 설정")]
    [SerializeField] private EasingMode firstSegmentEasing = EasingMode.SmootherStep;
    
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

    private float[] _t;
    private float[] _v;

    // -> 초기화: 키 정렬 및 캐시
    private void Awake()
    {
        Array.Sort(keys, (a,b) => a.T().CompareTo(b.T()));
        int n = keys.Length;
        _t = new float[n];
        _v = new float[n];
        for (int i = 0; i < n; i++)
        {
            _t[i] = keys[i].T();
            _v[i] = keys[i].km;
        }
    }

    // -> 프레임 갱신: 텍스트 반영
    private void Update()
    {
        if (!countdown || !textAltitude) return;

        if (countdown.IsCountingDown)
        {
            textAltitude.text = "ALT 0 km";
            return;
        }

        float t = countdown.TPlusSeconds;
        float alt = Evaluate(t);
        textAltitude.text = $"ALT {alt:F1} km";
    }

    // -> 주어진 T+초의 고도 계산 (첫 구간만 ease)
    private float Evaluate(float tPlusSec)
    {
        if (_t == null || _t.Length == 0) return 0f;
        int n = _t.Length;

        if (tPlusSec <= _t[0]) return _v[0];
        if (tPlusSec >= _t[n-1]) return _v[n-1];

        int hi = Array.BinarySearch(_t, tPlusSec);
        if (hi >= 0) return _v[hi];

        int idx = ~hi;      // 삽입 위치
        int i0  = idx - 1;  // 앞 키 인덱스
        int i1  = idx;      // 뒤 키 인덱스

        float t0 = _t[i0], t1 = _t[i1];
        float a0 = _v[i0], a1 = _v[i1];

        float u = Mathf.InverseLerp(t0, t1, tPlusSec);

        // 0->0:51 구간만 ease, 나머지는 linear
        // (키 배열이 정렬되어 있어 i0==0 && i1==1 이면 첫 구간)
        if (i0 == 0 && i1 == 1)
        {
            float ue = EasingUtil.Apply(u, firstSegmentEasing);
            return Mathf.Lerp(a0, a1, ue);
        }
        else
        {
            return Mathf.Lerp(a0, a1, u);
        }
    }
}
