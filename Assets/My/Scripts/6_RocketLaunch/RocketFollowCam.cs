using UnityEngine;

/// <summary> 카메라에 붙여서, 지정한 게임 오브젝트를 계속 바라보게 만드는 스크립트 </summary>
public class RocketFollowCam : MonoBehaviour
{   
    [Header("Look At")]
    [SerializeField] private Transform target;
    
    [Header("Options")]
    [SerializeField] private Vector3 offset = Vector3.zero; 
    [SerializeField] private float lookUpOffset; // 목표물보다 위쪽을 보게 할 y 오프셋
    [SerializeField] private bool smooth = true;
    [SerializeField] private float smoothSpeed = 5f;
    
    private void LateUpdate()
    {
        if (!target) return;

        // 바라볼 최종 지점 = 타겟 위치 + 오프셋 + 위쪽 오프셋
        Vector3 lookPoint = target.position + offset + new Vector3(0f, lookUpOffset, 0f);

        if (smooth)
        {
            Quaternion desiredRotation = Quaternion.LookRotation(lookPoint - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, smoothSpeed * Time.deltaTime);
        }
        else
        {
            transform.LookAt(lookPoint);
        }
    }
}