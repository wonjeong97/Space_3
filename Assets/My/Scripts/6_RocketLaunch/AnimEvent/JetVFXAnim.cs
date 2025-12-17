using System.Collections;
using UnityEngine;

public class JetVFXAnim : MonoBehaviour
{
    [Header("Target GameObjects")]
    [SerializeField] private GameObject targetA;
    [SerializeField] private GameObject targetB;
    [SerializeField] private GameObject targetC;
    [SerializeField] private GameObject addtionalSmoke;

    [Header("Animation Settings")]
    [SerializeField] private float animDuration = 0.3f;
    [SerializeField] private AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    
    [Header("Smoke Direction")]
    [SerializeField] private Transform directionRef;
    [SerializeField] private float smokeSpeed = 2f;

    private Vector3 _originalScaleA;
    private Vector3 _originalScaleB;
    private Vector3 _originalScaleC;
    private Vector3 _originalScaleD;

    private ParticleSystem _ps1;
    private ParticleSystem _ps2;
    private ParticleSystem _ps3;
    private ParticleSystem _smokePs;

    private void Awake()
    {
        if (targetA && targetB && targetC)
        {
            _originalScaleA = targetA.transform.localScale;
            _originalScaleB = targetB.transform.localScale;
            _originalScaleC = targetC.transform.localScale;
            if (addtionalSmoke) _originalScaleD = addtionalSmoke.transform.localScale;
            
            _ps1 = targetA.GetComponent<ParticleSystem>();
            _ps2 = targetB.GetComponent<ParticleSystem>();
            _ps3 = targetC.GetComponent<ParticleSystem>();
            if (addtionalSmoke) _smokePs = addtionalSmoke.GetComponent<ParticleSystem>();
        }
    }

    /// <summary> target들을 0 → 원래 크기로 확장 </summary>
    public void Expand()
    {
        if (!targetA || !targetB || !targetC) return;
        StopAllCoroutines();
        
        _ps1.Play();
        _ps2.Play();
        if (_ps3) {
            _ps3.Play();
            if (directionRef) StartCoroutine(ParticleUtil.FollowDirectionalSmokeRoutine(_ps3, directionRef, smokeSpeed));
        }

        if (_smokePs)
        {
            _smokePs.Play();
            if (directionRef) StartCoroutine(ParticleUtil.FollowDirectionalSmokeRoutine(_smokePs, directionRef, smokeSpeed));
        }
        
        StartCoroutine(ScaleAnim(targetA.transform, Vector3.zero, _originalScaleA));
        StartCoroutine(ScaleAnim(targetB.transform, Vector3.zero, _originalScaleB));
        StartCoroutine(ScaleAnim(targetC.transform, Vector3.zero, _originalScaleC));
        if (addtionalSmoke)
        {
            StartCoroutine(ScaleAnim(addtionalSmoke.transform, Vector3.zero, _originalScaleD));    
        }
    }

    /// <summary> target들을 원래 크기 → 0으로 축소 </summary>
    public void Shrink()
    {
        if (targetA) StartCoroutine(ScaleAnim(targetA.transform, _originalScaleA, Vector3.zero));
        if (targetB) StartCoroutine(ScaleAnim(targetB.transform, _originalScaleB, Vector3.zero));
        if (targetC) StartCoroutine(ScaleAnim(targetC.transform, _originalScaleC, Vector3.zero));
        if (addtionalSmoke) _smokePs.Stop();
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