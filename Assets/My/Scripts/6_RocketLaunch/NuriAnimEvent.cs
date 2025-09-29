using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class NuriAnimEvent : MonoBehaviour
{
    [Header("Fairing")]
    [SerializeField] private GameObject fairing1;
    [SerializeField] private GameObject fairing2;
    
    [Header("Stage1")]
    [SerializeField] private GameObject stage1;
    [SerializeField] private List<GameObject> stage1VFXs = new List<GameObject>();
    
    [Header("Stage2")]
    [SerializeField] private GameObject stage2;
    
    [Header("Stage3")]
    [SerializeField] private GameObject stage3;
    
    public CollisionDetectionMode collisionMode = CollisionDetectionMode.ContinuousDynamic;
    public RigidbodyInterpolation interpolation = RigidbodyInterpolation.Interpolate;
    
    public async Task DropStage1()
    {
        try
        {
            if (!stage1) return;

            foreach (GameObject vfx in stage1VFXs)
            {   
                if (vfx.TryGetComponent(out JetVFXAnim anim))
                {
                    anim.Shrink();   
                }
            }
        
            await Task.Delay(2000);
        
            stage1.transform.SetParent(null, true);
            var rb = stage1.GetComponent<Rigidbody>();
        
            // 물리 시뮬
            rb.isKinematic = false;                    
            rb.useGravity = true;
            rb.collisionDetectionMode = collisionMode;
            rb.interpolation = interpolation;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public void SeparateFairing()
    {
        if (!fairing1 || !fairing2) return;
        
        int rocketLayer = LayerMask.NameToLayer("Nuri");
        int rocketMask = 1 << rocketLayer;
        
        fairing1.transform.SetParent(null, true);
        fairing2.transform.SetParent(null, true);
        
        var rb1 = fairing1.GetComponent<Rigidbody>();
        var rb2 = fairing2.GetComponent<Rigidbody>();
        
        rb1.excludeLayers &= ~rocketMask;
        rb2.excludeLayers &= ~rocketMask;
        
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
        
        // 물리 시뮬
        rb1.isKinematic = false;                    
        rb1.useGravity = true;
        rb1.collisionDetectionMode = collisionMode;
        rb1.interpolation = interpolation;
        
        rb2.isKinematic = false;
        rb2.useGravity = true;
        rb2.collisionDetectionMode = collisionMode;
        rb2.interpolation = interpolation;
    }

    public void DropStage2()
    {
        if (!stage2) return;
        
        stage2.transform.SetParent(null, true);
        var rb = stage2.GetComponent<Rigidbody>();
        
        // 물리 시뮬
        rb.isKinematic = false;                    
        rb.useGravity = true;
        rb.collisionDetectionMode = collisionMode;
        rb.interpolation = interpolation;
    }
}
