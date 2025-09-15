using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
public class RMSetting
{
    public ImageSetting main1;
    public ImageSetting main2;
    public ImageSetting main3;
    public ImageSetting sub1;

    public ImageSetting[] rockets;
    public ImageSetting[] satellites;
}

public class RMManager : SceneManager_Base<RMSetting>
{
    [Header("UI")]
    [SerializeField] private GameObject mainImage1;
    [SerializeField] private GameObject mainImage2;
    [SerializeField] private GameObject mainImage3;
    [SerializeField] private GameObject videoPlayerObject;
    [SerializeField] private GameObject subImage1;

    protected override string JsonPath => "JSON/RMSetting.json";

    private enum Phase
    {
        SelectRocket,
        SelectSatellite,
        End
    }

    private Phase _phase = Phase.SelectRocket;
    private int _selectedRocket = -1;
    private int _selectedSatellite = -1;
    private bool _transitioning;

    private void Update()
    {
        if (!canInput || _transitioning) return;
        
        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            if (_phase == Phase.SelectRocket)
            {
                if (_selectedRocket > 0) _selectedRocket--;
                SetSelectedImage(_selectedRocket);    
            }
            else if (_phase == Phase.SelectSatellite)
            {
                if (_selectedSatellite > 0) _selectedSatellite--;
                SetSelectedImage(_selectedSatellite);
            }
        }
        else if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            if (_phase == Phase.SelectRocket)
            {
                if (_selectedRocket < setting.rockets.Length - 1) _selectedRocket++;
                SetSelectedImage(_selectedRocket);    
            }
            else if (_phase == Phase.SelectSatellite)
            {
                if (_selectedSatellite < setting.satellites.Length - 1) _selectedSatellite++;
                SetSelectedImage(_selectedSatellite);
            }
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            EnterConfirm();
        }
    }

    protected override async Task Init()
    {
        SettingImageObject(mainImage1, setting.main1);
        SettingImageObject(mainImage2, setting.main2);
        SettingImageObject(mainImage3, setting.main3);
        SettingImageObject(subImage1, setting.sub1);

        StartCoroutine(FadeImage(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage2, fadeImage3 }));
    }

    private void SetSelectedImage(int index)
    {
        if (index < 0) return;
        if (_phase == Phase.SelectRocket && index >= setting.rockets.Length) return;
        if (_phase == Phase.SelectSatellite && index >= setting.satellites.Length) return;

        if (_phase == Phase.SelectRocket)
        {
            SettingImageObject(mainImage1, setting.rockets[index]);
        }
        else if (_phase == Phase.SelectSatellite)
        {
            SettingImageObject(mainImage1, setting.satellites[index]);
        }
    }

    private void EnterConfirm()
    {
        if (_phase == Phase.SelectRocket)
        {
            _phase =  Phase.SelectSatellite;
            SettingImageObject(mainImage1, setting.satellites[0]);
        }
        else if (_phase == Phase.SelectSatellite)
        {
            _phase = Phase.End;
            StartCoroutine(ActivateVideo());
        }
    }

    private IEnumerator ActivateVideo()
    {
        
    }
}