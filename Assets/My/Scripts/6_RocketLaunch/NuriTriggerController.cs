using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// CountController의 PlusSeconds를 감시해 NuriAnimEvent 이벤트를 자동 호출.
/// T+ 2:05 → DropStage1()
/// T+ 3:56 → SeparateFairing()
/// T+ 4:30 → DropStage2()
/// T+ 12:14 → Stage3Off()
/// </summary>
public class NuriTriggerController : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CountController count;
    [SerializeField] private NuriAnimEvent nuri;
    [SerializeField] private FocusObject focus;

    [Header("Trigger Times (T+ in seconds)")]
    [SerializeField] private float tDropStage1     = 2f * 60f + 5f;   // 2:05
    [SerializeField] private float tSeparateFairing = 3f * 60f + 56f; // 3:56
    [SerializeField] private float tDropStage2     = 4f * 60f + 30f;  // 4:30
    [SerializeField] private float tStage3Off      = 12f * 60f + 14f; // 12:14

    [Header("Polling Interval (sec)")]
    [SerializeField] private float pollInterval = 0.05f;

    private bool _firedDrop1;
    private bool _firedFairing;
    private bool _firedDrop2;
    private bool _firedStage3Off;

    private void Reset()
    {
        count = FindObjectOfType<CountController>();
        nuri = FindObjectOfType<NuriAnimEvent>();
    }

    private void OnEnable()
    {
        RunTriggerLoop().Forget();
    }

    /// <summary>
    /// CountController의 T+ 시간을 감시해 지정 시각을 넘길 때 각 이벤트를 1회 호출한다.
    /// </summary>
    private async UniTaskVoid RunTriggerLoop()
    {
        CancellationToken token = this.GetCancellationTokenOnDestroy();

        if (count == null)
        {
            Debug.LogError("[NuriTriggerController] CountController reference missing");
            return;
        }
        if (nuri == null)
        {
            Debug.LogError("[NuriTriggerController] NuriAnimEvent reference missing");
            return;
        }
        if (focus == null)
        {
            Debug.LogError("[NuriTriggerController] FocusObject reference missing");
            return;
        }

        _firedDrop1 = _firedFairing = _firedDrop2 = _firedStage3Off = false;

        while (!token.IsCancellationRequested)
        {
            if (!count.IsCountingDown)
            {
                float t = count.TPlusSeconds;

                // T+ 2:05 DropStage1
                if (!_firedDrop1 && t >= tDropStage1)
                {
                    _firedDrop1 = true;
                    FocusObject.Pose pose = new FocusObject.Pose(new Vector3(0f, 3100f, 0f), Quaternion.identity);
                    focus.FocusTo(pose, 2f);
                    nuri.DropStage1().Forget();
                    LaunchManager.Instance?.FadeOutSubRocketStage1Async().Forget();
                    Debug.Log("[Trigger] DropStage1()");
                }

                // T+ 3:56 SeparateFairing
                if (!_firedFairing && t >= tSeparateFairing)
                {
                    _firedFairing = true;
                    nuri.SeparateFairing().Forget();
                    LaunchManager.Instance?.FadeOutSubRocketPairingAsync().Forget();
                    Debug.Log("[Trigger] SeparateFairing()");
                }

                // T+ 4:30 DropStage2
                if (!_firedDrop2 && t >= tDropStage2)
                {
                    _firedDrop2 = true;
                    FocusObject.Pose pose = new FocusObject.Pose(new Vector3(0f, 3800f, 0f), Quaternion.identity);
                    focus.FocusTo(pose, 2f);
                    nuri.DropStage2().Forget();
                    LaunchManager.Instance?.FadeOutSubRocketStage2Async().Forget();
                    Debug.Log("[Trigger] DropStage2()");
                }

                // T+ 12:14 Stage3Off
                if (!_firedStage3Off && t >= tStage3Off)
                {
                    _firedStage3Off = true;
                    FocusObject.Pose pose = new FocusObject.Pose(new Vector3(-46f, 4474f, 0f), Quaternion.Euler(-75f, -90f, 90f));
                    focus.FocusTo(pose, 2f);
                    nuri.Stage3Off().Forget();
                    Debug.Log("[Trigger] Stage3Off()");
                }
            }

            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(pollInterval), cancellationToken: token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
