using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class RocketLaunch : MonoBehaviour
{
    [Header("VFX Roots")]
    public List<GameObject> jetEngineVFX = new List<GameObject>();
    public GameObject flamesLight;
    public GameObject launcher;

    [Header("Particles")]
    public ParticleSystem turbulenceSmokeParticles;
    public ParticleSystem flamesAParticles;
    public ParticleSystem flamesBParticles;
    public ParticleSystem sparksParticles;
    public ParticleSystem takeOffSmokeParticles;
    public GameObject launchSmoke;

    [Header("Timing (fallbacks)")]
    [Tooltip("CountController가 없을 때 사용할 대기(초)")]
    public int startDelay = 10;
    public int engineWarmupTime = 6;
    public int launchEndTimer = 8;

    [Header("T- Gate")]
    [Tooltip("이 값(초) 이하의 T- 가 되면 시퀀스 시작. 예: 2 -> T-00:00:02")]
    [SerializeField] private float startAtTMinusSeconds = 2f;

    private ParticleSystem _launchSmokeParticle;

    private void Awake()
    {
        _launchSmokeParticle = launchSmoke ? launchSmoke.GetComponent<ParticleSystem>() : null;
    }

    private void Start()
    {
        SafeSetActive(flamesLight, false);

        foreach (GameObject vfx in jetEngineVFX)
            SafeSetActive(vfx, false);
    }

    public void Call()
    {
        StartCoroutine(LaunchRocket());
    }

    private IEnumerator LaunchRocket()
    {
        LogUtil.Log(nameof(RocketLaunch), nameof(LaunchRocket), "시퀀스 시작 요청");

        // 0) 사전 연기
        SafePlay(takeOffSmokeParticles);

        // 1) CountController가 있으면 T- 게이트까지 대기, 없으면 기존 startDelay 사용
        if (CountController.Instance)
        {
            float gate = Mathf.Max(0f, startAtTMinusSeconds);

            // 이미 T+일 경우 즉시 진행
            if (CountController.Instance.IsCountingDown)
            {
                LogUtil.Log(nameof(RocketLaunch), nameof(LaunchRocket), $"T- 게이트 대기 시작 (<= {gate:0.00}s)");
                while (CountController.Instance &&
                       CountController.Instance.IsCountingDown &&
                       CountController.Instance.TMinusSeconds > gate)
                {
                    yield return null;
                }
                LogUtil.Log(nameof(RocketLaunch), nameof(LaunchRocket), $"T- 게이트 통과 (현재 T- {CountController.Instance?.TMinusSeconds:0.00}s)");
            }
            else
            {
                LogUtil.Log(nameof(RocketLaunch), nameof(LaunchRocket), "이미 T+ 상태, 즉시 진행");
            }
        }
        else
        {
            LogUtil.LogWarn(nameof(RocketLaunch), nameof(LaunchRocket), $"CountController 없음 -> startDelay {startDelay}s 폴백");
            yield return new WaitForSeconds(Mathf.Max(0, startDelay));
        }

        // 2) 1단계: 스테이지 페이드, 불빛/연기/스파크 점화
        LaunchManager.Instance?.FadeInStagePublicAsync(1).Forget();

        SafeSetActive(flamesLight, true);
        SafePlay(turbulenceSmokeParticles);
        SafePlay(flamesAParticles);
        SafePlay(flamesBParticles);
        SafePlay(sparksParticles);

        // 3) 엔진 워밍업(선택) – CountController가 있든 없든 일관되게 대기
        if (engineWarmupTime > 0)
            yield return new WaitForSeconds(engineWarmupTime);

        // 4) 이륙 연기 및 제트 배기 VFX 온
        SafePlay(_launchSmokeParticle);

        foreach (GameObject vfx in jetEngineVFX)
            SafeSetActive(vfx, true);

        // 5) 종료 타이머 후 정리
        if (launchEndTimer > 0)
            yield return new WaitForSeconds(launchEndTimer);

        SafeSetActive(flamesLight, false);
        SafeStop(turbulenceSmokeParticles);
        SafeStop(flamesAParticles);
        SafeStop(flamesBParticles);
        SafeStop(sparksParticles);

        SafeDestroy(flamesLight);
        SafeDestroy(turbulenceSmokeParticles);
        SafeDestroy(flamesAParticles);
        SafeDestroy(flamesBParticles);
        SafeDestroy(sparksParticles);
        //SafeDestroy(launcher);

        LogUtil.Log(nameof(RocketLaunch), nameof(LaunchRocket), "시퀀스 종료");
    }

    // -------------------------
    // Helpers
    // -------------------------

    private static void SafeSetActive(GameObject go, bool on)
    {
        if (go) go.SetActive(on);
    }

    private static void SafePlay(ParticleSystem ps)
    {
        if (ps) ps.Play();
    }

    private static void SafeStop(ParticleSystem ps)
    {
        if (ps) ps.Stop();
    }

    private static void SafeDestroy(Object obj)
    {
        if (obj != null) Object.Destroy(obj);
    }
}
