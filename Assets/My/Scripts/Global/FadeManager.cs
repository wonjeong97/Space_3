using System;
using System.Collections;
using System.Threading.Tasks;
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

    // 비동기 래퍼
    public Task FadeInAsync(float duration, bool unscaledTime = false)
        => RunFadeAsync(1f, 0f, duration, unscaledTime);

    public Task FadeOutAsync(float duration, bool unscaledTime = false)
        => RunFadeAsync(0f, 1f, duration, unscaledTime);

    private async Task RunFadeAsync(float from, float to, float duration, bool unscaled)
    {
        var tcs = new TaskCompletionSource<bool>();
        mainFadeImage.raycastTarget = true;
        mainFadeImage.transform.SetAsLastSibling();
        
        subFadeImage.raycastTarget = true;
        subFadeImage.transform.SetAsLastSibling();
        
        StartCoroutine(Fade(from, to, duration, unscaled, () => tcs.TrySetResult(true))); // Fade 완료 후 tcs에 True 설정
        await tcs.Task; // True를 호출 받기 전까지 대기
        if (to <= 0.001f)
        {
            mainFadeImage.raycastTarget = false;
            mainFadeImage.transform.SetAsFirstSibling();
            
            subFadeImage.raycastTarget = false;
            subFadeImage.transform.SetAsLastSibling();
        }
    }

    private IEnumerator Fade(float from, float to, float duration, bool unscaled, Action onComplete)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float alpha = Mathf.Lerp(from, to, elapsed / duration);
            SetAlpha(alpha);
            elapsed += unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
            yield return null;
        }

        SetAlpha(to);
        onComplete?.Invoke();
    }

    private void SetAlpha(float alpha)
    {
        if (!mainFadeImage || !subFadeImage) return;
        var c1 = mainFadeImage.color;
        mainFadeImage.color = new Color(c1.r, c1.g, c1.b, alpha);
        
        var c2 = subFadeImage.color;
        subFadeImage.color = new Color(c2.r, c2.g, c2.b, alpha);
    }
    
    public Task FadeInMainAsync(float duration, bool unscaledTime = false)
        => RunFadeAsync(mainFadeImage, 1f, 0f, duration, unscaledTime);
    
    public Task FadeOutMainAsync(float duration, bool unscaledTime = false)
        => RunFadeAsync(mainFadeImage, 0f, 1f, duration, unscaledTime);
    
    private async Task RunFadeAsync(Image target, float from, float to, float duration, bool unscaled)
    {
        if (!target)
        {
            Debug.LogWarning("[FadeManager] Target Image is null for single fade.");
            return;
        }

        var tcs = new TaskCompletionSource<bool>();
        
        target.raycastTarget = true;
        target.transform.SetAsLastSibling();

        StartCoroutine(FadeSingle(target, from, to, duration, unscaled, () => tcs.TrySetResult(true)));
        await tcs.Task;

        if (to <= 0.001f)
        {
            target.raycastTarget = false;
            
            if (target == mainFadeImage)
                target.transform.SetAsFirstSibling();
            else
                target.transform.SetAsLastSibling();
        }
    }

    private IEnumerator FadeSingle(Image target, float from, float to, float duration, bool unscaled, Action onComplete)
    {
        if (!target)
        {
            onComplete?.Invoke();
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            float alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            SetAlpha(target, alpha);
            elapsed += unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
            yield return null;
        }

        SetAlpha(target, to);
        onComplete?.Invoke();
    }

    private void SetAlpha(Image target, float alpha)
    {
        if (!target) return;
        var c = target.color;
        target.color = new Color(c.r, c.g, c.b, alpha);
    }
}