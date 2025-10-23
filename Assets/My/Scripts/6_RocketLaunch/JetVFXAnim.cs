using System.Collections;
using UnityEngine;

public class JetVFXAnim : MonoBehaviour
{
    [SerializeField] private float duration = 2.5f; // 애니메이션 시간
    private Vector3 _originalScale;

    private void Awake()
    {
        // 최초 스케일 저장
        _originalScale = transform.localScale;
    }

    private void OnEnable()
    {
        // 시작 시 0에서 원래 크기로 복원
        Vector3 startScale = _originalScale;
        startScale.x = 0f;
        transform.localScale = startScale;

        StartCoroutine(ScaleAnim(Vector3.zero, new Vector3(_originalScale.x, _originalScale.y, _originalScale.z), duration));
    }

    /// <summary> X축 스케일을 from → to 로 time 동안 변화 </summary>
    private IEnumerator ScaleAnim(Vector3 from, Vector3 to, float time)
    {
        float elapsed = 0f;

        while (elapsed < time)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / time);

            transform.localScale = Vector3.Lerp(from, to, t);

            yield return null;
        }

        transform.localScale = to;
    }

    /// <summary> 외부에서 호출: 원래 크기 → 0 으로 줄어들기 </summary>
    public void Shrink()
    {
        StopAllCoroutines();
        ;
        StartCoroutine(ScaleAnim(new Vector3(_originalScale.x, _originalScale.y, _originalScale.z), Vector3.zero, duration)); 
    }

    /// <summary> 외부에서 호출: 0 → 원래 크기로 늘어나기 </summary>
    public void Expand()
    {
        StopAllCoroutines();
        StartCoroutine(ScaleAnim(Vector3.zero, new Vector3(_originalScale.x, _originalScale.y, _originalScale.z), duration));
    }
}