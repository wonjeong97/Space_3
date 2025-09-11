using UnityEngine;
using UnityEngine.UI;

// 특정위치 화면 터치시 게임 종료
public class GameCloser : MonoBehaviour
{
    [SerializeField] private RectTransform rectTransform;
    private CloseSetting closeSetting;

    private int clickCount = 0;
    private float timer = 0f;
    private bool counting = false;

    private void Start()
    {
        closeSetting = JsonLoader.Instance.settings.closeSetting;

        if (rectTransform != null)
        {
            Vector2 anchor = closeSetting.position;
            rectTransform.anchorMin = anchor;
            rectTransform.anchorMax = anchor;
            rectTransform.pivot = anchor;
            rectTransform.GetComponent<Image>().color = new Color(1, 1, 1, closeSetting.imageAlpha);
        }
    }

    private void Update()
    {
        if (!counting) return;

        timer += Time.deltaTime;

        if (timer >= closeSetting.resetClickTime)
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

        if (clickCount >= closeSetting.numToClose)
        {
            ExitGame();
        }
    }

    private void ResetClickCount()
    {
        clickCount = 0;
        timer = 0f;
        counting = false;
    }

    private void ExitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
