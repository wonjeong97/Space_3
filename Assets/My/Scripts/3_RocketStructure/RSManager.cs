using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
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
    [SerializeField] private GameObject image_Explain1;
    [SerializeField] private GameObject image_Explain2;
    [SerializeField] private GameObject image_Explain3;
    [SerializeField] private GameObject image_Explain4;
    [SerializeField] private GameObject image_Explain5;
    [SerializeField] private GameObject image_Explain6;
    [SerializeField] private GameObject image_Explain7;

    protected override string JsonPath => "JSON/RSSetting.json";

    private readonly List<GameObject> _explainImages = new List<GameObject>();
    private int _activateIndex;
    private bool _transitioning;
    private float _transitionTime;

    private void Awake()
    {
        _explainImages.Add(image_Explain1);
        _explainImages.Add(image_Explain2);
        _explainImages.Add(image_Explain3);
        _explainImages.Add(image_Explain4);
        _explainImages.Add(image_Explain5);
        _explainImages.Add(image_Explain6);
        _explainImages.Add(image_Explain7);
    }

    private void Update()
    {
        if (!canInput || _transitioning) return;

        if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.touchCount > 0)
        {
            if (_activateIndex >= _explainImages.Count - 1)
            {   
                StartCoroutine(FadeAndLoadScene(4, new[] { fadeImage1, fadeImage3 }));
            }
            else
            {
                StartCoroutine(AdvanceExplain());
            }
        }
    }

    protected override async Task Init()
    {
        for (int i = 0; i < _explainImages.Count; i++)
        {
            SettingImageObject(_explainImages[i], setting.explainImages[i]);
            _explainImages[i].SetActive(false);
        }
        
        // 첫 번째 이미지만 활성화
        _activateIndex = 0;
        _explainImages[_activateIndex].SetActive(true);

        _transitionTime = setting.transitionTime;
        
        StartCoroutine(TurnCamera3());
        StartCoroutine(FadeImage(1f, 0f, fadeTime, new []{ fadeImage1, fadeImage3 }));
    }

    private IEnumerator AdvanceExplain()
    {
        _transitioning = true;
        
        yield return CrossFade(_explainImages[_activateIndex], _explainImages[_activateIndex + 1], _transitionTime);
        _activateIndex++;
        
        _transitioning = false;
    }
}
