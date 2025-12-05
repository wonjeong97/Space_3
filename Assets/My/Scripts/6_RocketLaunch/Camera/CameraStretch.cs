using UnityEngine;

public class CameraStretch : MonoBehaviour
{
    [Header("Aspect Ratio Fix")]
    [Tooltip("1.0 = 정상\n1.0보다 크면: 옆으로 늘어남 (뚱뚱해짐)\n1.0보다 작으면: 옆으로 줄어듦 (홀쭉해짐)")]
    [Range(0.1f, 3.0f)]
    public float horizontalStretch = 1.2f;

    private Camera _cam;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
    }

    // 다른 스크립트(LaunchManager 등)가 FOV를 바꾼 뒤에 적용되도록 LateUpdate 사용
    private void LateUpdate()
    {
        if (!_cam) return;

        // 1. 현재 카메라 설정(FOV, Aspect 등)을 기준으로 매트릭스 재계산
        _cam.ResetProjectionMatrix();

        // 2. 현재 프로젝션 매트릭스 가져오기
        Matrix4x4 p = _cam.projectionMatrix;

        // 3. X축 스케일(m00)을 조절하여 가로 비율 변경
        // m00 값이 커지면 월드 X좌표가 화면상에서 더 넓게 그려집니다.
        p.m00 *= horizontalStretch;

        // 4. 수정된 매트릭스 적용
        _cam.projectionMatrix = p;
    }
}