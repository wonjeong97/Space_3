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
    [Header("Fairing")]
    [SerializeField] private GameObject fairing1;
    [SerializeField] private GameObject fairing2;
    [SerializeField] private ParticleSystem fairingSmoke;

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

    /// <summary> 1단 분리: 1단 제트 축소 → 연기 → 분리 → 2단 점화/확장 → 폐기 </summary>
    public async UniTask DropStage1()
    {
        var token = this.GetCancellationTokenOnDestroy();
        try
        {
            if (!stage1 || !stage1Smoke) return;
            
            LaunchManager.Instance?.FocusImage3ThenPingPong4();
            LaunchManager.Instance?.FadeInStagePublicAsync(2).Forget();
            
            foreach (GameObject vfx in stage1VfXs)
            {
                if (vfx && vfx.TryGetComponent(out JetVFXAnim anim))
                {
                    anim.Shrink();
                }
            }

            stage1Smoke?.Play();

            await UniTask.Delay(2000, cancellationToken: token);

            stage1.transform.SetParent(null, true);
            if (stage1.TryGetComponent(out Rigidbody rb))
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.collisionDetectionMode = collisionMode;
                rb.interpolation = interpolation;
            }

            await UniTask.Delay(300, cancellationToken: token);

            foreach (var vfx in stage2VfXs)
            {
                var root = vfx.rootObject;
                var flameA = vfx.flameA;
                var flameB = vfx.flameB;

                if (root && root.TryGetComponent(out JetVFXAnim anim) && flameA && flameB)
                {
                    flameA.Play();
                    flameB.Play();
                    anim.Expand();
                }
            }

            await UniTask.Delay(5000, cancellationToken: token);

            if (stage1) Destroy(stage1);
        }
        catch (OperationCanceledException)
        {
            // 씬 파괴/전환 시 정상 취소
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }

    /// <summary> 페어링 분리: 연기 → 분리/물리활성 → 연기정지 → 파편 제거 </summary>
    public async UniTask SeparateFairing()
    {
        var token = this.GetCancellationTokenOnDestroy();
        try
        {
            if (!fairing1 || !fairing2) return;

            fairingSmoke?.Play();
            LaunchManager.Instance?.FadeInStagePublicAsync(3).Forget();
            await UniTask.Delay(4000, cancellationToken: token);

            int rocketLayer = LayerMask.NameToLayer("Nuri");
            int rocketMask = 1 << rocketLayer;

            fairing1.transform.SetParent(null, true);
            fairing2.transform.SetParent(null, true);

            var rb1 = fairing1.GetComponent<Rigidbody>();
            var rb2 = fairing2.GetComponent<Rigidbody>();

            if (rb1) rb1.excludeLayers &= ~rocketMask;
            if (rb2) rb2.excludeLayers &= ~rocketMask;

            foreach (var col in fairing1.GetComponentsInChildren<Collider>(true))
            {
                col.excludeLayers &= ~rocketMask;
                col.includeLayers = ~0;
            }
            foreach (var col in fairing2.GetComponentsInChildren<Collider>(true))
            {
                col.excludeLayers &= ~rocketMask;
                col.includeLayers = ~0;
            }

            if (rb1)
            {
                rb1.isKinematic = false;
                rb1.useGravity = true;
                rb1.collisionDetectionMode = collisionMode;
                rb1.interpolation = interpolation;
            }

            if (rb2)
            {
                rb2.isKinematic = false;
                rb2.useGravity = true;
                rb2.collisionDetectionMode = collisionMode;
                rb2.interpolation = interpolation;
            }

            await UniTask.Delay(2000, cancellationToken: token);
            fairingSmoke?.Stop();

            await UniTask.Delay(4000, cancellationToken: token);

            if (fairingSmoke) Destroy(fairingSmoke.gameObject);
            if (fairing1) Destroy(fairing1);
            if (fairing2) Destroy(fairing2);
        }
        catch (OperationCanceledException)
        {
        }
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
            // 4 고정, 5 핑퐁
            LaunchManager.Instance?.FocusImage4ThenPingPong5();
            LaunchManager.Instance?.FadeInStagePublicAsync(4).Forget();
            
            foreach (var vfx in stage2VfXs)
            {
                var root = vfx.rootObject;
                if (root && root.TryGetComponent(out JetVFXAnim anim))
                {
                    anim.Shrink();
                }
            }

            stage2Smoke?.Play();

            await UniTask.Delay(2000, cancellationToken: token);

            stage2.transform.SetParent(null, true);
            if (stage2.TryGetComponent(out Rigidbody rb))
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.collisionDetectionMode = collisionMode;
                rb.interpolation = interpolation;
            }

            foreach (var vfx in stage3VfXs)
            {
                var root = vfx.rootObject;
                var flameA = vfx.flameA;
                var flameB = vfx.flameB;

                if (root && root.TryGetComponent(out JetVFXAnim anim) && flameA && flameB)
                {
                    flameA.Play();
                    flameB.Play();
                    anim.Expand();
                }
            }
            LaunchManager.Instance?.FadeInStagePublicAsync(5).Forget();

            await UniTask.Delay(5000, cancellationToken: token);

            if (stage2) Destroy(stage2);
        }
        catch (OperationCanceledException)
        {
        }
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
        catch (OperationCanceledException)
        {
            // 씬 전환/오브젝트 파괴 시 정상 취소
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }
}
