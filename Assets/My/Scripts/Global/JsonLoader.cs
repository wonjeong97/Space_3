using System;
using System.IO;
using TMPro;
using UnityEngine;

public class JsonLoader : MonoBehaviour
{
    [NonSerialized] public Settings settings;

    private static JsonLoader _instance;

    public static JsonLoader Instance
    {
        get
        {
            if (ReferenceEquals(_instance, null))
            {
                _instance = FindFirstObjectByType<JsonLoader>()
                            ?? new GameObject("JsonLoader").AddComponent<JsonLoader>();
            }

            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
            return;
        }

        settings = LoadJsonData<Settings>("Settings.json");
    }

    private void Start()
    {
        if (settings == null)
        {
            Debug.LogError($"[JsonLoader] Settings is null.]");
        }
    }

    public T LoadJsonData<T>(string fileName)
    {
        string filePath = Path.Combine(Application.streamingAssetsPath, fileName).Replace("\\", "/");

        if (!File.Exists(filePath))
        {
            Debug.LogWarning("[JsonLoader] File is not exits: " + filePath);
            return default;
        }

        string json = File.ReadAllText(filePath);
        //Debug.Log($"[JsonLoader] {fileName} load complete: \n" + json);

        return JsonUtility.FromJson<T>(json);
    }
}

#region Json Settings

[Serializable]
public enum UIImageType
{
    Simple = 0,
    Sliced,
    Tiled,
    Filled
}

[Serializable]
public class CloseSetting
{
    public Vector2 position;
    public int numToClose;
    public float resetClickTime;
    public float imageAlpha;
}

[Serializable]
public class ImageSetting
{
    public string name;
    public Vector2 position;
    public Vector2 size;
    public Vector3 rotation;
    public string sourceImage;
    public Color color = Color.white;
    public UIImageType type = UIImageType.Simple;
}

[Serializable]
public class TextSetting
{
    public string name;
    public Vector2 position;
    public Vector3 rotation;
    public string text;
    public string fontName;
    public float fontSize;
    public Color fontColor = Color.white;
    public TextAlignmentOptions alignment = TextAlignmentOptions.Center;
}

[Serializable]
public class FontMaps
{
    public string font1;
    public string font2;
    public string font3;
    public string font4;
    public string font5;
}

[Serializable]
public class VideoSetting
{
    public string name;
    public Vector2 position;
    public Vector2 size;
    public string fileName;
    public float volume;
}

[Serializable]
public class SoundSetting
{
    public string key;
    public String clipPath;
    public float volume = 1.0f;
}

[Serializable]
public class KeyboardSetting
{
    public string name;
    public Vector2 position;
    public Vector2 size;
}

[Serializable]
public class PageSetting
{
    public string name;
    public ImageSetting pageBackground;
    public Vector2 position;
    public Vector2 size = new Vector2(1920, 1080);
    public TextSetting[] texts;
    public ImageSetting[] images;
    public ButtonSetting[] buttons;
    public VideoSetting[] videos;
    public KeyboardSetting[] keyboards;
}

[Serializable]
public class PrintSetting
{
    public string printerName;
    public string printFont;
    public int printFontSize;
    public string[] printKeys;
}

[Serializable]
public class ButtonSetting
{
    public string name;
    public Vector2 position;
    public Vector2 size;
    public Vector3 rotation;
    public ImageSetting buttonBackgroundImage;
    public VideoSetting buttonBackgroundVideo;
    public ImageSetting buttonAdditionalImage;
    public TextSetting buttonText;
    public string buttonSound;

    public string targetPopupName; // 클릭 시 열릴 팝업 이름
}

[Serializable]
public class PopupSetting
{
    public string name;
    public ImageSetting popupBackgroundImage;
    public TextSetting[] popupTexts;
    public ImageSetting[] popupImages;
    public ButtonSetting popupCloseButton;
}

[Serializable]
public class EffectSetting
{
    public string name;
    public Vector2 position;
    public Vector2 size;
}

[Serializable]
public class GameObjectSetting
{
    public string name;
    public Vector3 position;
    public Vector3 size;
    public Vector3 rotation;
}

[Serializable]
public class Settings
{
    public float inactivityTime; // 입력이 없을 시 타이틀로 되돌아가는 시간
    public float fadeTime;
    public CloseSetting closeSetting;
    public FontMaps fontMap;
    public SoundSetting[] sounds;
}

#endregion