using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

public class NuriAnimEvent : MonoBehaviour
{
    [Header("Animator")] 
    [SerializeField] private Animator separateAnimator;

    [Header("Bottom Smoke & Flame")] 
    [SerializeField] private ParticleSystem bottomSmoke;
    [SerializeField] private ParticleSystem rocketFlame;

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
    [SerializeField] private GameObject verticalCameraSocket;

    [Header("Vertical Cam")] 
    [SerializeField] private RocketFollowCam rfc;
    
    [Header("Smoke Direction Control")]
    [SerializeField] private Transform rocketRoot;                // 기준 방향(없으면 this.transform)
    [SerializeField] private float smokeFlowSpeed = 2f;           // 연기가 밀려나는 속도
    [SerializeField] private ParticleSystem[] directionalSmokes;  // 방향 보정할 파티클들

    public CollisionDetectionMode collisionMode = CollisionDetectionMode.ContinuousDynamic;
    public RigidbodyInterpolation interpolation = RigidbodyInterpolation.Interpolate;

    private JetVFXAnim _jetVFXAnimStage1;
    private JetVFXAnim _jetVFXAnimStage2;
    private JetVFXAnim _jetVFXAnimStage3;

    private Camera _verticalCameraInst;
    private CameraShaker _verticalCameraShake;

    private void Awake()
    {
        _jetVFXAnimStage1 = stage1Flame.GetComponent<JetVFXAnim>();
        _jetVFXAnimStage2 = stage2Flame.GetComponent<JetVFXAnim>();
        _jetVFXAnimStage3 = stage3Flame.GetComponent<JetVFXAnim>();

        _verticalCameraInst = LaunchManager.Instance.VerticalCamera;
        _verticalCameraShake = CameraShaker.Instance;
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

        var main = bottomSmoke.main;
        main.loop = false;

        StartCoroutine(StopAfterDelay(bottomSmoke, 0.1f));
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
            rfc.LerpLookAtOffset(new Vector3(2.5f, 30f, 0f ), 2f);

            await UniTask.Delay(3000, cancellationToken: token);

            _jetVFXAnimStage2?.Expand();

            await UniTask.Delay(10000, cancellationToken: token);

            if (stage1) Destroy(stage1);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }

    public void StopStage1Smoke()
    {
        stage1Smoke?.Stop();
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

            await UniTask.Delay(5000, cancellationToken: token);

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

    public void StopFairingSmoke()
    {
        fairingSmoke?.Stop();
        fairingSmokeVertical?.Stop();
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
            rfc.LerpLookAtOffset(new Vector3(4f, 30f, 0f ), 2f);

            await UniTask.Delay(3000, cancellationToken: token);
            
            _jetVFXAnimStage3.Expand();

            await UniTask.Delay(10000, cancellationToken: token);
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

    public void StopStage2Smoke()
    {
        stage2Smoke?.Stop();
    }

    public async UniTask Stage3Off()
    {
        CancellationToken token = this.GetCancellationTokenOnDestroy();
        try
        {
            if (!stage3 || !stage3Flame) return;

            _jetVFXAnimStage3?.Shrink();

            await UniTask.Delay(5000, cancellationToken: token);

            separateAnimator?.SetTrigger("Stage03");
            rfc.LerpLookAtOffset(new Vector3(6f, 30f, 0f ), 2f);
        }
        catch (OperationCanceledException)
        { }
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
        SoundManager.Instance?.PlayByKey("Separate");
    }

    public void PlayRocketEngineSound(string soundKey)
    {
        SoundManager.Instance?.CrossFadeByKey(soundKey, loop: true);
    }

    public async void SetVerticalCameraToSocket()
    {
        try
        {
            if (!_verticalCameraInst)
            {
                Debug.LogError("[NuriAnimEvent] SetVerticalCameraToSocket => verticalCameraInstance is null.");
                return;
            }

            if (!verticalCameraSocket)
            {
                Debug.LogError("[NuriAnimEvent] SetVerticalCameraToSocket => verticalCameraSocket is null.");
                return;
            }

            await LaunchManager.Instance.FadeVerticalAsync(0f, 1f);

            RocketFollowCam rfc = _verticalCameraInst.GetComponent<RocketFollowCam>();
            rfc.enabled = false;
            _verticalCameraInst.transform.SetParent(verticalCameraSocket.transform);
            _verticalCameraInst.transform.localPosition = Vector3.zero;
            _verticalCameraInst.transform.localRotation = Quaternion.identity;
            _verticalCameraInst.fieldOfView = 120f;

            await LaunchManager.Instance.FadeVerticalAsync(1f, 0f);
        }
        catch (Exception e)
        {
            Debug.LogError($"[NuriAnimEvent] SetVerticalCameraToSocket => Exception: {e}");
        }
    }

    public void PlayVCamShake()
    {
        if (_verticalCameraShake == null)
        {
            Debug.LogError("[NuriAnimEvent] PlayVCamShake => _verticalCameraShake is null.");
            return;
        }
        
        _verticalCameraShake.PlayShake();
    }

    public void StopVCamShake()
    {
        if (_verticalCameraShake == null)
        {
            Debug.LogError("[NuriAnimEvent] StopVCamShake => _verticalCameraShake is null.");
            return;
        }
        
        _verticalCameraShake.StopShake();
    }
}