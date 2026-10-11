using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class StartSceneMenu : MonoBehaviour
{
    [SerializeField] private Button startButton;
    [SerializeField] private Button exitButton;

    private void OnEnable()
    {
        if (startButton == null || exitButton == null)
        {
            Debug.LogError("Inspector에서 Start Button과 Exit Button을 연결해주세요", this);
            enabled = false;
            return;
        }

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

    private void StartGame()
    {
        SceneManager.LoadScene("GamePlay");
    }

    private void ExitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
