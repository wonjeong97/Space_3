using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

public class NuriAnimEvent : MonoBehaviour
{
    [Header("Animator")] [SerializeField] private Animator separateAnimator;

    [Header("Bottom Smoke")]
    [SerializeField] private ParticleSystem bottomSmoke;

    [Header("Fairing")]
    [SerializeField] private GameObject fairing1;
    [SerializeField] private GameObject fairing2;
    [SerializeField] private ParticleSystem fairingSmoke;
    [SerializeField] private ParticleSystem fairingSmokeVertical;

    [Header("Stage1")]
    [SerializeField] private GameObject stage1;
    [SerializeField] private GameObject stage1Flame;
    [SerializeField] private ParticleSystem stage1Smoke;

    [Header("Stage2")]
    [SerializeField] private GameObject stage2;
    [SerializeField] private GameObject stage2Flame;
    [SerializeField] private ParticleSystem stage2Smoke;

    [Header("Stage3")]
    [SerializeField] private GameObject stage3;
    [SerializeField] private GameObject stage3Flame;

    public CollisionDetectionMode collisionMode = CollisionDetectionMode.ContinuousDynamic;
    public RigidbodyInterpolation interpolation = RigidbodyInterpolation.Interpolate;
    
    private JetVFXAnim _jetVFXAnimStage1;
    private JetVFXAnim _jetVFXAnimStage2;
    private JetVFXAnim _jetVFXAnimStage3;

    private void Awake()
    {
        _jetVFXAnimStage1 = stage1Flame.GetComponent<JetVFXAnim>();
        _jetVFXAnimStage2 = stage2Flame.GetComponent<JetVFXAnim>();
        _jetVFXAnimStage3 = stage3Flame.GetComponent<JetVFXAnim>();
    }

    public void StartBottomSmoke()
    {
        if (bottomSmoke && !bottomSmoke.isPlaying)
        {
            bottomSmoke.Play();
        }
    }

    public void StopBottomSmoke()
    {
        if (!bottomSmoke || !bottomSmoke.isPlaying)
            return;

        // 1) 루프 끄기 -> 자연스럽게 줄어들도록
        var main = bottomSmoke.main;
        main.loop = false;

        // 2) 5초 뒤 완전 정지
        StartCoroutine(StopAfterDelay(bottomSmoke, 5f));
    }

    private IEnumerator StopAfterDelay(ParticleSystem ps, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (ps)
        {
            ps.Stop();
        }
    }

    /// <summary> 1단 분리: 1단 제트 축소 → 연기 → 분리 → 2단 점화/확장 → 폐기 </summary>
    public async UniTask DropStage1()
    {
        var token = this.GetCancellationTokenOnDestroy();
        try
        {
            if (!stage1 || !stage1Smoke || !stage1Flame) return;
            
            stage1Smoke?.Play();
            _jetVFXAnimStage1?.Shrink();
            
            await UniTask.Delay(3000, cancellationToken: token);

            separateAnimator?.SetTrigger("Stage01");

            await UniTask.Delay(3000, cancellationToken: token);
            
            _jetVFXAnimStage2?.Expand();

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
            if (!stage2 || !stage2Smoke || !stage2Flame) return;

            _jetVFXAnimStage2?.Shrink();
            stage2Smoke?.Play();
            
            await UniTask.Delay(3000, cancellationToken: token);

            separateAnimator?.SetTrigger("Stage02");

            await UniTask.Delay(3000, cancellationToken: token);

            _jetVFXAnimStage3.Expand();

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
            if (!stage3 || !stage3Flame) return;
            
            _jetVFXAnimStage3?.Shrink();
            
            await UniTask.Delay(7000, cancellationToken: token);
           
            separateAnimator?.SetTrigger("Stage03");
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
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }

    public void PlaySeparateSound()
    {
        SoundManager.Instance?.PlayByKey("분리");
    }

    public void PlayRocketEngineSound(string soundKey)
    {
        SoundManager.Instance?.CrossFadeByKey(soundKey, loop: true);
    }
}