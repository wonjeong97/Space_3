using System.Collections;
using UnityEngine;

public class JetVFXAnim : MonoBehaviour
{
    [Header("Target GameObjects")]
    [SerializeField] private GameObject targetA;
    [SerializeField] private GameObject targetB;
    [SerializeField] private GameObject targetC;

    [Header("Animation Settings")]
    [SerializeField] private float animDuration = 0.6f;
    [SerializeField] private AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private Vector3 _originalScaleA;
    private Vector3 _originalScaleB;
    private Vector3 _originalScaleC;

    private void Awake()
    {
        if (targetA) _originalScaleA = targetA.transform.localScale;
        if (targetB) _originalScaleB = targetB.transform.localScale;
        if (targetC) _originalScaleC = targetC.transform.localScale;
    }

    /// <summary> target들을 0 → 원래 크기로 확장 </summary>
    public void Expand()
    {
        StopAllCoroutines();
        if (targetA) StartCoroutine(ScaleAnim(targetA.transform, Vector3.zero, _originalScaleA));
        if (targetB) StartCoroutine(ScaleAnim(targetB.transform, Vector3.zero, _originalScaleB));
        if (targetC) StartCoroutine(ScaleAnim(targetC.transform, Vector3.zero, _originalScaleC));
    }

    /// <summary> target들을 원래 크기 → 0으로 축소 </summary>
    public void Shrink()
    {
        StopAllCoroutines();
        if (targetA) StartCoroutine(ScaleAnim(targetA.transform, _originalScaleA, Vector3.zero));
        if (targetB) StartCoroutine(ScaleAnim(targetB.transform, _originalScaleB, Vector3.zero));
        if (targetC) StartCoroutine(ScaleAnim(targetC.transform, _originalScaleC, Vector3.zero));
    }

    /// <summary> 스케일 보간 애니메이션 </summary>
    private IEnumerator ScaleAnim(Transform target, Vector3 from, Vector3 to)
    {
        float t = 0f;
        target.localScale = from;

        while (t < animDuration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / animDuration);
            float eased = curve.Evaluate(u);
            target.localScale = Vector3.LerpUnclamped(from, to, eased);
            yield return null;
        }

        target.localScale = to;
    }
}