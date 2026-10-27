using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>대기 씬의 요청 알림, 브리핑, 수락과 최종 출동 버튼을 관리한다.</summary>
public sealed class WaitingMissionController : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float offerDelaySeconds = 5f; // 새 요청이 도착하기까지의 실제 시간(초)이다.
    [SerializeField] private Button missionButton;                 // 도착한 요청의 브리핑을 여는 버튼이다.
    [SerializeField] private GameObject notification;              // 새 요청이 있음을 표시하는 알림이다.
    [SerializeField] private GameObject briefingPanel;             // 요청 설명과 수락·거절 UI를 담는다.
    [SerializeField] private TMP_Text briefingText;                // 현재 요청 및 수락 결과를 안내한다.
    [SerializeField] private Button acceptButton;                 // 현재 요청을 수락하는 버튼이다.
    [SerializeField] private Button rejectButton;                 // 현재 요청을 거절하는 버튼이다.
    [SerializeField] private Button dispatchButton;               // 수락한 요청의 출동을 최종 확정한다.
    [SerializeField] private TMP_Text missionStatus;              // 대기와 출동 준비 상태를 표시한다.
    [SerializeField] private RectTransform safeAreaRoot;          // 모바일 안전 영역에 배치할 UI 부모다.

    private Coroutine offerRoutine; // 비활성화 시 중지할 요청 대기 작업이다.
    private int requestNumber;      // 현재 대기 씬에서 받은 요청의 순번이다.
    private WaitingStage stage;     // 수락과 출동을 구분하여 중복 입력을 차단한다.
    private Rect lastSafeArea;      // 화면 안전 영역이 변경될 때만 레이아웃을 갱신한다.
    private Vector2Int lastScreen;  // 화면 크기 변경을 감지하기 위한 직전 해상도다.

    private void Awake()
    {
        MissionDispatchSession.Clear();
    }

    private void OnEnable()
    {
        if (missionButton == null || notification == null || briefingPanel == null || briefingText == null
            || acceptButton == null || rejectButton == null || dispatchButton == null
            || missionStatus == null || safeAreaRoot == null)
        {
            Debug.LogError("Waiting 씬의 미션 UI 참조를 모두 연결해주세요.", this);
            enabled = false;
            return;
        }
        missionButton.onClick.AddListener(OpenBriefing);
        acceptButton.onClick.AddListener(AcceptMission);
        rejectButton.onClick.AddListener(RejectMission);
        dispatchButton.onClick.AddListener(Dispatch);
        ButtonPressEffect.Attach(missionButton);
        ButtonPressEffect.Attach(acceptButton);
        ButtonPressEffect.Attach(rejectButton);
        ButtonPressEffect.Attach(dispatchButton);
        UpdateSafeArea();
        if (GameProgressSession.IsLoaded && GameProgressSession.Current.Stage != ProgressStage.Ready)
            stage = WaitingStage.ProgressBlocked;
        if (stage == WaitingStage.Waiting) BeginWaiting();
        else RefreshView();
    }

    private void Update()
    {
        UpdateSafeArea();
    }

    private void OnDisable()
    {
        if (offerRoutine != null) StopCoroutine(offerRoutine);
        offerRoutine = null;
        if (missionButton != null) missionButton.onClick.RemoveListener(OpenBriefing);
        if (acceptButton != null) acceptButton.onClick.RemoveListener(AcceptMission);
        if (rejectButton != null) rejectButton.onClick.RemoveListener(RejectMission);
        if (dispatchButton != null) dispatchButton.onClick.RemoveListener(Dispatch);
    }

    // 거절 또는 새 씬 진입 시 실제 시간 기준으로 요청을 다시 기다린다.
    private void BeginWaiting()
    {
        if (offerRoutine != null) StopCoroutine(offerRoutine);
        stage = WaitingStage.Waiting;
        RefreshView();
        offerRoutine = StartCoroutine(WaitForOffer());
    }

    // 기존 5초 요청 대기 방식을 대기 씬에서도 유지한다.
    private IEnumerator WaitForOffer()
    {
        yield return new WaitForSecondsRealtime(offerDelaySeconds);
        offerRoutine = null;
        requestNumber++;
        stage = WaitingStage.Available;
        briefingText.text = $"출동 요청 #{requestNumber:000}\n\n긴급 출동 요청이 도착했습니다.\n\n"
            + "환자를 픽업한 뒤 병원으로 후송하세요.\n\n"
            + "수락 후 ‘출동하기’를 누르면 주행을 시작합니다.\n거절하면 5초 뒤 새 요청을 받습니다.";
        RefreshView();
    }

    // 아직 처리하지 않은 요청의 브리핑만 연다.
    public void OpenBriefing()
    {
        if (!isActiveAndEnabled || stage != WaitingStage.Available) return;
        stage = WaitingStage.Briefing;
        RefreshView();
    }

    // 수락은 준비 단계까지만 진행하며 씬 이동이나 맵 생성을 시작하지 않는다.
    public void AcceptMission()
    {
        if (!isActiveAndEnabled || stage != WaitingStage.Briefing) return;
        stage = WaitingStage.Accepted;
        briefingText.text = $"출동 요청 #{requestNumber:000} 수락 완료\n\n"
            + "환자 픽업 및 병원 후송 준비가 완료되었습니다.\n\n‘출동하기’를 누르면 주행을 시작합니다.";
        RefreshView();
    }

    // 거절한 요청은 출동 승인 없이 폐기한다.
    public void RejectMission()
    {
        if (!isActiveAndEnabled || stage != WaitingStage.Briefing) return;
        BeginWaiting();
    }

    // 유효한 수락과 로드 가능한 목적지를 확인한 뒤 한 번만 출동을 확정한다.
    public void Dispatch()
    {
        if (!isActiveAndEnabled || stage != WaitingStage.Accepted) return;
        if (!Application.CanStreamedLevelBeLoaded("GamePlay"))
        {
            Debug.LogError("Build Settings에 GamePlay 씬을 등록해주세요.", this);
            return;
        }
        if (!MissionDispatchSession.TryDispatch(requestNumber)) return;
        stage = WaitingStage.Dispatching;
        RefreshView();
        SceneFadeTransition.LoadScene("GamePlay");
    }

    // 하나의 상태로 알림과 버튼 표시를 결정해 수락 전 출동을 막는다.
    private void RefreshView()
    {
        missionButton.gameObject.SetActive(stage == WaitingStage.Available);
        notification.SetActive(stage == WaitingStage.Available);
        briefingPanel.SetActive(stage == WaitingStage.Briefing || stage == WaitingStage.Accepted
            || stage == WaitingStage.Dispatching);
        acceptButton.gameObject.SetActive(stage == WaitingStage.Briefing);
        rejectButton.gameObject.SetActive(stage == WaitingStage.Briefing);
        dispatchButton.gameObject.SetActive(stage == WaitingStage.Accepted || stage == WaitingStage.Dispatching);
        dispatchButton.interactable = stage == WaitingStage.Accepted;
        missionStatus.text = stage == WaitingStage.Accepted ? "미션 수락 완료 · 출동 준비"
            : stage == WaitingStage.Dispatching ? "출동 중"
            : stage == WaitingStage.ProgressBlocked
                ? (GameProgressSession.Current.Stage == ProgressStage.Fired ? "해고되었습니다." : "실적 평가 대기")
                : "출동 요청 대기 중";
    }

    // 세로 화면 비율과 모바일 안전 영역 변경을 UI 앵커에 반영한다.
    private void UpdateSafeArea()
    {
        if (safeAreaRoot == null || Screen.width <= 0 || Screen.height <= 0) return;
        var screen = new Vector2Int(Screen.width, Screen.height);
        var safeArea = Screen.safeArea;
        if (lastScreen == screen && lastSafeArea == safeArea) return;
        lastScreen = screen;
        lastSafeArea = safeArea;
        safeAreaRoot.anchorMin = new Vector2(safeArea.xMin / Screen.width, safeArea.yMin / Screen.height);
        safeAreaRoot.anchorMax = new Vector2(safeArea.xMax / Screen.width, safeArea.yMax / Screen.height);
        safeAreaRoot.offsetMin = safeAreaRoot.offsetMax = Vector2.zero;
    }

    private enum WaitingStage
    {
        Waiting,
        Available,
        Briefing,
        Accepted,
        Dispatching,
        ProgressBlocked
    }
}
