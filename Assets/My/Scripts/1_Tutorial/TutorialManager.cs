using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class TutorialSetting
{
    public ImageSetting tutorialPage1;
    public ImageSetting tutorialPage2;
    public ImageSetting tutorialPage3;

    public ImageSetting imageInfo;

    public ImageSetting subImage;
}

/// <summary> 튜토리얼 씬 관리 매니저 </summary>
public sealed class TutorialManager : SceneManager_Base<TutorialSetting>
{
    // ===== JSON 경로 =====
    protected override string JsonPath => "JSON/TutorialSetting.json";

    #region Serialized Refs

    [Header("UI")]
    [SerializeField] private List<GameObject> tutorialImageObjs; // 튜토리얼 이미지 목록
    [SerializeField] private GameObject tutorialPage1; // 이용안내 일체형 이미지
    [SerializeField] private GameObject tutorialPage2; // 모니터에 출력되는 내용을 보고 따라해주세요 배경 이미지
    [SerializeField] private GameObject tutorialPage3; // 각 상황에 맞게 버튼을 조정해주세요 배경 이미지
    [SerializeField] private GameObject infoImage;
    [SerializeField] private GameObject subImage;
    
    #endregion

    #region Settings / State

    private float CrossFadeTime => fadeTime; // 이미지 전환 시간 (씬 기본 페이드와 동일)
    private int _step; // 현재 튜토리얼 단계 인덱스

    #endregion

    #region Initialization

    /// <summary>
    /// 초기화 루틴: 이미지 세팅, LED/카메라 시작, 페이드 인, 입력 루프
    /// </summary>
    protected override async UniTask Init()
    {
        try
        {
            _step = 0;
            
            SettingImageObject(tutorialPage1, setting.tutorialPage1);
            SettingImageObject(tutorialPage2, setting.tutorialPage2);
            SettingImageObject(tutorialPage3, setting.tutorialPage3);
            
            SettingImageObject(infoImage, setting.imageInfo);
            SettingImageObject(subImage, setting.subImage);
            
            tutorialPage1.SetActive(true);
            tutorialPage2.SetActive(false);
            tutorialPage3.SetActive(false);

            await UniTask.Yield();

            // 3) LED/카메라 시작
            StartBlinkGreenAsync(500, 160);
            ArduinoInputManager.Instance?.SetLedAll(true);
            TurnCamera3Async(DestroyToken).Forget();

            // 4) 페이드 인
            await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage2, fadeImage3 });

            // 5) 입력 루프: 버튼 입력마다 다음 단계로 넘어감
            while (true)
            {
                CancellationToken cancel = DestroyToken;

                // 입력 대기
                while (!cancel.IsCancellationRequested && this != null && isActiveAndEnabled)
                {
                    if (TransitionInProgress) break; // 씬 전환 중이면 종료

                    bool arduinoPressed = ArduinoInputManager.Instance && ArduinoInputManager.Instance.TryConsumeAnyPress(out _);
                    if (arduinoPressed || TryConsumeSingleInput()) break;
                    await UniTask.Yield();
                }

                if (ShouldAbort()) break;

                // ===== 페이지 전환 로직 =====
                if (_step == 0)
                {
                    tutorialPage1.SetActive(false);
                    tutorialPage2.SetActive(true);
                    _step = 1;
                }
                else if (_step == 1)
                {
                    tutorialPage2.SetActive(false);
                    tutorialPage3.SetActive(true);
                    _step = 2;
                }
                else
                {
                    // 마지막 단계 -> 다음 씬
                    canInput = false;
                    if (ArduinoInputManager.Instance) ArduinoInputManager.Instance.FlushAll();
                    inputReceived = false;

                    if (ShouldAbort()) break;

                    int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 2;
                    await LoadSceneAsync(target, new[] { fadeImage1, fadeImage2, fadeImage3 });
                    break;
                }
                
                inputReceived = false;
                await UniTask.Yield();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[TutorialManager] Init-> 예외 발생: {e}");
        }
    }

    #endregion

    #region Helpers

    /// <summary> 취소/비활성/전환 중 여부 확인 </summary>
    private bool ShouldAbort()
    {
        return DestroyToken.IsCancellationRequested || !this || !isActiveAndEnabled || TransitionInProgress;
    }

    #endregion
}