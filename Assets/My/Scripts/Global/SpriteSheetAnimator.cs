using UnityEngine;

public class SpriteSheetAnimator : MonoBehaviour
{
    [Header("Sprite Sheet Settings")]
    [SerializeField] private Material material;
    [SerializeField] private int tilesX = 10;
    [SerializeField] private int tilesY = 10;
    [SerializeField] private float framesPerSecond = 30f;

    [Header("Optional")]
    [Tooltip("마지막 몇 프레임을 건너뛸지 (예: 10이면 91~100 프레임 미사용)")]
    [SerializeField] private int skipLastFrames = 10;

    [Header("Playback")]
    [Tooltip("씬 시작 시 자동 재생할지 여부")]
    [SerializeField] private bool playOnAwake = true;
    [Tooltip("끝까지 재생 후 처음으로 돌아가 반복할지 여부")]
    [SerializeField] private bool loop = true;
    [Tooltip("Time.timeScale의 영향을 받지 않게 할지 여부")]
    [SerializeField] private bool useUnscaledTime = false;

    private int totalFrames;
    private int usableFrames;
    private float frameDuration;
    private float clipDuration;

    private Renderer rend;
    private bool isPlaying;
    private float localTime; // 이 애니메이션 전용 시간

    private void Awake()
    {
        rend = GetComponent<Renderer>();

        totalFrames = tilesX * tilesY;
        usableFrames = Mathf.Max(1, totalFrames - skipLastFrames);
        frameDuration = 1f / Mathf.Max(1f, framesPerSecond);
        clipDuration = usableFrames * frameDuration;

        // Renderer에 머티리얼이 지정되어 있으면 덮어쓰기
        if (rend != null && material != null)
        {
            rend.material = material;
        }

        isPlaying = playOnAwake;
        localTime = 0f;
    }

    private void Update()
    {
        if (!isPlaying) return;
        if (rend == null || rend.material == null) return;

        // fps를 인스펙터에서 바꿔도 반영되도록 매 프레임 업데이트
        frameDuration = 1f / Mathf.Max(1f, framesPerSecond);
        clipDuration = usableFrames * frameDuration;

        float delta = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        localTime += delta;

        // 루프 / 원샷 처리
        if (localTime >= clipDuration)
        {
            if (loop)
            {
                // 반복 재생
                localTime %= clipDuration;
            }
            else
            {
                // 한 번만 재생하고 마지막 프레임에서 멈춤
                localTime = clipDuration - 0.0001f;
                isPlaying = false;
            }
        }

        // 현재 프레임 계산
        int currentFrame = Mathf.FloorToInt(localTime / frameDuration);
        currentFrame = Mathf.Clamp(currentFrame, 0, usableFrames - 1);

        int column = currentFrame % tilesX;
        int row = currentFrame / tilesX;

        Vector2 scale = new Vector2(1f / tilesX, 1f / tilesY);
        Vector2 offset = new Vector2(column * scale.x, 1f - scale.y - row * scale.y);

        rend.material.SetTextureScale("_MainTex", scale);
        rend.material.SetTextureOffset("_MainTex", offset);
    }

    /// <summary> 애니메이션 재생 시작(옵션: 처음부터) </summary>
    public void Play(bool restart = false)
    {
        if (restart)
        {
            localTime = 0f;
        }

        isPlaying = true;
    }

    /// <summary> 일시 정지 </summary>
    public void Pause()
    {
        isPlaying = false;
    }

    /// <summary> 정지 후 처음 프레임으로 이동 </summary>
    public void Stop()
    {
        isPlaying = false;
        localTime = 0f;
        ApplyFrame(0);
    }

    /// <summary> 특정 프레임 인덱스를 직접 적용하고 싶을 때 사용 (0 기반, usableFrames 미만) </summary>
    public void ApplyFrame(int frameIndex)
    {
        if (rend == null || rend.material == null) return;

        frameIndex = Mathf.Clamp(frameIndex, 0, usableFrames - 1);

        int column = frameIndex % tilesX;
        int row = frameIndex / tilesX;

        Vector2 scale = new Vector2(1f / tilesX, 1f / tilesY);
        Vector2 offset = new Vector2(column * scale.x, 1f - scale.y - row * scale.y);

        rend.material.SetTextureScale("_MainTex", scale);
        rend.material.SetTextureOffset("_MainTex", offset);
    }
}
