using UnityEngine;

public class FadeTrigger : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        // 태그가 "Camera3"인 오브젝트가 들어오면 페이드 아웃 후 뒷 배경 없애기
        if (other.CompareTag("Camera3"))
        {
            LaunchManager.Instance?.FadeAndDeleteBg();
        }
    }
}