using System.Collections;
using UnityEngine;

public class CameraShaker : MonoBehaviour
{
    public static CameraShaker Instance;
    
    [Header("Shake Settings")]
    [SerializeField] private float defaultAmplitude = 0.5f;   // 흔들림 세기
    [SerializeField] private float defaultFrequency = 20f;    // 초당 흔들림 횟수 느낌

    private Vector3 _originalLocalPos;
    private Coroutine _shakeRoutine;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _originalLocalPos = transform.localPosition;
    }

    // 인스펙터 기본값으로 흔들기 시작
    public void PlayShake()
    {
        PlayShake(defaultAmplitude, defaultFrequency);
    }

    // 파라미터로 흔들기 시작
    public void PlayShake(float amplitude, float frequency)
    {
        if (_shakeRoutine != null)
        {
            StopCoroutine(_shakeRoutine);
            transform.localPosition = _originalLocalPos;
        }

        // 현재 위치를 기준 위치로 재설정 (카메라가 이동한 뒤에도 정상 동작하도록)
        _originalLocalPos = transform.localPosition;

        _shakeRoutine = StartCoroutine(ShakeRoutine(amplitude, frequency));
    }

    // 흔들기 정지
    public void StopShake()
    {
        if (_shakeRoutine != null)
        {
            StopCoroutine(_shakeRoutine);
            _shakeRoutine = null;
        }

        transform.localPosition = _originalLocalPos;
    }

    // 실제 흔들기 코루틴 (외부에서 StopShake 호출 전까지 계속)
    private IEnumerator ShakeRoutine(float amplitude, float frequency)
    {
        while (true)
        {
            float shakeX = (Random.value * 2f - 1f) * amplitude;
            float shakeY = (Random.value * 2f - 1f) * amplitude;

            transform.localPosition = _originalLocalPos + new Vector3(shakeX, shakeY, 0f);

            float wait = (frequency > 0f) ? (1f / frequency) : 0f;
            if (wait > 0f)
            {
                yield return new WaitForSeconds(wait);
            }
            else
            {
                yield return null;
            }
        }
    }
}
