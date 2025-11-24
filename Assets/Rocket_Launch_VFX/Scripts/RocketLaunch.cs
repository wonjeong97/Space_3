using System.Collections;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class RocketLaunch : MonoBehaviour
{   
    [Header("Rocket")]
    [SerializeField] private NuriAnimEvent nuriAnimEvent;
    
    [Header("VFX Roots")]
    public GameObject stage01Flame;
    public GameObject flamesLight;
    public GameObject launcher;

    [Header("Particles")] 
    public ParticleSystem engineSmokeParticles;
    public ParticleSystem turbulenceSmokeParticles;
    public ParticleSystem flamesAParticles;
    public ParticleSystem flamesBParticles;
    public ParticleSystem sparksParticles;
    public ParticleSystem takeOffSmokeParticles;
    public ParticleSystem launchSmokeParticle;

    [Header("T- Trigger Times (seconds)")]
    [Tooltip("T- 이 값 이하가 되면 사전 연기(takeOffSmoke)를 켬")]
    [SerializeField] private float tMinusPreSmoke = 10f;
    
    [Tooltip("T- 이 값 이하가 되면 Bottom Smoke를 끔")]
    [SerializeField] private float tBottomSmokeOff = 5f;

    [Tooltip("T- 이 값 이하가 되면 엔진 점화 VFX(flames, 스파크 등)를 켬")]
    [SerializeField] private float tMinusEngineOn = 1f;

    [Tooltip("T- 이 값 이하가 되면 이륙 연기 및 제트 배기 VFX를 켬 (보통 0)")]
    [SerializeField] private float tMinusLaunchVfx = 0.5f;

    [Header("T+ Stop Time (seconds)")]
    [Tooltip("T+ 이 값 이상이 되면 모든 VFX를 정리하고 시퀀스를 종료")]
    [SerializeField] private float tPlusStopVfx = 8f;

    [Header("Stop Delay Settings")]
    [Tooltip("루프를 끈 뒤 완전 Stop()까지 기다릴 시간(초)")]
    [SerializeField] private float stopDelaySeconds = 5f;

    private JetVFXAnim _stage01JetEngineVFX;

    private void Awake()
    {
        _stage01JetEngineVFX = stage01Flame ? stage01Flame.GetComponent<JetVFXAnim>() : null;
    }

    private void Start()
    {
        // 시작 시 불빛 비활성
        SafeSetActive(flamesLight, false);
    }

    public void Call()
    {
        StartCoroutine(LaunchRocket());
    }

    private IEnumerator LaunchRocket()
    {
        if (!CountController.Instance)
        {
            LogUtil.Log(nameof(RocketLaunch), nameof(LaunchRocket), "CountController 없음 -> 시퀀스 중단");
            yield break;
        }

        bool firedBottomSmokeOff = false;
        bool firedPreSmoke = false;
        bool firedEngineOn = false;
        bool firedLaunchVfx = false;
        bool finished = false;
        
        while (CountController.Instance && !finished)
        {
            if (CountController.Instance.IsCountingDown)
            {   
                // T- 구간
                float tMinus = CountController.Instance.TMinusSeconds;
                
                // 1) 사전 연기 (T- 10)
                if (!firedPreSmoke && tMinus <= tMinusPreSmoke)
                {
                    firedPreSmoke = true;
                    SafePlay(engineSmokeParticles);
                }

                if (!firedBottomSmokeOff && tMinus <= tBottomSmokeOff)
                {
                    firedBottomSmokeOff = true;
                    nuriAnimEvent?.StopBottomSmoke();
                }

                // 2) 엔진 점화 VFX (T- 1)
                if (!firedEngineOn && tMinus <= tMinusEngineOn)
                {   
                    firedEngineOn = true;
                    SafeStop(engineSmokeParticles, 0.1f);
                    
                    LaunchManager.Instance?.FadeInStagePublicAsync(1).Forget();

                    SafeSetActive(flamesLight, true);
                    SafePlay(takeOffSmokeParticles);
                    SafePlay(turbulenceSmokeParticles);
                    SafePlay(flamesAParticles);
                    SafePlay(flamesBParticles);
                    SafePlay(sparksParticles);
                    SafePlay(launchSmokeParticle);
                }

                // 3) 이륙 VFX (T- 0)
                if (!firedLaunchVfx && tMinus <= tMinusLaunchVfx)
                {   
                    Debug.Log("이륙");
                    firedLaunchVfx = true;
                    
                    // 1단 제트 플레임 확장 애니메이션
                    _stage01JetEngineVFX?.Expand();
                }
            }
            else
            {   
                // T+ 구간
                float tPlus = CountController.Instance.TPlusSeconds;

                if (tPlus >= tPlusStopVfx)
                {
                    finished = true;
                }
            }

            yield return null;
        }

        // 공통 정리: 바로 Stop()하지 말고 loop를 끄고 일정 시간 후 정지
        SafeSetActive(flamesLight, false);

        
        SafeStop(turbulenceSmokeParticles,   stopDelaySeconds);
        SafeStop(flamesAParticles,           stopDelaySeconds);
        SafeStop(flamesBParticles,           stopDelaySeconds);
        SafeStop(sparksParticles,            stopDelaySeconds);
        SafeStop(launchSmokeParticle,       stopDelaySeconds);
        SafeStop(takeOffSmokeParticles,      stopDelaySeconds);

        // 필요하다면 파티클 정지 후 Destroy까지 하고 싶을 때는
        // StopAfterDelay 안에서 Destroy까지 같이 처리하도록 확장할 수 있음.
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
        if (ps)
        {
            ps.Play();
        }
    }

    /// <summary> 루프를 끄고 일정 시간(delay) 후 완전 Stop(). </summary>
    private void SafeStop(ParticleSystem ps, float delay)
    {
        if (!ps || !ps.isPlaying) return;

        ParticleSystem.MainModule main = ps.main;
        main.loop = false;

        StartCoroutine(StopAfterDelay(ps, delay));
    }

    private IEnumerator StopAfterDelay(ParticleSystem ps, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (ps != null)
        {
            ps.Stop();
        }
    }

    private static void SafeDestroy(Object obj)
    {
        if (obj != null) Object.Destroy(obj);
    }
}
