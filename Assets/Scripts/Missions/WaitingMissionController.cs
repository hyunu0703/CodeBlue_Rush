using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>누적 안내·평가·해고 저장을 처리하고 일반 진행에서만 기존 미션 요청과 출동을 허용한다.</summary>
public sealed class WaitingMissionController : MonoBehaviour
{
    [SerializeField] private Button homeButton; // 진행 기록을 유지한 채 처음 시작 화면으로 돌아가는 입력이다.
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

    [SerializeField] private TMP_Text progressText;       // 현재 일차와 누적 실적을 안내한다.
    [SerializeField] private TMP_Text targetText;         // 다음 누적 목표와 부족 실적을 별도 카드에 안내한다.
    [SerializeField] private GameObject evaluationPanel; // 평가 확인과 해고 중 요청 UI를 덮는 패널이다.
    [SerializeField] private TMP_Text evaluationText;     // 현재 평가의 목표와 결과를 표시한다.
    [SerializeField] private TMP_Text saveStatus;         // 저장 중 상태와 실패 안내를 표시한다.
    [SerializeField] private Button evaluationButton;    // 통과 계속하기 또는 해고 후 다시 시작 입력이다.
    [SerializeField] private TMP_Text evaluationLabel;   // 현재 진행 상태에 맞는 버튼 문구다.
    [SerializeField] private Button retryButton;         // 실패했던 저장만 재시도하며 다음 입력과 분리한다.
    private Button resetButton;                         // 안전 영역 왼쪽 위에서 진행 기록을 1일차로 초기화한다.
    private GameObject resetDialog;                     // 기존 화면을 유지한 채 초기화 여부만 확인하는 입력 차단 패널이다.
    private Button resetConfirmButton;                  // 초기화 확인 후에만 새 진행 저장을 시작한다.
    private Button resetCancelButton;                   // 저장이나 요청 상태를 변경하지 않고 확인창을 닫는다.
    private GameProgress observedProgress;               // 이 화면이 표시하고 있는 저장 완료 원본이다.
    private GameProgress pendingProgress;                // 재계산 없이 같은 내용으로 재시도할 저장 후보다.
    private ProgressAction progressAction;               // 후보 저장 성공 뒤 실행할 화면 전환이다.
    private bool isSaving;                               // 저장 중 재진입과 중복 입력을 차단한다.
    private bool hasSaveError;                           // 실패한 저장을 재시도 버튼에서만 허용한다.

