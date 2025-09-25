using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class UIBlink : MonoBehaviour
{
    [SerializeField] private float _periodSeconds = 2f; // 한 사이클(밝아졌다 어두워짐) 시간
    
    private readonly int _minAlpha255 = 0;
    private readonly int _maxAlpha255 = 255;

    private Image _image;
    private Coroutine _routine;

    private void Awake()
    {
        // 자기 자신에서 우선 검색, 없으면 자식에서 한 번만 검색
        if (!TryGetComponent(out _image))
            _image = GetComponentInChildren<Image>(includeInactive: true);
    }

    private void OnEnable()
    {
        if (_image == null)
        {
            Debug.LogWarning("[Blink] image not found on this GameObject or its children.");
            return;
        }

        _routine = StartCoroutine(BlinkRoutine());
    }

    private void OnDisable()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }
    }

    private IEnumerator BlinkRoutine()
    {
        _periodSeconds = Mathf.Max(0.0001f, _periodSeconds);
        float min01 = Mathf.Clamp01(_minAlpha255 / 255f);
        float max01 = Mathf.Clamp01(_maxAlpha255 / 255f);

        float t = 0f;
        while (true)
        {
            t += Time.deltaTime;
            float ping = Mathf.PingPong(t * (2f / _periodSeconds), 1f); // periodSeconds에 맞춘 0~1~0
            float a = Mathf.Lerp(min01, max01, ping);
            SetAlpha01(a);
            yield return null;
        }
    }

    private void SetAlpha01(float a01)
    {        
        Color c = _image.color;
        _image.color = new Color(c.r, c.g, c.b, a01);        
    }
}
