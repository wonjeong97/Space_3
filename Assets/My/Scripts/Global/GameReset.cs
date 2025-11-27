using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GameReset : MonoBehaviour
{
    [SerializeField] private RectTransform rectTransform;
    private CloseSetting resetSetting;

    private int clickCount = 0;
    private float timer = 0f;
    private bool counting = false;
    
    private ISceneResettable sceneResetter;
    
    private void Awake()
    {
        // 씬 안에 있는 MonoBehaviour 중 ISceneResettable 구현체 하나 찾기
        MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            ISceneResettable candidate = behaviours[i] as ISceneResettable;
            if (candidate != null)
            {
                sceneResetter = candidate;
                break;
            }
        }
    }
    
    private void Start()
    {
        resetSetting = JsonLoader.Instance.settings.resetSetting;

        if (rectTransform != null)
        {
            Vector2 anchor = resetSetting.position;
            rectTransform.anchorMin = anchor;
            rectTransform.anchorMax = anchor;
            rectTransform.pivot = anchor;
            rectTransform.GetComponent<Image>().color = new Color(1, 1, 1, resetSetting.imageAlpha);
        }
    }
    
    private void Update()
    {
        if (!counting) return;

        timer += Time.deltaTime;

        if (timer >= resetSetting.resetClickTime)
        {
            ResetClickCount();
        }
    }

    /// <summary>
    /// 클릭 시 호출되어 클릭 횟수를 증가시킵니다.
    /// </summary>
    public void Click()
    {
        counting = true;
        clickCount++;

        if (clickCount >= resetSetting.numToClose)
        {
            ResetGame();
        }
    }

    private void ResetClickCount()
    {
        clickCount = 0;
        timer = 0f;
        counting = false;
    }

    private void ResetGame()
    {
        // 다시 누를 수 있도록 카운트 초기화
        ResetClickCount();

        if (sceneResetter != null)
        {
            sceneResetter.ResetToTitle();
        }
        else
        {
            Debug.LogError("[GameReset] ResetGame-> ISceneResettable 구현을 찾지 못했습니다.");
        }
    }
}