using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>시작 화면의 버튼 입력과 게임 진입 및 종료를 처리한다.</summary>
public sealed class StartSceneMenu : MonoBehaviour
{
    [SerializeField] private Button startButton; // 게임 씬으로 진입하는 버튼이다.
    [SerializeField] private Button exitButton;  // 실행을 종료하는 버튼이다.

    [SerializeField] private GameObject errorPanel;        // 읽기 실패 시 진입을 보류하는 안내다.
    [SerializeField] private TMP_Text errorText;           // 오류와 재시도 방법을 표시한다.
    [SerializeField] private Button retryButton;          // 초기화 없이 같은 저장을 다시 읽는다.
    [SerializeField] private RectTransform errorSafeArea; // 오류 안내를 안전 영역에 배치한다.

    private bool isStarting;       // 페이드 중 중복 읽기와 씬 전환을 차단한다.
    private Rect lastSafeArea;     // 안전 영역 변경 시에만 앵커를 갱신한다.
    private Vector2Int lastScreen; // 화면 회전과 해상도 변경을 감지한다.

    private void OnEnable()
    {
        if (startButton == null || exitButton == null || errorPanel == null
            || errorText == null || retryButton == null || errorSafeArea == null)
        {
            Debug.LogError("Start 씬의 시작·종료·저장 오류 UI 참조를 모두 연결해주세요.", this);
            enabled = false;
            return;
        }

        ButtonPressEffect.Attach(startButton);
        ButtonPressEffect.Attach(exitButton);
        startButton.onClick.AddListener(StartGame);
        exitButton.onClick.AddListener(ExitGame);
        retryButton.onClick.AddListener(StartGame);
        ButtonPressEffect.Attach(retryButton);
        isStarting = false;
        startButton.interactable = true;
        errorPanel.SetActive(false);
        UpdateSafeArea();
    }

    private void Update()
    {
        UpdateSafeArea();
    }

    private void OnDisable()
    {
        if (retryButton != null) retryButton.onClick.RemoveListener(StartGame);
        if (startButton != null)
        {
            startButton.onClick.RemoveListener(StartGame);
        }
        if (exitButton != null)
        {
            exitButton.onClick.RemoveListener(ExitGame);
        }
    }

    // 저장을 정상 복원한 경우에만 페이드를 시작하고 실패하면 같은 데이터를 다시 읽는다.
    private void StartGame()
    {
        if (ApplicationPause.IsPaused || isStarting) return;
        isStarting = true;
        startButton.interactable = false;
        retryButton.interactable = false;
        if (!Application.CanStreamedLevelBeLoaded("Waiting"))
        {
            ShowError("대기 화면을 열지 못했습니다.\n다시 시도해주세요.");
            Debug.LogError("Build Settings에 Waiting 씬을 등록해주세요.", this);
            return;
        }
        if (!GameProgressSession.TryLoad(out var error))
        {
            ShowError("진행 데이터를 읽지 못했습니다.\n다시 시도해주세요.");
            Debug.LogWarning($"진행 데이터 읽기 실패: {error}", this);
            return;
        }
        errorPanel.SetActive(false);
        SceneFadeTransition.LoadScene("Waiting");
    }

    // 실패 후 재시도 버튼을 통해서만 시작 동작을 다시 허용한다.
    private void ShowError(string message)
    {
        isStarting = false;
        errorText.text = message;
        errorPanel.SetActive(true);
        retryButton.interactable = true;
    }

    // 기기 안전 영역이 달라질 때 오류 안내의 앵커를 갱신한다.
    private void UpdateSafeArea()
    {
        if (errorSafeArea == null || Screen.width <= 0 || Screen.height <= 0) return;
        var screen = new Vector2Int(Screen.width, Screen.height);
        var safeArea = Screen.safeArea;
        if (screen == lastScreen && safeArea == lastSafeArea) return;
        lastScreen = screen;
        lastSafeArea = safeArea;
        errorSafeArea.anchorMin = new Vector2(safeArea.xMin / Screen.width, safeArea.yMin / Screen.height);
        errorSafeArea.anchorMax = new Vector2(safeArea.xMax / Screen.width, safeArea.yMax / Screen.height);
        errorSafeArea.offsetMin = errorSafeArea.offsetMax = Vector2.zero;
    }

    // 실행 환경에 맞춰 게임을 종료한다.
    private void ExitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
