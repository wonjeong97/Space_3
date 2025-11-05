using UnityEngine;

/// <summary>
/// CountController의 T+ 시간에 따라 고도(km) 키프레임을 보간해서
/// 로켓(또는 지정한 Transform)의 Y를 올려주는 컨트롤러.
/// - 고도(km)를 배경 높이(Y)로 스케일링해서 사용.
/// - 세그먼트 경계 없이 부드럽게 이어짐.
/// </summary>
public sealed class RocketYController : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CountController countController;
    [SerializeField] private Transform target; // 비워두면 본인 Transform 사용

    [Header("Background Mapping")]
    [Tooltip("고도 0km일 때의 Y(로켓 시작 높이)")]
    [SerializeField] private float baseY = 0f;

    [Tooltip("최대 고도 maxAltitudeKm일 때의 Y(배경 맨 위)")]
    [SerializeField] private float topY = 700f;

    [Tooltip("키프레임 중 최대 고도(km). 예: 550")]
    [SerializeField] private float maxAltitudeKm = 550f;

    [Header("Options")]
    [Tooltip("로컬 좌표로 이동할지 여부. 체크 해제 시 월드 좌표로 이동")]
    [SerializeField] private bool useLocalPosition = true;

    [Tooltip("SmoothDamp 시간. 0이면 즉시 반영, 값이 클수록 천천히 따라감")]
    [Range(0f, 0.5f)]
    [SerializeField] private float smoothDamp = 0.05f;

    [System.Serializable]
    public struct AltitudeKey
    {
        [Tooltip("시점 (초 단위 T+)")]
        public float tPlusSeconds;

        [Tooltip("해당 시점의 고도(km)")]
        public float altitudeKm;
    }

    [Header("Altitude Keys (T+ vs km)")]
    [SerializeField] private AltitudeKey[] altitudeKeys =
    {
        // 0:00   0 km
        new AltitudeKey { tPlusSeconds = 0f,   altitudeKm = 0f   },
        // 2:05   64.5 km
        new AltitudeKey { tPlusSeconds = 2f*60f + 5f,  altitudeKm = 64.5f },
        // 2:31   100 km
        new AltitudeKey { tPlusSeconds = 2f*60f + 31f, altitudeKm = 100f  },
        // 3:45   200 km
        new AltitudeKey { tPlusSeconds = 3f*60f + 45f, altitudeKm = 200f  },
        // 3:56   210 km
        new AltitudeKey { tPlusSeconds = 3f*60f + 56f, altitudeKm = 210f  },
        // 4:30   261 km
        new AltitudeKey { tPlusSeconds = 4f*60f + 30f, altitudeKm = 261f  },
        // 4:54   300 km
        new AltitudeKey { tPlusSeconds = 4f*60f + 54f, altitudeKm = 300f  },
        // 6:13   400 km
        new AltitudeKey { tPlusSeconds = 6f*60f + 13f, altitudeKm = 400f  },
        // 8:15   500 km
        new AltitudeKey { tPlusSeconds = 8f*60f + 15f, altitudeKm = 500f  },
        // 12:14  550 km
        new AltitudeKey { tPlusSeconds = 12f*60f + 14f, altitudeKm = 550f }
    };

    private float _velocityY;

    private void Awake()
    {
        if (target == null) target = transform;

        if (countController == null)
        {
            if (CountController.Instance != null)
                countController = CountController.Instance;
            else
                countController = FindObjectOfType<CountController>();

            if (countController == null)
            {
                Debug.LogError("[RocketYController] Awake-> CountController를 찾지 못했습니다. 인스펙터에서 연결해 주세요.");
            }
        }
    }

    private void Update()
    {
        if (!target || altitudeKeys == null || altitudeKeys.Length == 0) return;

        float tPlus = GetTPlus();
        float altitudeKm = EvaluateAltitudeKm(tPlus);

        // 고도(km) -> Y로 스케일링
        float yTarget = baseY;
        if (maxAltitudeKm > 0.001f)
        {
            float u = Mathf.Clamp01(altitudeKm / maxAltitudeKm);
            yTarget = Mathf.Lerp(baseY, topY, u);
        }

        StepY(yTarget);
    }

    private float GetTPlus()
    {
        if (!countController) return 0f;

        try
        {
            return Mathf.Max(0f, countController.TPlusSeconds);
        }
        catch (System.SystemException e)
        {
            Debug.LogError($"[RocketYController] GetTPlus-> T+ 조회 실패: {e.Message}");
            return 0f;
        }
    }

    /// <summary> 고도 키프레임을 선형 보간해서 현재 고도(km)를 반환. </summary>
    private float EvaluateAltitudeKm(float tPlusSec)
    {
        if (altitudeKeys.Length == 1) return altitudeKeys[0].altitudeKm;

        // 범위 밖이면 양 끝값 고정
        if (tPlusSec <= altitudeKeys[0].tPlusSeconds)
            return altitudeKeys[0].altitudeKm;

        int last = altitudeKeys.Length - 1;
        if (tPlusSec >= altitudeKeys[last].tPlusSeconds)
            return altitudeKeys[last].altitudeKm;

        // 사이에 있는 두 키 찾기
        for (int i = 0; i < last; i++)
        {
            AltitudeKey k0 = altitudeKeys[i];
            AltitudeKey k1 = altitudeKeys[i + 1];

            if (tPlusSec >= k0.tPlusSeconds && tPlusSec <= k1.tPlusSeconds)
            {
                float u = Mathf.InverseLerp(k0.tPlusSeconds, k1.tPlusSeconds, tPlusSec);
                return Mathf.Lerp(k0.altitudeKm, k1.altitudeKm, u);
            }
        }

        // 이론상 여기까지 오면 안 오지만, 방어 코드
        return altitudeKeys[last].altitudeKm;
    }

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
            lp.y = newY;
            target.localPosition = lp;
        }
        else
        {
            Vector3 p = target.position;
            float newY = Mathf.SmoothDamp(p.y, targetYValue, ref _velocityY, smoothDamp);
            p.y = newY;
            target.position = p;
        }
    }

    private void SnapY(float y)
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

        _velocityY = 0f;
    }
}
