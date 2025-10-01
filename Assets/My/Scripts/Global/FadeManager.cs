using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class FadeManager : MonoBehaviour
{
    public static FadeManager Instance { get; private set; }

    [SerializeField] private Image mainFadeImage;
    [SerializeField] private Image subFadeImage;

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

        if (!mainFadeImage || !subFadeImage)
        {
            Debug.LogError("[FadeManager] Fade Image is not assigned.");
            return;
        }

        SetAlpha(1f);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // -----------------------------
    // 페이드 전체 (메인 + 서브)
    // -----------------------------
    public UniTask FadeInAsync(float duration, bool unscaledTime = false, CancellationToken token = default)
        => RunFadeAsync(1f, 0f, duration, unscaledTime, token);

    public UniTask FadeOutAsync(float duration, bool unscaledTime = false, CancellationToken token = default)
        => RunFadeAsync(0f, 1f, duration, unscaledTime, token);

    private async UniTask RunFadeAsync(float from, float to, float duration, bool unscaled, CancellationToken token)
    {
        if (!mainFadeImage || !subFadeImage) return;

        mainFadeImage.raycastTarget = true;
        mainFadeImage.transform.SetAsLastSibling();
        subFadeImage.raycastTarget = true;
        subFadeImage.transform.SetAsLastSibling();

        float elapsed = 0f;
        while (elapsed < duration)
        {
            token.ThrowIfCancellationRequested();

            float alpha = Mathf.Lerp(from, to, elapsed / duration);
            SetAlpha(alpha);

            elapsed += unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        SetAlpha(to);

        if (to <= 0.001f)
        {
            mainFadeImage.raycastTarget = false;
            mainFadeImage.transform.SetAsFirstSibling();

            subFadeImage.raycastTarget = false;
            subFadeImage.transform.SetAsLastSibling();
        }
    }

    // -----------------------------
    // 페이드 개별 (단일 이미지)
    // -----------------------------
    public UniTask FadeInMainAsync(float duration, bool unscaledTime = false, CancellationToken token = default)
        => RunFadeAsync(mainFadeImage, 1f, 0f, duration, unscaledTime, token);

    public UniTask FadeOutMainAsync(float duration, bool unscaledTime = false, CancellationToken token = default)
        => RunFadeAsync(mainFadeImage, 0f, 1f, duration, unscaledTime, token);

    private async UniTask RunFadeAsync(Image target, float from, float to, float duration, bool unscaled, CancellationToken token)
    {
        if (!target)
        {
            Debug.LogWarning("[FadeManager] Target Image is null for single fade.");
            return;
        }

        target.raycastTarget = true;
        target.transform.SetAsLastSibling();

        float elapsed = 0f;
        while (elapsed < duration)
        {
            token.ThrowIfCancellationRequested();

            float alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            SetAlpha(target, alpha);

            elapsed += unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        SetAlpha(target, to);

        if (to <= 0.001f)
        {
            target.raycastTarget = false;

            if (target == mainFadeImage)
                target.transform.SetAsFirstSibling();
            else
                target.transform.SetAsLastSibling();
        }
    }

    // -----------------------------
    // 유틸 메서드
    // -----------------------------
    private void SetAlpha(float alpha)
    {
        if (!mainFadeImage || !subFadeImage) return;

        Color c1 = mainFadeImage.color;
        mainFadeImage.color = new Color(c1.r, c1.g, c1.b, alpha);

        Color c2 = subFadeImage.color;
        subFadeImage.color = new Color(c2.r, c2.g, c2.b, alpha);
    }

    private void SetAlpha(Image target, float alpha)
    {
        if (!target) return;

        Color c = target.color;
        target.color = new Color(c.r, c.g, c.b, alpha);
    }
}
