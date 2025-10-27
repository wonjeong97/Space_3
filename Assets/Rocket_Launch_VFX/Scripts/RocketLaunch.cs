using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class RocketLaunch : MonoBehaviour
{
    public Animation rocketLaunchAnim;
    public List<GameObject> jetEngineVFX = new List<GameObject>();

    public GameObject flamesLight;
    public GameObject canyon;

    public ParticleSystem turbulenceSmokeParticles;
    public ParticleSystem flamesAParticles;
    public ParticleSystem flamesBParticles;
    public ParticleSystem sparksParticles;
    public ParticleSystem takeOffSmokeParticles;
    
    public GameObject launchSmoke;

    public int startDelay = 10;
    public int engineWarmupTime = 6;
    public int launchEndTimer = 8;

    private ParticleSystem _launchSmokeParticle;
    private Animation _launchSmokeAnim;

    private void Awake()
    {
        _launchSmokeParticle = launchSmoke?.GetComponent<ParticleSystem>();
        _launchSmokeAnim = launchSmoke?.GetComponent<Animation>();
    }

    private void Start()
    {
        flamesLight.SetActive(false);

        foreach (GameObject vfx in jetEngineVFX)
        {
            vfx.SetActive(false);
        }
    }

    public void Call()
    {
        StartCoroutine(LaunchRocket());
    }

    private IEnumerator LaunchRocket()
    {
        takeOffSmokeParticles.Play();
        yield return new WaitForSeconds(startDelay); // 9
        LaunchManager.Instance?.FadeInStagePublicAsync(1).Forget();

        flamesLight.SetActive(true);
        turbulenceSmokeParticles.Play();
        flamesAParticles.Play();
        flamesBParticles.Play();
        sparksParticles.Play();
        yield return new WaitForSeconds(engineWarmupTime); // 2

        _launchSmokeParticle.Play();
        _launchSmokeAnim.Play();
        rocketLaunchAnim?.Play();

        foreach (GameObject vfx in jetEngineVFX)
        {
            vfx.SetActive(true);
        }

        yield return new WaitForSeconds(launchEndTimer);
        flamesLight.SetActive(false);
        turbulenceSmokeParticles.Stop();
        flamesAParticles.Stop();
        flamesBParticles.Stop();
        sparksParticles.Stop();

        Destroy(flamesLight);
        Destroy(turbulenceSmokeParticles);
        Destroy(flamesAParticles);
        Destroy(flamesBParticles);
        Destroy(sparksParticles);
        Destroy(canyon);
    }
}