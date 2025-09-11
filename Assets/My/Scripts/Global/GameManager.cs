using System;
using System.Threading.Tasks;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [SerializeField] private Reporter reporter;

    // 미입력 시간 이후 타이틀로 되돌아가는 가게 하는 프로퍼티
    private float inactivityTimer;
    private float inactivityThreshold = 30f;
    private Vector3 LastMousePosition;
    public event Action onReset;

    public GameObject TitlePage { get; set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }

        for (int i = 0; i < Display.displays.Length; i++)
        {
            if (i == 0) continue;
            Display.displays[i].Activate(); // Display 2, 3 activate
        }
    }

    private void Start()
    {
        Cursor.visible = false;
        LastMousePosition = Input.mousePosition;

        if (JsonLoader.Instance.settings != null)
        {
            inactivityThreshold = JsonLoader.Instance.settings.inactivityTime;
        }
    }

    private void Update()
    {
        // D키를 눌러 디버그 패널 활성화 / 비활성화
        if (Input.GetKeyDown(KeyCode.D))
        {
            reporter.showGameManagerControl = !reporter.showGameManagerControl;

            if (reporter.show)
            {
                reporter.show = false;
            }
        }
        else if (Input.GetKeyDown(KeyCode.M))
        {
            Cursor.visible = !Cursor.visible;
        }
        
        if (TitlePage && !TitlePage.activeInHierarchy)
        {
            inactivityTimer += Time.deltaTime;
            if (inactivityTimer >= inactivityThreshold)
            {
                inactivityTimer = 0f;
                // 타이틀로 자동 이동
            }
        }

        if (Input.anyKeyDown || Input.touchCount > 0 || Input.GetMouseButton(0) || Input.mousePosition != LastMousePosition)
        {
            inactivityTimer = 0f;
            LastMousePosition = Input.mousePosition;
        }
    }
    

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void Reset()
    {
        onReset?.Invoke();
    }
}