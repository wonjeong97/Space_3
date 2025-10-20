using UnityEngine;
using UnityEngine.UI;

// 특정위치 화면 터치시 게임 종료
public class GameCloser : MonoBehaviour
{
    [SerializeField] private RectTransform rectTransform;
    private CloseSetting _closeSetting;

    private int _clickCount = 0;
    private float _timer = 0f;
    private bool _counting = false;

    private void Start()
    {
        _closeSetting = JsonLoader.Instance.settings.closeSetting;

        if (rectTransform != null)
        {
            Vector2 anchor = _closeSetting.position;
            rectTransform.anchorMin = anchor;
            rectTransform.anchorMax = anchor;
            rectTransform.pivot = anchor;
            rectTransform.GetComponent<Image>().color = new Color(1, 1, 1, _closeSetting.imageAlpha);
        }
    }

    private void Update()
    {
        if (!_counting) return;

        _timer += Time.deltaTime;

        if (_timer >= _closeSetting.resetClickTime)
        {
            ResetClickCount();
        }
    }

    /// <summary>
    /// 클릭 시 호출되어 클릭 횟수를 증가시킵니다.
    /// </summary>
    public void Click()
    {
        _counting = true;
        _clickCount++;

        if (_clickCount >= _closeSetting.numToClose)
        {
            ExitGame();
        }
    }

    private void ResetClickCount()
    {
        _clickCount = 0;
        _timer = 0f;
        _counting = false;
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
