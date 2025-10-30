using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

[Serializable]
public class TitleSetting
{
    public ImageSetting background;
    public ImageSetting titleImage;
    public ImageSetting infoImage;
}

/// <summary> 타이틀 씬 관리 클래스 </summary>
public class TitleManager : SceneManager_Base<TitleSetting>
{
    protected override string JsonPath => "JSON/TitleSetting.json";
    
    [Header("UI")]
    [SerializeField] private GameObject backgroundImage;
    [SerializeField] private GameObject titleImage; // 우주발사체 타이틀 이미지
    [SerializeField] private GameObject infoImage;  // 시작하려면 아무 버튼이나 누르세요 이미지
    
    /// <summary> 씬 초기화 메서드 </summary>
    protected override async UniTask Init()
    {
        inputReceived = false;

        SettingImageObject(backgroundImage, setting.background);
        SettingImageObject(titleImage,     setting.titleImage);
        SettingImageObject(infoImage,      setting.infoImage);
        await UniTask.Yield();

        CancellationToken ct = DestroyToken;

        bool ready = await WaitArduinoReadyAsync(3000, ct);
        if (!ready)
        {
            Debug.LogWarning("[TitleManager] 아두이노 준비 타임 아웃");
        }
        else
        {
            Debug.Log("[TitleManager] 아두이노 열림");
            ArduinoInputManager.Instance?.SetLedAll(true);
            StartBlinkGreenAsync(500, 160);
        }

        TurnCamera3Async(ct).Forget();
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage3 });

        if (ct.IsCancellationRequested || !this || !isActiveAndEnabled) return;

        // 입력 대기
        while (!ct.IsCancellationRequested && isActiveAndEnabled)
        {
            ArduinoInputManager aim = ArduinoInputManager.Instance;
            if (aim != null && aim.TryConsumeAnyPress(out _)) break;
            if (TryConsumeSingleInput()) break;
            await UniTask.Yield();
        }

        int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 1;
        await LoadSceneAsync(target, new[] { fadeImage1, fadeImage3 });
    }

    // 헬퍼: 아두이노 포트가 열릴 때까지 대기
    private async UniTask<bool> WaitArduinoReadyAsync(int timeoutMs, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            ArduinoInputManager inst = ArduinoInputManager.Instance;
            if (inst != null && inst.ArduinoReady) return true;
            await UniTask.Delay(50, cancellationToken: ct);
        }
        return false;
    }
}