    private bool IsResetDialogOpen => resetDialog != null && resetDialog.activeSelf;
    private bool CanReceiveMission => !IsResetDialogOpen && !ApplicationPause.IsPaused && isActiveAndEnabled && GameProgressSession.IsLoaded && !isSaving && pendingProgress == null
        && ReferenceEquals(observedProgress, GameProgressSession.Current)
        && observedProgress.Stage == ProgressStage.Ready;

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
        if (homeButton == null || missionButton == null || notification == null || briefingPanel == null || briefingText == null
            || acceptButton == null || rejectButton == null || dispatchButton == null
            || missionStatus == null || safeAreaRoot == null || progressText == null || targetText == null || evaluationPanel == null
            || evaluationText == null || saveStatus == null || evaluationButton == null || evaluationLabel == null
            || retryButton == null)
        {
            Debug.LogError("Waiting 씬의 미션 UI 참조를 모두 연결해주세요.", this);
            enabled = false;
            return;
        }
        EnsureResetButton();
        EnsureResetDialog();
        homeButton.onClick.AddListener(ReturnToStart);
        ButtonPressEffect.Attach(homeButton);
        resetButton.onClick.AddListener(OpenResetDialog);
        resetConfirmButton.onClick.AddListener(ConfirmReset);
        resetCancelButton.onClick.AddListener(CancelReset);
        missionButton.onClick.AddListener(OpenBriefing);
        acceptButton.onClick.AddListener(AcceptMission);
        rejectButton.onClick.AddListener(RejectMission);
        dispatchButton.onClick.AddListener(Dispatch);
        evaluationButton.onClick.AddListener(ConfirmProgress);
        retryButton.onClick.AddListener(RetryProgressSave);
        ApplicationPause.Changed += HandlePauseChanged;
        ButtonPressEffect.Attach(missionButton);
        ButtonPressEffect.Attach(acceptButton);
        ButtonPressEffect.Attach(rejectButton);
        ButtonPressEffect.Attach(dispatchButton);
        ButtonPressEffect.Attach(evaluationButton);
        ButtonPressEffect.Attach(retryButton);
        UpdateSafeArea();
        if (!PrepareProgress()) return;
        if (stage == WaitingStage.Waiting && CanReceiveMission) BeginWaiting();
        else RefreshView();
    }

    private void Update()
    {
        UpdateSafeArea();
    }

    private void OnDisable()
    {
        if (homeButton != null) homeButton.onClick.RemoveListener(ReturnToStart);
        if (offerRoutine != null) StopCoroutine(offerRoutine);
        offerRoutine = null;
        if (missionButton != null) missionButton.onClick.RemoveListener(OpenBriefing);
        if (acceptButton != null) acceptButton.onClick.RemoveListener(AcceptMission);
        if (rejectButton != null) rejectButton.onClick.RemoveListener(RejectMission);
        if (dispatchButton != null) dispatchButton.onClick.RemoveListener(Dispatch);
        if (evaluationButton != null) evaluationButton.onClick.RemoveListener(ConfirmProgress);
        if (retryButton != null) retryButton.onClick.RemoveListener(RetryProgressSave);
        if (resetButton != null) resetButton.onClick.RemoveListener(OpenResetDialog);
        if (resetConfirmButton != null) resetConfirmButton.onClick.RemoveListener(ConfirmReset);
        if (resetCancelButton != null) resetCancelButton.onClick.RemoveListener(CancelReset);
        if (resetDialog != null) resetDialog.SetActive(false);
        ApplicationPause.Changed -= HandlePauseChanged;
    }

    // 저장 중 이탈과 중복 전환을 막고 수락 상태를 비운 뒤 시작 화면으로 돌아간다.
    private void ReturnToStart()
    {
        if (isSaving || ApplicationPause.IsPaused || SceneFadeTransition.IsTransitioning) return;
        if (offerRoutine != null) StopCoroutine(offerRoutine);
        offerRoutine = null;
        MissionDispatchSession.Clear();
        SceneFadeTransition.LoadScene("Start");
    }

    // 기존 안전 영역과 글꼴을 사용해 제목 위 왼쪽에 초기화 입력을 한 번만 준비한다.
    private void EnsureResetButton()
    {
        if (resetButton != null) return;
        var root = new GameObject("ResetProgress_Button", typeof(RectTransform), typeof(Image), typeof(Button));
        root.layer = safeAreaRoot.gameObject.layer;
        root.transform.SetParent(safeAreaRoot, false);
        var rect = (RectTransform)root.transform;
        rect.anchorMin = new Vector2(0.04f, 0.92f);
        rect.anchorMax = new Vector2(0.31f, 0.98f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var background = root.GetComponent<Image>();
        background.color = new Color(0.56f, 0.24f, 0.18f, 1f);
        resetButton = root.GetComponent<Button>();
        resetButton.targetGraphic = background;
        resetButton.interactable = false;
        var labelRoot = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelRoot.layer = root.layer;
        labelRoot.transform.SetParent(root.transform, false);
        var label = labelRoot.GetComponent<TextMeshProUGUI>();
        label.font = progressText.font;
        label.fontSharedMaterial = progressText.fontSharedMaterial;
        label.color = Color.white;
        label.text = "1일차 초기화";
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = 10f;
        label.fontSizeMax = 18f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(6f, 2f);
        label.rectTransform.offsetMax = new Vector2(-6f, -2f);
        ButtonPressEffect.Attach(resetButton);
    }

    // 본문과 두 선택지를 분리하고 뒤쪽 입력을 차단하는 확인창을 한 번만 준비한다.
    private void EnsureResetDialog()
    {
        if (resetDialog != null) return;
        resetDialog = new GameObject("Reset Confirmation", typeof(RectTransform), typeof(Image));
        resetDialog.layer = safeAreaRoot.gameObject.layer;
        resetDialog.transform.SetParent(safeAreaRoot, false);
        var rect = (RectTransform)resetDialog.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        resetDialog.GetComponent<Image>().color = new Color(0.04f, 0.06f, 0.09f, 1f);
        var textRoot = new GameObject("Question", typeof(RectTransform), typeof(TextMeshProUGUI));
        textRoot.layer = resetDialog.layer;
        textRoot.transform.SetParent(resetDialog.transform, false);
        var text = textRoot.GetComponent<TextMeshProUGUI>();
        text.font = progressText.font;
        text.fontSharedMaterial = progressText.fontSharedMaterial;
        text.text = "정말 1일차부터 다시 시작하시겠습니까?";
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true;
        text.fontSizeMin = 12f;
        text.fontSizeMax = 26f;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        text.rectTransform.anchorMin = new Vector2(0.1f, 0.5f);
        text.rectTransform.anchorMax = new Vector2(0.9f, 0.72f);
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        resetConfirmButton = CreateResetChoice("확인", 0.1f, 0.47f);
        resetCancelButton = CreateResetChoice("취소", 0.53f, 0.9f);
        resetDialog.SetActive(false);
    }

    // 초기화 버튼의 글꼴과 누름 효과를 유지하면서 확인창 안에 선택 버튼을 배치한다.
    private Button CreateResetChoice(string label, float left, float right)
    {
        var root = new GameObject("Reset " + label, typeof(RectTransform), typeof(Image), typeof(Button));
        root.layer = resetDialog.layer;
        root.transform.SetParent(resetDialog.transform, false);
        root.GetComponent<Image>().color = resetButton.GetComponent<Image>().color;
        var button = root.GetComponent<Button>();
        button.targetGraphic = root.GetComponent<Image>();
        var text = Instantiate(resetButton.GetComponentInChildren<TMP_Text>(), root.transform, false);
        text.text = label;
        var rect = (RectTransform)button.transform;
        rect.anchorMin = new Vector2(left, 0.32f);
        rect.anchorMax = new Vector2(right, 0.43f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        ButtonPressEffect.Attach(button);
        return button;
    }

    // 확인 전에는 저장 후보와 수락 상태를 그대로 보존하고 확인창만 표시한다.
    private void OpenResetDialog()
    {
        if (ApplicationPause.IsPaused || !isActiveAndEnabled || isSaving || stage == WaitingStage.Dispatching
            || !GameProgressSession.IsLoaded || !ReferenceEquals(observedProgress, GameProgressSession.Current)) return;
        resetDialog.SetActive(true);
        RefreshView();
    }

    // 취소하면 열기 전 화면과 요청 대기 시간을 이어서 사용한다.
    private void CancelReset()
    {
        if (ApplicationPause.IsPaused || !isActiveAndEnabled || !IsResetDialogOpen) return;
        resetDialog.SetActive(false);
        RefreshView();
    }

    // 확인창의 명시적 동의 후에만 기존 요청을 폐기하고 초기 상태 저장을 시작한다.
    private void ConfirmReset()
    {
        if (!IsResetDialogOpen || ApplicationPause.IsPaused || !isActiveAndEnabled || isSaving
            || stage == WaitingStage.Dispatching || !GameProgressSession.IsLoaded
            || !ReferenceEquals(observedProgress, GameProgressSession.Current)) return;
        resetDialog.SetActive(false);
        if (offerRoutine != null) StopCoroutine(offerRoutine);
        offerRoutine = null;
        pendingProgress = new GameProgress();
        progressAction = ProgressAction.Restart;
        hasSaveError = false;
        stage = WaitingStage.ProgressBlocked;
        evaluationText.text = "진행 초기화\n\n일차와 누적 실적을 초기화하고 1일차부터 다시 시작합니다.";
        evaluationLabel.text = "초기화";
        SaveProgress();
    }

    // 기존 타이머는 유지하며, 이탈 중 씬 진입이나 저장 완료로 보류된 작업만 복귀 때 시작한다.
    private void HandlePauseChanged(bool paused)
    {
        if (paused || !isActiveAndEnabled || IsResetDialogOpen) return;
        if (stage == WaitingStage.Waiting && offerRoutine == null && CanReceiveMission) BeginWaiting();
        else if (progressAction == ProgressAction.RecordFiring && !hasSaveError) SaveProgress();
    }

    // 첫 진입과 저장 상태 변경 시에만 후보를 만들며 재활성화 시 실패 후보를 유지한다.
    private bool PrepareProgress()
    {
        if (!GameProgressSession.IsLoaded && !GameProgressSession.TryLoad(out var error))
        {
            stage = WaitingStage.ProgressBlocked;
            progressText.text = "진행 데이터를 읽지 못했습니다.";
            evaluationPanel.SetActive(false);
            missionButton.gameObject.SetActive(false);
            notification.SetActive(false);
            briefingPanel.SetActive(false);
            Debug.LogWarning($"진행 데이터 읽기 실패: {error}", this);
            SceneFadeTransition.LoadScene("Start");
            return false;
        }
        var current = GameProgressSession.Current;
        if (ReferenceEquals(observedProgress, current)) return true;
        observedProgress = current;
        pendingProgress = null;
        progressAction = ProgressAction.None;
        hasSaveError = false;
        saveStatus.text = string.Empty;
        if (current.Stage == ProgressStage.Ready)
        {
            stage = WaitingStage.Waiting;
            return true;
        }
        stage = WaitingStage.ProgressBlocked;
        var summary = $"{current.CurrentDay}일차 · {current.EvaluationPeriod}차 실적 평가\n\n"
            + $"누적 실적: {current.CumulativeScore}점\n평가 누적 목표: {current.CumulativeTarget}점\n"
            + $"부족 실적: {current.RequiredScore}점\n\n";
        if (current.Stage == ProgressStage.Fired)
        {
            evaluationText.text = summary + "해고되었습니다.\n다시 시작하면 1일차부터 진행합니다.";
            evaluationLabel.text = "다시 시작";
            current.TryRestart(out pendingProgress);
            progressAction = ProgressAction.Restart;
        }
        else
        {
            current.TryCompleteEvaluation(current.EvaluationPeriod, out pendingProgress);
            if (current.CanPassEvaluation)
            {
                evaluationText.text = summary + "실적 평가 통과\n계속하기를 누르면 다음 목표로 진행합니다.";
                evaluationLabel.text = "계속하기";
                progressAction = ProgressAction.Continue;
            }
            else
            {
                evaluationText.text = summary + "실적 평가 미달\n해고 기록을 저장해야 다시 시작할 수 있습니다.";
                evaluationLabel.text = "다시 시작";
                progressAction = ProgressAction.RecordFiring;
                SaveProgress();
            }
        }
        return true;
    }

    // 통과 확인 또는 저장된 해고의 재시작만 처리한다. 실패 재시도와 입력을 분리한다.
    private void ConfirmProgress()
    {
        if (IsResetDialogOpen || hasSaveError || progressAction == ProgressAction.RecordFiring) return;
        SaveProgress();
    }

    // 해고 저장 성공 뒤 같은 재시도 입력이 곧바로 재시작 입력으로 바뀌지 않게 한다.
    private void RetryProgressSave()
    {
        if (IsResetDialogOpen || !hasSaveError) return;
        SaveProgress();
    }

    // 저장 성공 후에만 원본을 교체하고 요청 대기 또는 저장된 해고 화면으로 전환한다.
    private void SaveProgress()
    {
        if (ApplicationPause.IsPaused || !isActiveAndEnabled || stage != WaitingStage.ProgressBlocked
            || isSaving || pendingProgress == null) return;
        if (!GameProgressSession.IsLoaded || !ReferenceEquals(GameProgressSession.Current, observedProgress))
        {
            ShowSaveError("진행 상태를 확인하지 못했습니다.\n게임을 다시 실행해주세요.");
            return;
        }
        isSaving = true;
        saveStatus.text = "저장 중…";
        RefreshView();
        if (!GameProgressSession.TrySave(pendingProgress, out var error))
        {
            isSaving = false;
            ShowSaveError("진행 데이터를 저장하지 못했습니다.\n다시 시도해주세요.");
            Debug.LogWarning($"평가 진행 저장 실패: {error}", this);
            return;
        }
        var completedAction = progressAction;
        isSaving = false;
        hasSaveError = false;
        saveStatus.text = string.Empty;
        pendingProgress = null;
        progressAction = ProgressAction.None;
        if (completedAction == ProgressAction.RecordFiring)
        {
            // 해고 저장 성공 후에만 초기화 후보와 다시 시작 입력을 준비한다.
            PrepareProgress();
            RefreshView();
            return;
        }
        observedProgress = GameProgressSession.Current;
        requestNumber = 0;
        stage = WaitingStage.Waiting;
        MissionDispatchSession.Clear();
        BeginWaiting();
    }

    // 기존 파일·원본·후보를 유지하고 동일 저장 작업만 다시 허용한다.
    private void ShowSaveError(string message)
    {
        hasSaveError = true;
        saveStatus.text = message;
        RefreshView();
    }

    // 거절 또는 새 씬 진입 시 실제 시간 기준으로 요청을 다시 기다린다.
    private void BeginWaiting()
    {
        if (!CanReceiveMission) return;
        if (offerRoutine != null) StopCoroutine(offerRoutine);
        stage = WaitingStage.Waiting;
        RefreshView();
        offerRoutine = StartCoroutine(WaitForOffer());
    }

    // 기존 실제 시간 5초 기준을 유지하되 백그라운드·복귀 첫 프레임은 남은 시간에서 제외한다.
    private IEnumerator WaitForOffer()
    {
        var elapsed = 0f;
        while (elapsed < offerDelaySeconds || IsResetDialogOpen)
        {
            yield return null;
            if (!IsResetDialogOpen) elapsed += ApplicationPause.ActiveDeltaTime;
        }
        offerRoutine = null;
        if (!CanReceiveMission || stage != WaitingStage.Waiting) yield break;
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
        if (!isActiveAndEnabled || !CanReceiveMission || stage != WaitingStage.Available) return;
        stage = WaitingStage.Briefing;
        RefreshView();
    }

    // 수락은 준비 단계까지만 진행하며 씬 이동이나 맵 생성을 시작하지 않는다.
    public void AcceptMission()
    {
        if (!isActiveAndEnabled || !CanReceiveMission || stage != WaitingStage.Briefing) return;
        stage = WaitingStage.Accepted;
        briefingText.text = $"출동 요청 #{requestNumber:000} 수락 완료\n\n"
            + "환자 픽업 및 병원 후송 준비가 완료되었습니다.\n\n‘출동하기’를 누르면 주행을 시작합니다.";
        RefreshView();
    }

    // 거절한 요청은 출동 승인 없이 폐기한다.
    public void RejectMission()
    {
        if (!isActiveAndEnabled || !CanReceiveMission || stage != WaitingStage.Briefing) return;
        BeginWaiting();
    }

    // 유효한 수락과 로드 가능한 목적지를 확인한 뒤 한 번만 출동을 확정한다.
    public void Dispatch()
    {
        if (!isActiveAndEnabled || !CanReceiveMission || stage != WaitingStage.Accepted) return;
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
        if (resetButton != null)
            resetButton.interactable = !IsResetDialogOpen && !isSaving && stage != WaitingStage.Dispatching && GameProgressSession.IsLoaded
                && ReferenceEquals(observedProgress, GameProgressSession.Current);
        if (observedProgress != null)
        {
            progressText.text = $"{observedProgress.CurrentDay}일차\n누적 실적: {observedProgress.CumulativeScore}점";
            targetText.text = $"다음 목표: {observedProgress.CumulativeTarget}점\n부족 실적: {observedProgress.RequiredScore}점";
        }
        evaluationPanel.SetActive(stage == WaitingStage.ProgressBlocked);
        evaluationButton.gameObject.SetActive(!hasSaveError);
        retryButton.gameObject.SetActive(hasSaveError);
        evaluationButton.interactable = !isSaving && progressAction != ProgressAction.None
            && progressAction != ProgressAction.RecordFiring;
        retryButton.interactable = !isSaving;
        missionButton.gameObject.SetActive(stage == WaitingStage.Available);
        notification.SetActive(stage == WaitingStage.Available);
        briefingPanel.SetActive(stage == WaitingStage.Briefing || stage == WaitingStage.Accepted
            || stage == WaitingStage.Dispatching);
        // 실적 안내가 브리핑 본문 위에 그려지지 않게 배경 안내를 함께 숨긴다.
        progressText.gameObject.SetActive(!briefingPanel.activeSelf && !evaluationPanel.activeSelf && !IsResetDialogOpen);
        targetText.gameObject.SetActive(progressText.gameObject.activeSelf);
        missionStatus.gameObject.SetActive(!briefingPanel.activeSelf && !evaluationPanel.activeSelf && !IsResetDialogOpen);
        if (briefingPanel.activeSelf) briefingPanel.transform.SetAsLastSibling();
        if (evaluationPanel.activeSelf) evaluationPanel.transform.SetAsLastSibling();
        resetButton.transform.SetAsLastSibling();
        if (IsResetDialogOpen) resetDialog.transform.SetAsLastSibling();
        acceptButton.gameObject.SetActive(stage == WaitingStage.Briefing);
        rejectButton.gameObject.SetActive(stage == WaitingStage.Briefing);
        dispatchButton.gameObject.SetActive(stage == WaitingStage.Accepted || stage == WaitingStage.Dispatching);
        dispatchButton.interactable = stage == WaitingStage.Accepted;
        missionStatus.text = stage == WaitingStage.Accepted ? "미션 수락 완료 · 출동 준비"
            : stage == WaitingStage.Dispatching ? "출동 중"
            : stage == WaitingStage.ProgressBlocked
                ? (observedProgress.Stage == ProgressStage.Fired ? "해고되었습니다." : "실적 평가 대기")
                : "출동 요청 대기 중";
    }

    private enum ProgressAction
    {
        None,
        Continue,
        RecordFiring,
        Restart
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
