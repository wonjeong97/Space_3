using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;

public class SkyboxBlender : MonoBehaviour
{
    public static SkyboxBlender Instance;

    // 위에서 만든 쉐이더로 생성한 머티리얼을 할당하세요.
    [SerializeField] private Material blendedSkyboxMaterial;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        
        // 시작 시 Blend 값을 0으로 초기화
        if (blendedSkyboxMaterial != null)
        {
            blendedSkyboxMaterial.SetFloat("_Blend", 0f);
            RenderSettings.skybox = blendedSkyboxMaterial; // 렌더 세팅에 적용
        }
    }

    /// <summary>
    /// 새로운 큐브맵으로 크로스 페이드합니다.
    /// </summary>
    /// <param name="newSkybox">새로 보여줄 .exr(Cubemap) 텍스처</param>
    /// <param name="duration">전환 시간</param>
    public async UniTask ChangeSkyboxAsync(Cubemap newSkybox, float duration, CancellationToken token = default)
    {
        if (blendedSkyboxMaterial == null) return;

        // 1. 현재 B에 있는 텍스처(또는 결과)를 A로 옮김 (연속 페이드를 위해)
        // 만약 Blend가 1(B가 보임)이었다면 B를 A로 복사하고 Blend를 0으로 리셋
        // 여기서는 가장 단순하게 현재 화면에 보이는 텍스처를 A로 설정합니다.
        
        // 현재 _Blend가 1에 가까우면 B가 메인, 0에 가까우면 A가 메인
        float currentBlend = blendedSkyboxMaterial.GetFloat("_Blend");
        Texture currentTex = (currentBlend > 0.5f) 
            ? blendedSkyboxMaterial.GetTexture("_TexB") 
            : blendedSkyboxMaterial.GetTexture("_TexA");

        // A 슬롯에 현재 보이는 텍스처 할당
        blendedSkyboxMaterial.SetTexture("_TexA", currentTex);
        
        // B 슬롯에 새로운 텍스처 할당
        blendedSkyboxMaterial.SetTexture("_TexB", newSkybox);

        // Blend를 0으로 초기화 (A가 100% 보이게)
        blendedSkyboxMaterial.SetFloat("_Blend", 0f);

        // 2. 0 -> 1 로 Blend 값 증가 (UniTask)
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (token.IsCancellationRequested) return;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            
            blendedSkyboxMaterial.SetFloat("_Blend", t);
            
            await UniTask.Yield();
        }

        blendedSkyboxMaterial.SetFloat("_Blend", 1f);
    }
}