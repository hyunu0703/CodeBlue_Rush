using UnityEngine;
using UnityEngine.UI;

/// <summary>게임 단계와 확정된 별 평가 및 다음 출동 버튼만 표시한다</summary>
public sealed class GameFlowUI : MonoBehaviour
{
    [SerializeField] private GameFlow flow;
    [SerializeField] private GameObject panel;
    [SerializeField] private GameObject controls;
    [SerializeField] private Text title;
    [SerializeField] private Text stars;
    [SerializeField] private Text buttonLabel;
    [SerializeField] private Text status;
    [SerializeField] private Button continueButton;

    private void OnEnable()
    {
        if (flow) flow.Changed += Refresh;
        if (continueButton) continueButton.onClick.AddListener(Continue);
        Refresh();
    }

    private void OnDisable()
    {
        if (flow) flow.Changed -= Refresh;
        if (continueButton) continueButton.onClick.RemoveListener(Continue);
    }

    private void Continue()
    {
        if (flow) flow.Continue();
    }

    // UI에서는 평가하거나 미션을 자동 진행하지 않는다
    private void Refresh()
    {
        if (!flow) return;
        bool result = flow.State == GameFlow.Phase.Result;
        bool failed = flow.State == GameFlow.Phase.GameOver;
        panel.SetActive(result || failed || flow.Error != null);
        controls.SetActive(flow.State == GameFlow.Phase.DrivingToPatient || flow.State == GameFlow.Phase.Pickup || flow.State == GameFlow.Phase.Transporting);
        title.text = flow.Error ?? (result ? "PATIENT DELIVERED" : "GAME OVER");
        stars.text = result ? new string('★', flow.Stars) + new string('☆', 3 - flow.Stars) : string.Empty;
        buttonLabel.text = "NEXT MISSION";
        continueButton.interactable = result || failed;
        status.text = flow.State == GameFlow.Phase.Pickup ? "STOP - PATIENT BOARDING" : flow.State == GameFlow.Phase.Transporting ? "DRIVE TO HOSPITAL" : flow.State == GameFlow.Phase.DrivingToPatient ? "DRIVE TO PATIENT" : string.Empty;
    }
}
