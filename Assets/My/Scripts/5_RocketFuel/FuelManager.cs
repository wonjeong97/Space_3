using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class FuelSetting
{
    public float popupFadeTime;
    public float fuelFillSpeed;

    public ImageSetting main1;
    public ImageSetting main2;
    public ImageSetting main3;
    public ImageSetting fuelPopup;
    public ImageSetting sub1;

    public ImageSetting[] fuelImage;
}

public class FuelManager : SceneManager_Base<FuelSetting>
{
    [Header("UI")] 
    [SerializeField] private GameObject mainImage1;
    [SerializeField] private GameObject mainImage2;
    [SerializeField] private GameObject mainImage3;
    [SerializeField] private GameObject popupImage;
    [SerializeField] private GameObject subImage;
    [SerializeField] private GameObject fuelImage1;
    [SerializeField] private GameObject fuelImage2;
    [SerializeField] private GameObject fuelImage3;

    protected override string JsonPath => "JSON/FuelSetting.json";
    private Coroutine _popupFadeCoroutine;
    private float _popupFadeTime, _fuelFillSpeed;

    private Image _fuel1Image, _fuel2Image, _fuel3Image;

    private enum Phase
    {
        RocketMove,
        FuelInjection1,
        FuelInjection2,
        FuelInjection3
    }

    private Phase _phase = Phase.RocketMove;

    protected override void OnDisable()
    {
        _popupFadeCoroutine = null;
    }

    private void Update()
    {
        if (!canInput) return;

        if (_phase == Phase.FuelInjection1)
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow) && _popupFadeCoroutine == null)
            {
                _popupFadeCoroutine = StartCoroutine(PopupFade(_popupFadeTime));
            }

            // 누르고 있는 동안 지속 증가
            if (Input.GetKey(KeyCode.LeftArrow))
            {
                bool completed = IncreaseFill(_fuel1Image, _fuelFillSpeed * Time.deltaTime);
                if (completed)
                {
                    _phase = Phase.FuelInjection2;
                }
            }
        }
        else if (_phase == Phase.FuelInjection2)
        {
            if (Input.GetKey(KeyCode.DownArrow))
            {
                bool completed = IncreaseFill(_fuel2Image, _fuelFillSpeed * Time.deltaTime);
                if (completed)
                {
                    _phase = Phase.FuelInjection3;
                }
            }
        }
        else if (_phase == Phase.FuelInjection3)
        {
            if (Input.GetKey(KeyCode.RightArrow))
            {
                bool completed = IncreaseFill(_fuel3Image, _fuelFillSpeed * Time.deltaTime);
                if (completed)
                {
                    Debug.Log("fuel fill completed");
                }
            }
        }
    }

    protected override async Task Init()
    {
        _popupFadeTime = setting.popupFadeTime;
        _fuelFillSpeed = setting.fuelFillSpeed;

        SettingImageObject(mainImage1, setting.main1);
        SettingImageObject(mainImage2, setting.main2);
        SettingImageObject(mainImage3, setting.main3);
        SettingImageObject(popupImage, setting.fuelPopup);
        SettingImageObject(subImage, setting.sub1);

        SettingImageObject(fuelImage1, setting.fuelImage[0]);
        SettingImageObject(fuelImage2, setting.fuelImage[1]);
        SettingImageObject(fuelImage3, setting.fuelImage[2]);

        InitFuelImage();
        //popupImage.SetActive(false);

        _phase = Phase.FuelInjection1;
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage2, fadeImage3 });
    }

    private void InitFuelImage()
    {
        if (fuelImage1.TryGetComponent(out _fuel1Image))
        {
            _fuel1Image.type = Image.Type.Filled;
            _fuel1Image.fillMethod = Image.FillMethod.Horizontal;
            _fuel1Image.fillOrigin = 0; // Left: 0, Right: 1
            _fuel1Image.fillAmount = 0f;
        }

        if (fuelImage2.TryGetComponent(out _fuel2Image))
        {
            _fuel2Image.type = Image.Type.Filled;
            _fuel2Image.fillMethod = Image.FillMethod.Horizontal;
            _fuel2Image.fillOrigin = 0;
            _fuel2Image.fillAmount = 0f;
        }

        if (fuelImage3.TryGetComponent(out _fuel3Image))
        {
            _fuel3Image.type = Image.Type.Filled;
            _fuel3Image.fillMethod = Image.FillMethod.Horizontal;
            _fuel3Image.fillOrigin = 0;
            _fuel3Image.fillAmount = 0f;
        }
    }

    private IEnumerator PopupFade(float duration)
    {
        if (!popupImage) yield break;
        float elapsed = 0f;
        float alpha = 0f;
        Image image = popupImage.GetComponent<Image>();

        while (elapsed < duration)
        {
            alpha = Mathf.Lerp(1f, 0f, elapsed / duration);
            SetAlpha(image, alpha);

            elapsed += Time.deltaTime;
            yield return null;
        }

        SetAlpha(image, alpha);
    }

    private bool IncreaseFill(Image img, float delta)
    {
        if (!img) return false;
        float before = img.fillAmount;
        img.fillAmount = Mathf.Clamp01(img.fillAmount + delta);
        return (before < 1f && img.fillAmount >= 1f);
    }
}