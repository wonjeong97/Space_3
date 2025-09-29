using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RocketLaunch : MonoBehaviour
{
    public GameObject rocketLaunchAnim;
    public List<GameObject> jetEngineVFX = new List<GameObject>();

    public GameObject flamesLight;

    public ParticleSystem turbulence_Smoke_Particles;
    public ParticleSystem flames_A_Particles;
    public ParticleSystem flames_B_Particles;
    public ParticleSystem sparks_Particles;
    public ParticleSystem takeOff_Smoke_Particles;
    public ParticleSystem launch_Smoke_Particles;

    public int startDelay = 10;
    public int engineWarmupTime = 6;
    public int launchEndTimer = 8;

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
        launch_Smoke_Particles.Play();
        yield return new WaitForSeconds(startDelay);

        flamesLight.SetActive(true);
        turbulence_Smoke_Particles.Play();
        flames_A_Particles.Play();
        flames_B_Particles.Play();
        sparks_Particles.Play();
        yield return new WaitForSeconds(engineWarmupTime);

        takeOff_Smoke_Particles.Play();
        rocketLaunchAnim.GetComponent<Animation>().Play();
        launch_Smoke_Particles.Stop();
        
        foreach (GameObject vfx in jetEngineVFX)
        {
            vfx.SetActive(true);
        }
        yield return new WaitForSeconds(launchEndTimer);

        flamesLight.SetActive(false);
        turbulence_Smoke_Particles.Stop();
        flames_A_Particles.Stop();
        flames_B_Particles.Stop();
        sparks_Particles.Stop();

    }

}