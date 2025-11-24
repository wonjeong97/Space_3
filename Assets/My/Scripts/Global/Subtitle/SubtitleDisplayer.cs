using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class SubtitleDisplayer : MonoBehaviour
{
    public static SubtitleDisplayer Instance { get; private set; }

    [Header("Subtitle Source (optional, TextAsset 기반 사용 시)")]
    public TextAsset Subtitle;

    [Header("UI")]
    public TextMeshProUGUI Text;
    public TextMeshProUGUI Text2;

    [Range(0f, 1f)]
    public float FadeTime = 0.25f;

    private bool _isPaused;
    private bool _isPausedTimeSet;
    private float _pausedTime;

    private Coroutine _subtitleRoutine;
    private SRTParser _currentParser;

    ///<Summary>싱글톤 인스턴스를 설정하고 초기 상태를 만든다</Summary>
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (Text != null)
        {
            Text.gameObject.SetActive(false);
        }

        if (Text2 != null)
        {
            Text2.gameObject.SetActive(false);
        }
    }

    ///<Summary>TextAsset 기반 자막 재생을 시작한다</Summary>
    public void StartSubtitle(TextAsset subtitleAsset)
    {
        if (subtitleAsset == null)
        {
            StopSubtitle();
            return;
        }

        _currentParser = new SRTParser(subtitleAsset);
        StartSubtitleInternal();
    }

    ///<Summary>StreamingAssets 상대 경로 기반 자막 재생을 시작한다</Summary>
    public void StartSubtitleFromStreamingAssets(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            StopSubtitle();
            return;
        }

        SRTParser parser = SRTParser.CreateFromStreamingAssets(relativePath);
        if (parser == null)
        {
            StopSubtitle();
            return;
        }

        _currentParser = parser;
        StartSubtitleInternal();
    }

    ///<Summary>현재 자막 재생을 정지하고 텍스트를 숨긴다</Summary>
    public void StopSubtitle()
    {
        if (_subtitleRoutine != null)
        {
            StopCoroutine(_subtitleRoutine);
            _subtitleRoutine = null;
        }

        _currentParser = null;
        _isPaused = false;
        _isPausedTimeSet = false;

        if (Text != null)
        {
            Text.text = string.Empty;
            Text.gameObject.SetActive(false);
            SetAlpha(Text, 0f);
        }

        if (Text2 != null)
        {
            Text2.text = string.Empty;
            Text2.gameObject.SetActive(false);
            SetAlpha(Text2, 0f);
        }
    }

    ///<Summary>자막 재생을 일시정지하거나 재개한다</Summary>
    public void SetPaused(bool paused)
    {
        _isPaused = paused;
    }

    ///<Summary>내부적으로 자막 코루틴을 시작한다</Summary>
    private void StartSubtitleInternal()
    {
        if (_currentParser == null)
        {
            StopSubtitle();
            return;
        }

        if (_subtitleRoutine != null)
        {
            StopCoroutine(_subtitleRoutine);
        }

        _isPaused = false;
        _isPausedTimeSet = false;
        _subtitleRoutine = StartCoroutine(BeginWithParser(_currentParser));
    }

    ///<Summary>SRTParser를 이용해 자막을 갱신하는 코루틴</Summary>
    private IEnumerator BeginWithParser(SRTParser parser)
    {
        TextMeshProUGUI current = Text;
        TextMeshProUGUI faded = Text2;

        if (current != null)
        {
            current.text = string.Empty;
            current.gameObject.SetActive(true);
            SetAlpha(current, 0f);
        }

        if (faded != null)
        {
            faded.text = string.Empty;
            faded.gameObject.SetActive(true);
            SetAlpha(faded, 0f);
        }

        float startTime = Time.time;
        SubtitleBlock currentSubtitle = null;

        while (true)
        {
            while (_isPaused)
            {
                if (!_isPausedTimeSet)
                {
                    _pausedTime = Time.time;
                    _isPausedTimeSet = true;
                }

                yield return null;
            }

            if (_isPausedTimeSet)
            {
                startTime += Time.time - _pausedTime;
                _isPausedTimeSet = false;
            }

            float elapsed = Time.time - startTime;

            SubtitleBlock block = parser.GetForTime(elapsed);
            if (block != null)
            {
                if (!block.Equals(currentSubtitle))
                {
                    currentSubtitle = block;

                    TextMeshProUGUI temp = current;
                    current = faded;
                    faded = temp;

                    if (current != null)
                    {
                        current.text = currentSubtitle.Text;
                    }

                    if (faded != null)
                    {
                        StartCoroutine(FadeTextOut(faded));
                    }

                    yield return new WaitForSeconds(FadeTime / 3f);

                    if (current != null)
                    {
                        yield return FadeTextIn(current);
                    }
                }

                yield return null;
            }
            else
            {
                if (current != null)
                {
                    StartCoroutine(FadeTextOut(current));
                }

                if (faded != null)
                {
                    yield return FadeTextOut(faded);
                }

                if (current != null)
                {
                    current.gameObject.SetActive(false);
                }

                if (faded != null)
                {
                    faded.gameObject.SetActive(false);
                }

                yield break;
            }
        }
    }

    ///<Summary>에디터에서 FadeTime 값을 정규화한다</Summary>
    private void OnValidate()
    {
        FadeTime = (int)(FadeTime * 10f) / 10f;
    }

    ///<Summary>텍스트를 서서히 사라지게 한다</Summary>
    private IEnumerator FadeTextOut(TextMeshProUGUI text)
    {
        if (text == null)
        {
            yield break;
        }

        Color toColor = text.color;
        toColor.a = 0f;
        yield return Fade(text, toColor, Ease.OutSine);
    }

    ///<Summary>텍스트를 서서히 나타나게 한다</Summary>
    private IEnumerator FadeTextIn(TextMeshProUGUI text)
    {
        if (text == null)
        {
            yield break;
        }

        Color toColor = text.color;
        toColor.a = 1f;
        yield return Fade(text, toColor, Ease.InSine);
    }

    ///<Summary>DOTween으로 텍스트 색상을 보간한다</Summary>
    private IEnumerator Fade(TextMeshProUGUI text, Color toColor, Ease ease)
    {
        yield return DOTween
            .To(() => text.color, c => text.color = c, toColor, FadeTime)
            .SetEase(ease)
            .WaitForCompletion();
    }

    ///<Summary>텍스트 알파 값을 직접 설정한다</Summary>
    private void SetAlpha(TextMeshProUGUI text, float alpha)
    {
        if (text == null)
        {
            return;
        }

        Color c = text.color;
        c.a = alpha;
        text.color = c;
    }
}
