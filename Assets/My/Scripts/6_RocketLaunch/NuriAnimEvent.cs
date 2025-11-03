using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

[Serializable]
public struct JetEngine
{
    public GameObject rootObject;
    public ParticleSystem flameA;
    public ParticleSystem flameB;
}

public class NuriAnimEvent : MonoBehaviour
{
    [Header("Animator")] 
    [SerializeField] private Animator separateAnimator;
    
    [Header("Fairing")]
    [SerializeField] private GameObject fairing1;
    [SerializeField] private GameObject fairing2;
    [SerializeField] private ParticleSystem fairingSmoke;
    [SerializeField] private ParticleSystem fairingSmokeVertical;

    [Header("Stage1")]
    [SerializeField] private GameObject stage1;
    [SerializeField] private List<GameObject> stage1VfXs = new List<GameObject>();
    [SerializeField] private ParticleSystem stage1Smoke;

    [Header("Stage2")]
    [SerializeField] private GameObject stage2;
    [SerializeField] private List<JetEngine> stage2VfXs = new List<JetEngine>();
    [SerializeField] private ParticleSystem stage2Smoke;

    [Header("Stage3")]
    [SerializeField] private GameObject stage3;
    [SerializeField] private List<JetEngine> stage3VfXs = new List<JetEngine>();

    public CollisionDetectionMode collisionMode = CollisionDetectionMode.ContinuousDynamic;
    public RigidbodyInterpolation interpolation = RigidbodyInterpolation.Interpolate;

    [ContextMenu("test call")]
    public void Test()
    {
        separateAnimator?.SetTrigger("Stage01");
    }
    
    /// <summary> 1단 분리: 1단 제트 축소 → 연기 → 분리 → 2단 점화/확장 → 폐기 </summary>
    public async UniTask DropStage1()
    {
        var token = this.GetCancellationTokenOnDestroy();
        try
        {
            if (!stage1 || !stage1Smoke) return;
            
            foreach (GameObject vfx in stage1VfXs)
            {
                if (vfx && vfx.TryGetComponent(out JetVFXAnim anim))
                {
                    anim.Shrink();
                }
            }

            stage1Smoke?.Play();
            await UniTask.Delay(3000, cancellationToken: token);
            
            separateAnimator?.SetTrigger("Stage01");
            
            await UniTask.Delay(3000, cancellationToken: token);

            foreach (JetEngine vfx in stage2VfXs)
            {
                GameObject root = vfx.rootObject;
                ParticleSystem flameA = vfx.flameA;
                ParticleSystem flameB = vfx.flameB;

                if (root && root.TryGetComponent(out JetVFXAnim anim) && flameA && flameB)
                {
                    flameA.Play();
                    flameB.Play();
                    anim.Expand();
                }
            }

            await UniTask.Delay(10000, cancellationToken: token);

            if (stage1) Destroy(stage1);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }

    /// <summary> 페어링 분리: 연기 → 분리/물리활성 → 연기정지 → 파편 제거 </summary>
    public async UniTask SeparateFairing()
    {
        CancellationToken token = this.GetCancellationTokenOnDestroy();
        try
        {
            if (!fairing1 || !fairing2) return;

            fairingSmoke?.Play();
            fairingSmokeVertical?.Play();
            await UniTask.Delay(3000, cancellationToken: token);
            
            separateAnimator?.SetTrigger("Fairing");
           
            await UniTask.Delay(2000, cancellationToken: token);
            fairingSmoke?.Stop();
            fairingSmokeVertical?.Stop();

            await UniTask.Delay(2000, cancellationToken: token);

            if (fairingSmoke) Destroy(fairingSmoke.gameObject);
            if (fairing1) Destroy(fairing1);
            if (fairing2) Destroy(fairing2);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }

    /// <summary> 2단 분리: 2단 제트 축소 → 연기 → 분리 → 3단 점화/확장 → 폐기 </summary>
    public async UniTask DropStage2()
    {
        var token = this.GetCancellationTokenOnDestroy();
        try
        {
            if (!stage2) return;
            
            foreach (JetEngine vfx in stage2VfXs)
            {
                GameObject root = vfx.rootObject;
                if (root && root.TryGetComponent(out JetVFXAnim anim))
                {
                    anim.Shrink();
                }
            }

            stage2Smoke?.Play();
            await UniTask.Delay(3000, cancellationToken: token);

            separateAnimator?.SetTrigger("Stage02");
            
            await UniTask.Delay(3000, cancellationToken: token);
            
            foreach (JetEngine vfx in stage3VfXs)
            {
                GameObject root = vfx.rootObject;
                ParticleSystem flameA = vfx.flameA;
                ParticleSystem flameB = vfx.flameB;

                if (root && root.TryGetComponent(out JetVFXAnim anim) && flameA && flameB)
                {
                    flameA.Play();
                    flameB.Play();
                    anim.Expand();
                }
            }
            await UniTask.Delay(10000, cancellationToken: token);
            if (stage2) Destroy(stage2);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }

    public async UniTask Stage3Off()
    {
        CancellationToken token = this.GetCancellationTokenOnDestroy();
        try
        {
            if (!stage3) return;
            
            foreach (JetEngine vfx in stage3VfXs)
            {
                var root = vfx.rootObject;
                if (root && root.TryGetComponent(out JetVFXAnim anim))
                {
                    anim.Shrink();
                }
            }
            
            await UniTask.Delay(5000, cancellationToken: token);
            
            foreach (JetEngine vfx in stage3VfXs)
            {
                GameObject root = vfx.rootObject;
                if (root)
                {
                    Destroy(root);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }

    /// <summary> 다음 씬 호출 </summary>
    public async UniTask CallNextScene()
    {
        CancellationToken token = this.GetCancellationTokenOnDestroy();
        if (LaunchManager.Instance == null) return;

        try
        {
            await LaunchManager.Instance.LoadNextSceneAsync().AttachExternalCancellation(token);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }
}
