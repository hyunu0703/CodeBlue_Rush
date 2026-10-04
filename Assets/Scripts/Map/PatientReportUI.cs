using UnityEngine;
using UnityEngine.UI;

/// <summary>상황보고의 메시지를 이벤트로만 표시한다</summary>
public sealed class PatientReportUI : MonoBehaviour
{
    [SerializeField] private PatientReport report;
    [SerializeField] private Text label;
    [SerializeField] private HospitalTransfer transfer;
    [SerializeField] private GameFlow flow;
    private PatientReport subscribed;

    // 런타임에서 보고 데이터와 표시 대상을 명시적으로 연결한다
    public void Bind(PatientReport source)
    {
        Unbind();
        report = source;
        if (isActiveAndEnabled)
            OnEnable();
    }

    // 이벤트를 한 번 구독하고 현재 메시지를 반영한다
    private void OnEnable()
    {
        Unbind();
        subscribed = report;
        if (subscribed)
            subscribed.ReportChanged += Refresh;
        if (transfer)
            transfer.HospitalArrived += Refresh;
        if (flow)
            flow.Changed += Refresh;
        Refresh();
    }

    // 비활성화 시 실제 구독 대상에서 해제한다
    private void OnDisable()
    {
        Unbind();
    }

    // 참조 변경과 비활성화에서 동일 구독을 정리한다
    private void Unbind()
    {
        if (subscribed)
            subscribed.ReportChanged -= Refresh;
        if (transfer)
            transfer.HospitalArrived -= Refresh;
        if (flow)
            flow.Changed -= Refresh;
        subscribed = null;
    }

    // 미션 상태를 변경하지 않고 표시만 갱신한다
    private void Refresh()
    {
        if (!label)
            return;
        if (flow && (flow.Error != null || flow.State == GameFlow.Phase.GameOver))
            label.text = flow.Error ?? "미션 실패\n새 출동을 시작하세요";
        else if (transfer && transfer.Status == HospitalTransfer.TransferStatus.Arrived)
            label.text = "환자 이송 완료";
        else
            label.text = report && report.isActiveAndEnabled ? report.IsPatientOnBoard ? "환자 탑승 완료\n병원으로 이동 후 정차하세요" : report.Message : string.Empty;
    }
}
