using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CountController.TPlusSeconds에 따라 각 연료 이미지의 fillAmount를 1→0으로 선형 감소시킨다.
/// - 카운트다운(T-) 동안에는 fillAmount=1 유지.
/// - T+ 구간에서 트랙별 [start,end] 구간만큼 선형으로 1→0.
/// - end 이전에는 1, end 이후에는 0으로 고정.
/// </summary>
public class FuelController : MonoBehaviour
{
    [Serializable]
    public struct FuelTrack
    {
        [Tooltip("감소시킬 연료 이미지 (Image.type=Filled 필요)")]
        public Image image;

        [Header("Start (T+ mm:ss)")]
        [Range(0, 59)] public int startMin;
        [Range(0, 59)] public int startSec;

        [Header("End (T+ mm:ss)")]
        [Range(0, 59)] public int endMin;
        [Range(0, 59)] public int endSec;

        // 캐시용(에디터 표시 필요 없으므로 NonSerialized)
        [NonSerialized] public float startT;  // seconds
        [NonSerialized] public float endT;    // seconds
    }

    [Header("Refs")]
    [SerializeField] private CountController count;  // 없으면 Instance 사용

    [Header("Tracks")]
    [SerializeField] private FuelTrack[] tracks;

    // -> 초기화: 참조/시간 캐시
    private void Awake()
    {
        if (count == null) count = CountController.Instance;
        RebuildTimeCache();
    }

    // -> 에디터에서 값 바꿀 때 시간 캐시 재계산
    private void OnValidate()
    {
        RebuildTimeCache();
    }

    // -> 매 프레임 fillAmount 갱신
    private void LateUpdate()
    {
        if (tracks == null || tracks.Length == 0) return;
        float tPlus = 0f;
        bool validT = false;

        if (count != null)
        {
            // T- 동안에는 모두 1로 유지
            if (count.IsCountingDown)
            {
                SetAllFill(1f);
                return;
            }
            tPlus = count.TPlusSeconds;
            validT = true;
        }

        // CountController가 없으면 아무 것도 하지 않음
        if (!validT) return;

        for (int i = 0; i < tracks.Length; i++)
        {
            Image img = tracks[i].image;
            if (img == null) continue;

            float startT = tracks[i].startT;
            float endT = tracks[i].endT;

            // 시간이 뒤집힌 경우 방어적으로 교환
            if (endT < startT)
            {
                float tmp = startT;
                startT = endT;
                endT = tmp;
            }

            // 구간 밖 처리
            if (tPlus <= startT)
            {
                SetFill(img, 1f);
                continue;
            }
            if (tPlus >= endT)
            {
                SetFill(img, 0f);
                continue;
            }

            // 구간 내 선형 보간: u = (t - start) / (end - start)
            float u = Mathf.InverseLerp(startT, endT, tPlus);
            float fill = 1f - u; // 1→0
            SetFill(img, fill);
        }
    }

    // -> 시간 캐시를 재계산
    private void RebuildTimeCache()
    {
        if (tracks == null) return;
        for (int i = 0; i < tracks.Length; i++)
        {
            tracks[i].startT = Mathf.Max(0f, tracks[i].startMin * 60f + tracks[i].startSec);
            tracks[i].endT   = Mathf.Max(0f, tracks[i].endMin   * 60f + tracks[i].endSec);
        }
    }

    // -> 모든 트랙 fillAmount 일괄 설정
    private void SetAllFill(float v)
    {
        if (tracks == null) return;
        for (int i = 0; i < tracks.Length; i++)
        {
            if (tracks[i].image != null) SetFill(tracks[i].image, v);
        }
    }

    // -> 안전하게 fillAmount 설정
    private static void SetFill(Image img, float v)
    {
        // Filled 타입이 아니어도 값은 들어가지만, 표시되려면 Filled여야 함
        img.fillAmount = Mathf.Clamp01(v);
    }
}
