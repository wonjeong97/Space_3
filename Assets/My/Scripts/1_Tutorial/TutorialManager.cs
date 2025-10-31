using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class TutorialSetting
{
    public ImageSetting background;

    public ImageSetting infoImage1;
    public ImageSetting infoImage2;
    public ImageSetting infoImage3;

    public TextSetting infoText;
    public ImageSetting[] tutorialImages;
}

/// <summary> 튜토리얼 씬 관리 매니저 </summary>
public sealed class TutorialManager : SceneManager_Base<TutorialSetting>
{
    // ===== JSON 경로 =====
    protected override string JsonPath => "JSON/TutorialSetting.json";

    #region Serialized Refs

    [Header("UI")]
    [SerializeField] private GameObject backgroundImage; // 배경 팝업 이미지
    [SerializeField] private GameObject infoImage1; // "조작 안내"
    [SerializeField] private GameObject infoImage2; // "모니터에 출력되는 내용을 보고 따라해주세요!"
    [SerializeField] private GameObject infoImage3; // "컨트롤러의 아무 버튼을 누르면 다음 화면으로 진행됩니다."
    [SerializeField] private List<GameObject> tutorialImageObjs; // 튜토리얼 이미지 목록

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

            // 1) 튜토리얼 이미지 배치 및 초기 가시성 설정
            int count = (tutorialImageObjs != null && setting.tutorialImages != null) ? Mathf.Min(tutorialImageObjs.Count, setting.tutorialImages.Length) : 0;

            for (int i = 0; i < count; i++)
            {
                try
                {
                    if (tutorialImageObjs != null && setting.tutorialImages != null)
                    {
                        SettingImageObject(tutorialImageObjs[i], setting.tutorialImages[i]);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[TutorialManager] Init-> 튜토리얼 이미지 세팅 중 예외(i={i}): {e.Message}");
                }
            }

            if (tutorialImageObjs != null)
            {
                for (int i = 0; i < tutorialImageObjs.Count; i++)
                {
                    SetActiveWithAlpha(tutorialImageObjs[i], i == 0, i == 0 ? 1f : 0f);
                }
            }

            // 2) 고정 UI 이미지 배치
            try
            {
                SettingImageObject(backgroundImage, setting.background);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TutorialManager] Init-> 배경 이미지 세팅 중 예외: {e.Message}");
            }

            try
            {
                SettingImageObject(infoImage1, setting.infoImage1);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TutorialManager] Init-> infoImage1 세팅 중 예외: {e.Message}");
            }

            try
            {
                SettingImageObject(infoImage2, setting.infoImage2);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TutorialManager] Init-> infoImage2 세팅 중 예외: {e.Message}");
            }

            try
            {
                SettingImageObject(infoImage3, setting.infoImage3);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TutorialManager] Init-> infoImage3 세팅 중 예외: {e.Message}");
            }

            await UniTask.Yield();

            // 3) LED/카메라 시작
            StartBlinkGreenAsync(500, 160);
            TurnCamera3Async(DestroyToken).Forget();

            // 4) 페이드 인
            await FadeImageAsync(1f, 0f, fadeTime, new[] { fadeImage1, fadeImage3 });

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

                // 다음 이미지로 전환
                if (_step < count - 1)
                {
                    if (ShouldAbort()) break;
                    try
                    {
                        if (tutorialImageObjs != null)
                        {
                            await AdvanceStepAsync(tutorialImageObjs[_step], tutorialImageObjs[_step + 1], CrossFadeTime);
                        }

                        _step++;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[TutorialManager] Init-> 단계 전환 중 예외: {e}");
                        break;
                    }
                }
                else
                {
                    // 마지막 단계 → 다음 씬으로 전환
                    canInput = false;
                    if (ArduinoInputManager.Instance) ArduinoInputManager.Instance.FlushAll();
                    inputReceived = false;

                    if (ShouldAbort()) break;

                    int target = (nextSceneBuildIndex >= 0) ? nextSceneBuildIndex : 2;
                    Debug.Log($"[TutorialManager] Init-> 다음 씬으로 전환 시도 (buildIndex={target})");
                    await LoadSceneAsync(target, new[] { fadeImage1, fadeImage3 });
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[TutorialManager] Init-> 예외 발생: {e}");
        }
    }

    #endregion

    #region Helpers

    /// <summary> 게임 오브젝트 활성화 및 Image 알파 적용 </summary>
    private void SetActiveWithAlpha(GameObject go, bool active, float alpha)
    {
        if (!go)
        {
            Debug.LogWarning("[TutorialManager] SetActiveWithAlpha-> 대상 오브젝트가 null입니다");
            return;
        }

        go.SetActive(active);

        if (go.TryGetComponent(out Image img))
        {
            Color c = img.color;
            c.a = alpha;
            img.color = c;
        }
        else
        {
            Debug.LogWarning("[TutorialManager] SetActiveWithAlpha-> Image 컴포넌트를 찾을 수 없습니다");
        }
    }

    /// <summary> 취소/비활성/전환 중 여부 확인 </summary>
    private bool ShouldAbort()
    {
        return DestroyToken.IsCancellationRequested || !this || !isActiveAndEnabled || TransitionInProgress;
    }

    #endregion
}