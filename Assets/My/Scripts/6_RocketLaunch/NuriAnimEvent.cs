using UnityEngine;

public class NuriAnimEvent : MonoBehaviour
{
    [SerializeField] private GameObject fairing1;
    [SerializeField] private GameObject fairing2;
    [SerializeField] private GameObject stage1;
    [SerializeField] private GameObject stage2;
    [SerializeField] private GameObject stage3;
    
    public CollisionDetectionMode collisionMode = CollisionDetectionMode.ContinuousDynamic;
    public RigidbodyInterpolation interpolation = RigidbodyInterpolation.Interpolate;
    
    public void DropStage1()
    {
        if (!stage1) return;
        
        stage1.transform.SetParent(null, true);
        var rb = stage1.GetComponent<Rigidbody>();
        
        // 물리 시뮬
        rb.isKinematic = false;                    
        rb.useGravity = true;
        rb.collisionDetectionMode = collisionMode;
        rb.interpolation = interpolation;
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
