using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class RSSetting
{
    public float transitionTime;
    public ImageSetting[] explainImages;
}

public class RSManager : SceneManager_Base<RSSetting>
{
    [Header("UI")]
    [SerializeField] private List<GameObject> explainImageObjs;

    protected override string JsonPath => "JSON/RSSetting.json";

    private int _index;
    private float _crossTime;
    
    protected override async Task Init()
    {   
        _crossTime = Mathf.Max(0f, setting.transitionTime);
        
        // 세팅 개수와 오브젝트 개수 동기화
        int count = Mathf.Min(explainImageObjs.Count, setting.explainImages.Length);
        for (int i = 0; i < count; i++)
        {
            SettingImageObject(explainImageObjs[i], setting.explainImages[i]); // 부모 공통 메서드
            if (explainImageObjs[i]) explainImageObjs[i].SetActive(false);
        }
        
        // 첫 번째 이미지만 활성화
        _index = 0;
        if (count > 0 && explainImageObjs[0])
        {
            explainImageObjs[0].SetActive(true);
            if (explainImageObjs[0].TryGetComponent(out Image img0))
            {
                var c = img0.color; c.a = 1f; img0.color = c;
            }
        }
        
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage3 });
        
        StartCoroutine(TurnCamera3());
        while (true)
        {
            // 단발 입력 대기(부모 헬퍼)
            while (!TryConsumeSingleInput())
                await Task.Yield();

            // 이 씬 내에서 연속 입력 허용 위해 플래그 해제
            inputReceived = false;

            if (_index >= count - 1) break; // 마지막이면 루프 종료 → 씬 전환

            // 현재 -> 다음으로 크로스페이드 (async 버전)
            await CrossFadeAsync(explainImageObjs[_index], explainImageObjs[_index + 1], _crossTime);
            _index++;
        }

        // 다음 씬 전환 (인스펙터에서 미지정 시 원래 흐름대로 4번 씬)
        int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 4;
        await LoadSceneAsync(target, new[] { fadeImage1, fadeImage3 });
    }
}
