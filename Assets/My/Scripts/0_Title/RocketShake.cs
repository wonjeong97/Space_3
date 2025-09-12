using UnityEngine;

/// <summary>
/// 로켓 오브젝트에 흔들림(진동) 효과를 주는 스크립트
/// </summary>
public class RocketShake : MonoBehaviour
{
    [Header("Shake Settings")]
    [SerializeField] private float intensity = 0.1f;   // 흔들림 강도
    [SerializeField] private float frequency = 20f;    // 흔들림 속도
    [SerializeField] private bool playOnStart = false; // 시작 시 자동 재생 여부

    private Vector3 originalPos;   // 원래 위치
    private bool isShaking;

    private void Awake()
    {
        originalPos = transform.localPosition;
    }

    private void OnEnable()
    {
        if (playOnStart) StartShake();
    }

    private void Update()
    {
        if (isShaking)
        {
            float offsetX = (Mathf.PerlinNoise(Time.time * frequency, 0f) - 0.5f) * 2f * intensity;
            float offsetY = (Mathf.PerlinNoise(0f, Time.time * frequency) - 0.5f) * 2f * intensity;
            float offsetZ = (Mathf.PerlinNoise(Time.time * frequency, Time.time * frequency) - 0.5f) * 2f * intensity;

            transform.localPosition = originalPos + new Vector3(offsetX, offsetY, offsetZ);
        }
    }

    /// <summary>
    /// 흔들림 시작
    /// </summary>
    private void StartShake()
    {
        if (!isShaking)
        {
            originalPos = transform.localPosition;
            isShaking = true;
        }
    }

    /// <summary>
    /// 흔들림 정지
    /// </summary>
    private void StopShake()
    {
        isShaking = false;
        transform.localPosition = originalPos;
    }

    /// <summary>
    /// 지정한 시간 동안 흔들림 (코루틴 기반)
    /// </summary>
    public void ShakeForSeconds(float duration)
    {
        StartShake();
        CancelInvoke(nameof(StopShake));
        Invoke(nameof(StopShake), duration);
    }
}