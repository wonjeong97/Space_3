using System;
using System.Collections;
using UnityEngine;

[Serializable]
public class TitleSetting
{
    public float camera3TurnSpeed;
}

public class TitleManager : MonoBehaviour
{
    public static TitleManager Instance {get; private set;}

    protected TitleSetting titleSetting;

    [Header("Camera")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private Camera camera2;
    [SerializeField] private Camera camera3;
    

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }

        if (!mainCamera || !camera2 || !camera3)
        {
            Debug.LogError("[TitleManager] camera is not assigned");
        }
    }

    private void Start()
    {
        InitTitle();
    }

    private void InitTitle()
    {
        titleSetting ??= JsonLoader.Instance.LoadJsonData<TitleSetting>("JSON/TitleSetting.json");
        
        
    }

    private IEnumerator TurnCamera3()
    {
        yield break;
    }
}
