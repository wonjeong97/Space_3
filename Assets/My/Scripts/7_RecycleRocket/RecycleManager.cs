using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class RecycleSetting
{
    public float popupFadeTime;
    public float gameCloseTime;
    public ImageSetting main1;
    public ImageSetting main2;
    public ImageSetting main3;
    public ImageSetting popup1;
    public ImageSetting endBackground;
    public ImageSetting endImage1;
    public ImageSetting endImage2;

    public ImageSetting[] main1Children;
}

/// <summary>
/// 발사체 회수 팝업을 띄운 뒤 체험을 종료 -> 타이틀로 복귀
/// 흐름: 초기세팅 -> 입력 대기 -> LED/가이드 종료 -> 팝업->엔딩 크로스페이드 -> 지정시간 후 씬 전환
/// </summary>
public class RecycleManager : SceneManager_Base<RecycleSetting>
{
    [Header("UI")]
    [SerializeField] private GameObject mainImage1;
    [SerializeField] private GameObject mainImage2;
    [SerializeField] private GameObject mainImage3;
    [SerializeField] private GameObject popupImage1;
    [SerializeField] private GameObject endBackgroundImage;
    [SerializeField] private GameObject endImage1;
    [SerializeField] private GameObject endImage2;

    [Header("mainImage1")]
    [SerializeField] private Image[] main1ChildrenImages;

    protected override string JsonPath => "JSON/RecycleSetting.json";

    private float _popupFadeTime;
    private float _gameCloseTime;

    #region Logging helpers

    private static void Log(string method, string msg)
    {
        Debug.Log($"[RecycleManager] {method}-> {msg}");
    }

    private static void LogWarn(string method, string msg)
    {
        Debug.LogWarning($"[RecycleManager] {method}-> {msg}");
    }

    private static void LogError(string method, string msg)
    {
        Debug.LogError($"[RecycleManager] {method}-> {msg}");
    }

    #endregion

    #region Unity lifecycle

    /// <summary> 파괴/비활성화 시 LED 이펙트 등 외부 리소스 정리 </summary>
    protected override void OnDisable()
    {
        base.OnDisable();
        try
        {
            StopLedEffects();
            ArduinoInputManager.Instance?.SetLedAll(false);
        }
        catch (Exception e)
        {
            LogError(nameof(OnDisable), e.ToString());
        }
    }

    /// <summary> 초기 세팅 및 입력 대기 -> 크로스페이드 -> 종료 타이머 -> 타이틀 복귀 </summary>
    protected override async UniTask Init()
    {
        CancellationToken token = DestroyToken;

        // 시간 파라미터 보정
        _popupFadeTime = Mathf.Max(0f, setting.popupFadeTime);
        _gameCloseTime = Mathf.Max(0f, setting.gameCloseTime);

        // 고정 이미지 세팅
        SettingImageObject(mainImage1, setting.main1);
        SettingImageObject(mainImage2, setting.main2);
        SettingImageObject(mainImage3, setting.main3);
        SettingImageObject(popupImage1, setting.popup1);
        SettingImageObject(endBackgroundImage, setting.endBackground);
        SettingImageObject(endImage1, setting.endImage1);
        SettingImageObject(endImage2, setting.endImage2);

        // mainImage1 자식 이미지들 세팅
        if (setting.main1Children != null && main1ChildrenImages != null)
        {
            int count = Mathf.Min(setting.main1Children.Length, main1ChildrenImages.Length);
            for (int i = 0; i < count; i++)
            {
                if (main1ChildrenImages[i] == null) continue;
                SettingImageObject(main1ChildrenImages[i].gameObject, setting.main1Children[i]);
            }
        }

        // 엔딩 백그라운드는 시작 시 비활성
        if (endBackgroundImage != null) endBackgroundImage.gameObject.SetActive(false);

        // LED 안내 시작
        ArduinoInputManager.Instance?.SetLedAll(true);
        StartBlinkGreenAsync(500, 160);

        // 첫 페이드 인
        await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage3 });

        // 입력 대기 루프 (아두이노 버튼 or 키보드)
        while (!token.IsCancellationRequested)
        {
            bool pressed =
                (ArduinoInputManager.Instance != null && ArduinoInputManager.Instance.TryConsumeAnyPress(out _))
                || TryConsumeSingleInput();

            if (pressed)
            {
                StopLedEffects();
                ArduinoInputManager.Instance?.SetLedAll(false);
                LedStrip.Range(0, 9, 255, 0, 0);
                break;
            }

            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        ArduinoInputManager.Instance?.FlushAll();

        // 팝업(앞) -> 엔딩(뒤) 크로스페이드
        try
        {
            CrossFadeAsync(popupImage1, endBackgroundImage, _popupFadeTime).Forget();
        }
        catch (Exception e)
        {
            LogWarn(nameof(Init), "CrossFadeAsync failed: " + e.Message);
            // 실패해도 종료 타이머는 계속 진행
        }

        // 종료 타이머 후 타이틀로 이동
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(_gameCloseTime), cancellationToken: token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // 무입력 복귀 정책과 충돌 방지
        PauseInactivityTimer();

        // 0번 빌드 인덱스(타이틀)로 전환
        await LoadSceneAsync(0, new[] { fadeImage1, fadeImage3 });
    }

    #endregion
}
