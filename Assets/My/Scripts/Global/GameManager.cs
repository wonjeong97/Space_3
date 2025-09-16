using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [SerializeField] private Reporter reporter;
    
    public event Action onReset;

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