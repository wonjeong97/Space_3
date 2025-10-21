using TMPro;
using UnityEngine;

/// <summary>
/// 목표 궤도(targetKm)까지 남은 거리(KM)를 표기
/// - T- 구간 -> Alt=0으로 간주(= targetKm 그대로 표시)
/// - T+ 구간 -> AltitudeByTime에서 Alt(km) 읽어 targetKm - Alt
/// - 음수는 0으로 클램프(옵션)
/// </summary>
public class DistanceByAlt : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CountController countdown;   // T+/T- 상태 및 T+ 초
    [SerializeField] private AltitudeByTime altitudeByTime;   // 현재 고도(km) 제공
    [SerializeField] private TextMeshProUGUI textDistance;    // DISTANCE 표기 TMP

    [Header("Config")]
    [Tooltip("목표 궤도 고도(km)")]
    [SerializeField] private float targetKm = 550f;

    [Tooltip("0 미만은 0으로 클램프 여부")]
    [SerializeField] private bool clampToZero = true;

    [Tooltip("표시 소수 자리수")]
    [SerializeField] private int decimals = 0;

    private void Reset()
    {
        countdown     = FindObjectOfType<CountController>();
        altitudeByTime = FindObjectOfType<AltitudeByTime>();
        textDistance  = GetComponentInChildren<TextMeshProUGUI>();
    }

    private void Update()
    {
        if (!textDistance || !countdown || !altitudeByTime) return;

        // 1) 현재 고도 읽기 -> T-에서는 0으로 간주
        float curAltKm = countdown.IsCountingDown
            ? 0f
            : altitudeByTime.GetCurrentAltitudeKm(countdown.TPlusSeconds);

        // 2) 남은 거리 계산
        float distKm = targetKm - curAltKm;
        if (clampToZero && distKm < 0f) distKm = 0f;

        // 3) 출력
        textDistance.text = $"DIST {distKm.ToString($"F{decimals}")} km";
    }

    /// <summary> 외부에서 현재 남은 거리(km) 필요 시 호출 </summary>
    public float GetCurrentDistanceKm()
    {
        if (!countdown || !altitudeByTime) return Mathf.Max(0f, targetKm);
        float alt = countdown.IsCountingDown ? 0f : altitudeByTime.GetCurrentAltitudeKm(countdown.TPlusSeconds);
        float d = targetKm - alt;
        return clampToZero ? Mathf.Max(0f, d) : d;
    }
}