using UnityEngine;
using UnityEngine.UI;

/// <summary>시작 화면의 버튼 입력과 게임 진입 및 종료를 처리한다.</summary>
public sealed class StartSceneMenu : MonoBehaviour
{
    [SerializeField] private Button startButton; // 게임 씬으로 진입하는 버튼이다.
    [SerializeField] private Button exitButton;  // 실행을 종료하는 버튼이다.

    private void OnEnable()
    {
        if (startButton == null || exitButton == null)
        {
            Debug.LogError("Inspector에서 Start Button과 Exit Button을 연결해주세요", this);
            enabled = false;
            return;
        }

        ButtonPressEffect.Attach(startButton);
        ButtonPressEffect.Attach(exitButton);
        startButton.onClick.AddListener(StartGame);
        exitButton.onClick.AddListener(ExitGame);
    }

    private void OnDisable()
    {
        if (startButton != null)
        {
            startButton.onClick.RemoveListener(StartGame);
        }
        if (exitButton != null)
        {
            exitButton.onClick.RemoveListener(ExitGame);
        }
    }

    // 화면이 어두워진 뒤 미션을 받을 대기 씬으로 진입한다.
    private void StartGame()
    {
        SceneFadeTransition.LoadScene("Waiting");
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
