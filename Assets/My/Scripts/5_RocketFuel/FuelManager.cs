using System;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
public class FuelSetting
{
    
}

public class FuelManager : SceneManager_Base<RMSetting>
{
    [Header("UI")] 
    [SerializeField] private GameObject mainImage1;
    [SerializeField] private GameObject mainImage2;
    [SerializeField] private GameObject mainImage3;

    protected override string JsonPath => "JSON/FuelSetting.json";

    protected override async Task Init()
    {
        
    }
}
