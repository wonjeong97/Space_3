using System.Collections;
using UnityEngine;

/// <summary>
/// 위치+회전 포즈로 천천히 이동 (월드 / 로컬 선택 가능)
/// </summary>
public class FocusObject : MonoBehaviour
{
    [System.Serializable]
    public struct Pose
    {
        public Vector3 position;
        public Quaternion rotation;

        public Pose(Vector3 pos, Quaternion rot)
        {
            position = pos;
            rotation = rot;
        }
    }

    [Header("Defaults")]
    public bool useLocal = true;           // true면 로컬 좌표계 기준 이동
    public float defaultDuration = 1f;

    private Coroutine _routine;

    public void FocusTo(Pose pose, float duration)
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }
        _routine = StartCoroutine(FocusRoutine(pose, duration));
    }

    private IEnumerator FocusRoutine(Pose pose, float duration)
    {
        if (duration <= 0f)
        {
            ApplyPose(pose);
            yield break;
        }

        Vector3 startPos = useLocal ? transform.localPosition : transform.position;
        Quaternion startRot = useLocal ? transform.localRotation : transform.rotation;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float clamped = Mathf.Clamp01(t);

            if (useLocal)
            {
                transform.localPosition = Vector3.Lerp(startPos, pose.position, clamped);
                transform.localRotation = Quaternion.Slerp(startRot, pose.rotation, clamped);
            }
            else
            {
                transform.position = Vector3.Lerp(startPos, pose.position, clamped);
                transform.rotation = Quaternion.Slerp(startRot, pose.rotation, clamped);
            }

            yield return null;
        }

        ApplyPose(pose);
        _routine = null;
    }

    private void ApplyPose(Pose pose)
    {
        if (useLocal)
            transform.SetLocalPositionAndRotation(pose.position, pose.rotation);
        else
            transform.SetPositionAndRotation(pose.position, pose.rotation);
    }
}
