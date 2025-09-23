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

/// <summary> 우주발사체의 구조와 기능 씬 관리 매니저 </summary>
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
        
        // 설정 개수와 오브젝트 개수 동기화
        // 오브젝트 or 세팅 중 더 작은 개수를 사용하여 null 에러 방지
        int count = Mathf.Min(explainImageObjs.Count, setting.explainImages.Length);
        for (int i = 0; i < count; i++)
        {
            // 이미지 세팅 후 숨김
            SettingImageObject(explainImageObjs[i], setting.explainImages[i]);
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
            // 입력 대기
            while (true)
            {
                if (ArduinoInputManager.Instance && ArduinoInputManager.Instance.TryConsumeAnyPress(out _)) break;
                if (TryConsumeSingleInput()) break;
                
                await Task.Yield();
            }
            if (ArduinoInputManager.Instance) ArduinoInputManager.Instance.FlushAll();
            inputReceived = false; // 연속 입력 설정

            if (_index >= count - 1) break; // 마지막이면 루프 종료 → 씬 전환

            // 현재 이미지 -> 다음 이미지로 크로스페이드
            await CrossFadeAsync(explainImageObjs[_index], explainImageObjs[_index + 1], _crossTime);
            _index++;
        }

        // 다음 씬 전환
        int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 4;
        await LoadSceneAsync(target, new[] { fadeImage1, fadeImage3 });
    }
}
