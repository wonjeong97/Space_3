using System.Collections;
using UnityEngine;

public class ParticleUtil : MonoBehaviour
{
    ///<Summary>파티클에 로켓 기준 방향 속도를 적용</Summary>
    public static void ApplyDirectionalVelocity(ParticleSystem ps, Transform dirRef, float speed)
    {
        if (!ps || !dirRef) return;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;

        Vector3 dir = dirRef.up; // 필요하면 -dirRef.forward로 변경
        vel.x = dir.x * speed;
        vel.y = dir.y * speed;
        vel.z = dir.z * speed;
    }

    ///<Summary>파티클이 재생되는 동안 매 프레임 방향을 갱신</Summary>
    public static IEnumerator FollowDirectionalSmokeRoutine(ParticleSystem ps, Transform dirRef, float speed)
    {
        if (!ps || !dirRef) yield break;

        while (ps && ps.isPlaying)
        {
            ApplyDirectionalVelocity(ps, dirRef, speed);
            yield return null;
        }
    }
}
