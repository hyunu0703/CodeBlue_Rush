using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>출동·픽업·후송과 인계 결과를 관리하고 하루 완료 저장 성공 후 대기 씬으로 복귀한다.</summary>
[DisallowMultipleComponent]
public sealed class MissionController : MonoBehaviour
{
    // 대기 씬으로 옮긴 기존 제안 UI는 주행 씬에서 숨기되 저장된 참조를 보존한다.
    [SerializeField] private Button missionButton;
    [SerializeField] private GameObject notification;
    [SerializeField] private GameObject briefingPanel;
    [SerializeField] private TMP_Text briefingText;
    [SerializeField] private Button acceptButton;
    [SerializeField] private Button rejectButton;
    [SerializeField] private TMP_Text missionStatus;
    // 브리핑과 상태 표시를 화면 안전 영역 안에 배치할 부모다.
    [SerializeField] private RectTransform safeAreaRoot;
    // 맵과 운전 입력을 같은 미션 상태에 맞춰 제어한다. Rigidbody 설정 자체는 변경하지 않는다.
    [SerializeField] private MissionMapGenerator mapGenerator;
    [SerializeField] private Rigidbody2D ambulance;
    [SerializeField] private AmbulanceDriveInput driveInput;
    // 병원 도착 반경은 차량 중심 기준 월드 유닛이다. 환자 픽업은 차량 중심의 노란 사각형 진입으로 검사한다.
    [SerializeField, Min(0.1f)] private float arrivalRadius = 0.85f;
    // 미지정 참조는 기존 GamePlay 씬에 한정하여 런타임 표시를 생성/연결한다.
    [SerializeField] private RectTransform navigationPanel;
    [SerializeField] private Camera navigationCamera;
    [SerializeField] private PatientRescueCutscene rescueCutscene;
    [SerializeField] private RectTransform patientECGPanel; // 픽업 완료부터 병원 인계까지 표시하는 기존 검은 패널이다.
    [SerializeField] private PatientHealth patientHealth;   // 차량에서 시간과 충돌에 따른 환자 HP를 관리한다.
    [SerializeField] private MissionPerformance missionPerformance; // 출동부터 인계까지의 직접 차량 충돌 기록이다.
    private PatientECGGraphic patientECG;                   // 패널 안에서만 그리는 심전도 이력이다.
    private TrafficSignalGraphic trafficSignal;            // 안내 문구 아래에서 교차로 통과 중 표시하는 세 원형 신호다.

    [SerializeField] private GameObject resultPanel;  // 안전 영역 안에서 후송 결과와 확인 입력을 표시한다.
    [SerializeField] private TMP_Text resultText;     // 인계 시 고정한 기록과 누적 후보를 표시한다.
    [SerializeField] private TMP_Text saveStatus;     // 저장 실패와 재시도 안내를 결과 아래에 표시한다.
    [SerializeField] private Button confirmButton;   // 같은 결과 후보를 저장하고 다음 일차로 진입한다.
    [SerializeField] private TMP_Text confirmLabel;  // 확인 또는 실패 후 다시 시도 문구다.
    private GameProgress missionProgress;            // 출동 시점에 확정한 일차와 누적 실적 원본이다.
    private MissionResult result;                    // 인계 후에는 HP·접촉 변화와 무관하게 유지할 결과다.
    private bool isSaving;                           // 저장 중 재진입과 연속 확인 입력을 차단한다.
    [SerializeField, Min(0.01f)] private float arrivalFadeSeconds = 0.8f; // 병원 도착부터 결과 표시까지 검게 어두워지는 시간(초)이다.
    private CanvasGroup arrivalCover;                // 결과창 아래에서 주행 화면을 가리고 뒤쪽 UI 입력을 막는 검은 덮개다.
    private Coroutine arrivalRoutine;                // 비활성화 시 중단하고 재활성화 시 다시 시작하는 도착 연출이다.

    private readonly System.Random random = new System.Random(); // 출동 후 유효한 맵 시드를 선택하는 난수 소스다.
    private MissionStage stage;                                 // 주행 씬의 준비, 픽업과 후송 진행 상태다.
    // 화면 또는 안전 영역이 바뀐 경우에만 UI 앵커를 갱신하기 위한 직전 값이다.
    private Rect lastSafeArea;
    private int lastScreenWidth;
    private int lastScreenHeight;
    // 픽업 표현, 감지 위치, 활성 기간 코루틴과 차량 잠금은 이 미션 인스턴스가 소유한다.
    private MissionNavigationGraphic navigation;
    private SpriteRenderer ambulanceRenderer;
    private BoxCollider2D ambulanceCollider;
    private Coroutine rescueRoutine;
    private Vector2 previousPosition;
    private RigidbodyConstraints2D originalConstraints;
    private bool isVehicleLocked;
    private bool isPresentationReady;
    // 픽업 도보 위치와 차선 방향은 재활성화 때 같은 대기 장소를 복원하기 위해 보관한다.
    private Vector2 pickupSidewalkPosition;
    private float pickupRotation;

    /// <summary>수락 및 맵 준비가 끝난 미션이 진행 중인지 반환한다.</summary>
    public bool IsMissionActive => stage == MissionStage.Active || stage == MissionStage.Rescuing
        || stage == MissionStage.Transporting;
    /// <summary>현재 출동한 미션의 8자리 시드다. 맵 준비 전에는 0이다.</summary>
    public int CurrentSeed { get; private set; }
    /// <summary>픽업 컷신 완료 뒤에 생성한 후송 시드다. 후송 시작 전에는 0이다.</summary>
    public int TransportSeed { get; private set; }
    /// <summary>구출 이미지가 모두 사라진 뒤에만 true가 된다. 새 출동에서 초기화한다.</summary>
    public bool IsPatientPickedUp { get; private set; }
    /// <summary>맵 배치와 시작 위치 준비 이후 메인 스레드에서 한 번 발행한다.</summary>
    public event Action<int> OnMissionAccepted;

    /// <summary>에디터 설정 메뉴가 필요한 UI와 씬 컴포넌트를 연결한다.</summary>
    public void Configure(Button missionButton, GameObject notification, GameObject briefingPanel, TMP_Text briefingText,
        Button acceptButton, Button rejectButton, TMP_Text missionStatus, MissionMapGenerator mapGenerator,
        Rigidbody2D ambulance, AmbulanceDriveInput driveInput, RectTransform safeAreaRoot)
    {
        this.missionButton = missionButton;
        this.notification = notification;
        this.briefingPanel = briefingPanel;
        this.briefingText = briefingText;
        this.acceptButton = acceptButton;
        this.rejectButton = rejectButton;
        this.missionStatus = missionStatus;
        this.mapGenerator = mapGenerator;
        this.ambulance = ambulance;
        this.driveInput = driveInput;
        this.safeAreaRoot = safeAreaRoot;
    }

    private void OnEnable()
    {
        if (!ValidateReferences())
        {
            return;
        }
        if (!mapGenerator.ValidateConfiguration(out var error))
        {
            FailConfiguration(error);
            return;
        }

        UpdateSafeArea();
        confirmButton.onClick.AddListener(ConfirmResult);
        ButtonPressEffect.Attach(confirmButton);
        if (stage == MissionStage.Arriving || stage == MissionStage.Result || stage == MissionStage.Completed)
        {
            LockVehicle();
            EnsureArrivalCover();
            arrivalCover.blocksRaycasts = true;
            if (stage == MissionStage.Arriving) arrivalRoutine = StartCoroutine(FadeArrival());
            else arrivalCover.alpha = 1f;
        }
        missionPerformance.SetRecording(IsMissionActive);
        if (stage == MissionStage.Waiting)
        {
            RefreshView();
        }
        else
        {
            if (isPresentationReady && IsMissionActive)
            {
                if (!EnsurePresentation())
                {
                    return;
                }
                if (stage == MissionStage.Active)
                {
                    rescueCutscene.ShowPickup(pickupSidewalkPosition, pickupRotation, ambulanceRenderer);
                }
                previousPosition = ambulance.position;
            }
            RefreshView();
        }
    }

    private IEnumerator Start()
    {
        // 입력 컴포넌트의 Start가 끝난 뒤 승인하므로 씬 진입 전 누른 입력도 해제 후 적용된다.
        yield return null;
        while (ApplicationPause.IsPaused) yield return null;
        if (!isActiveAndEnabled || !EnsurePresentation()) yield break;
        // 씬 직접 실행에서도 미확인 저장을 덮어쓰거나 임의의 첫날로 처리하지 않는다.
        if (!GameProgressSession.IsLoaded && !GameProgressSession.TryLoad(out var error))
        {
            Debug.LogWarning($"진행 데이터 읽기 실패: {error}", this);
            SceneFadeTransition.LoadScene("Start");
            yield break;
        }
        if (GameProgressSession.Current.Stage != ProgressStage.Ready)
        {
            SceneFadeTransition.LoadScene("Waiting");
            yield break;
        }
        AcceptMission();
        if (enabled && stage == MissionStage.Waiting) SceneFadeTransition.LoadScene("Waiting");
    }

    private void FixedUpdate()
    {
        if ((stage != MissionStage.Active && stage != MissionStage.Transporting) || Time.timeScale <= 0f)
        {
            return;
        }
        if (!ValidateReferences() || !EnsurePresentation())
        {
            return;
        }
        // 픽업은 차체 전체의 주차를, 병원은 오른쪽 차고 접근 영역과 차체의 접촉을 검사한다.
        var currentPosition = ambulance.position;
        mapGenerator.UpdateRoadStreaming(ambulanceCollider.bounds, ambulance.linearVelocity);
        var hasArrived = stage == MissionStage.Active
            ? rescueCutscene.IsVehicleInsidePickup(ambulance, ambulanceRenderer, ambulanceCollider)
            : mapGenerator.HasReachedHospital(ambulanceCollider, previousPosition, currentPosition, arrivalRadius);
        previousPosition = currentPosition;
        if (!hasArrived)
        {
            return;
        }
        if (stage == MissionStage.Active)
        {
            stage = MissionStage.Rescuing;
            RefreshView();
            LockVehicle();
            rescueRoutine = StartCoroutine(RunRescue());
        }
        else
        {
            CompleteMission();
        }
    }

    private void Update()
    {
        UpdateSafeArea();
    }

    private void OnDisable()
    {
        if (confirmButton != null) confirmButton.onClick.RemoveListener(ConfirmResult);
        if (resultPanel != null) resultPanel.SetActive(false);
        if (missionPerformance != null) missionPerformance.SetRecording(false);
        if (patientHealth != null)
        {
            patientHealth.SetTransporting(false);
            patientHealth.SetTimerRunning(false);
        }
        if (patientECGPanel != null) patientECGPanel.gameObject.SetActive(false);
        StopArrival();
        StopRescue();
        if (stage == MissionStage.Rescuing)
        {
            // 중단된 컷신은 픽업 완료로 판정하지 않는다. 재활성화 시 같은 장소에서 다시 시작한다.
            stage = MissionStage.Active;
        }
        if (navigation != null)
        {
            navigation.SetGuidanceVisible(false);
        }
        if (missionButton != null)
        {
            missionButton.gameObject.SetActive(false);
        }
        if (briefingPanel != null)
        {
            briefingPanel.SetActive(false);
        }
        if (driveInput != null)
        {
            driveInput.SetMissionDrivingAllowed(false);
        }
    }

    private void OnDestroy()
    {
        // 덮개는 UI Canvas 아래에 있으므로 미션 컴포넌트만 제거할 때도 함께 정리한다.
        if (arrivalCover != null) Destroy(arrivalCover.gameObject);
    }

    // 대기 씬에서 확정한 출동만 소비하여 맵을 준비하고, 성공한 이후 운전을 허용한다.
    public void AcceptMission()
    {
        if (ApplicationPause.IsPaused || !isActiveAndEnabled || stage != MissionStage.Waiting || !ValidateReferences() || !EnsurePresentation()
            || !GameProgressSession.IsLoaded || GameProgressSession.Current.Stage != ProgressStage.Ready
            || !MissionDispatchSession.TryConsume(out _))
        {
            return;
        }
        stage = MissionStage.Preparing;
        missionProgress = GameProgressSession.Current;
        RefreshView();
        try
        {
            // 자기 겹침과 후송 출구 막힘을 사전 검사한다. 여기서는 후송 시드를 게임 상태에 저장하지 않는다.
            var seed = 0;
            for (var attempt = 0; attempt < 256; attempt++)
            {
                var candidate = MissionSeedRules.CreateSeed(random);
                if (mapGenerator != null && mapGenerator.TrySelectTransportSeed(candidate, out _, out _))
                {
                    seed = candidate;
                    break;
                }
            }
            if (seed == 0 || mapGenerator == null || !mapGenerator.isActiveAndEnabled
                || ambulance == null || driveInput == null)
            {
                FailConfiguration("유효한 시드 또는 미션 참조를 준비하지 못했습니다. 미션 컴포넌트와 카탈로그를 확인해주세요.");
                return;
            }
            if (!mapGenerator.TryGenerate(seed, out var error))
            {
                FailConfiguration(error);
                return;
            }
            // 차량 이미지 전체가 들어가는 크기로 픽업 지점과 도보 대기 위치를 함께 준비한다.
            var parkingSize = rescueCutscene.GetParkingSize(ambulanceRenderer, ambulanceCollider);
            if (!mapGenerator.TryConfigurePickup(parkingSize, out pickupSidewalkPosition, out pickupRotation, out error))
            {
                FailConfiguration(error);
                return;
            }
            rescueCutscene.ConfigurePickupZone(mapGenerator.DestinationPosition, pickupRotation, parkingSize, ambulanceRenderer);

            // 입력은 아직 차단된 상태다. 속도를 비운 후 Rigidbody 위치로 순간이동하고 보간 잔상을 방지한다.
            var interpolation = ambulance.interpolation;
            ambulance.interpolation = RigidbodyInterpolation2D.None;
            ambulance.linearVelocity = Vector2.zero;
            ambulance.angularVelocity = 0f;
            ambulance.position = mapGenerator.StartPosition;
            ambulance.rotation = mapGenerator.StartRotation;
            ambulance.interpolation = interpolation;
            CurrentSeed = seed;
            TransportSeed = 0;
            IsPatientPickedUp = false;
            patientHealth.ResetPatient();
            patientECG.ResetPatient(patientHealth);
            previousPosition = ambulance.position;
            // 픽업 장소는 노란 테두리 자체로 표시한다. 기존 글자가 사각형과 차량을 가리지 않게 비운다.
            mapGenerator.SetDestinationLabel(string.Empty);
            rescueCutscene.ShowPickup(pickupSidewalkPosition, pickupRotation, ambulanceRenderer);
            stage = MissionStage.Active;
            missionPerformance.BeginMission();
            RefreshView();
            OnMissionAccepted?.Invoke(seed);
        }
        catch (Exception exception)
        {
            // UI 이벤트 경계에서 예상치 못한 실패를 한 번 관찰하고 추가 수락과 조작을 막는다.
            Debug.LogException(exception, this);
            stage = MissionStage.Waiting;
            CurrentSeed = 0;
            enabled = false;
        }
    }

    // 병원 인계 순간 기록과 차량을 고정하고 암전이 끝난 뒤 결과를 표시한다.
    public void CompleteMission()
    {
        if (ApplicationPause.IsPaused || !isActiveAndEnabled || stage != MissionStage.Transporting || !IsPatientPickedUp
            || !ValidateReferences() || !EnsurePresentation())
        {
            return;
        }
        missionPerformance.SetRecording(false);
        patientHealth.SetTransporting(false);
        patientHealth.SetTimerRunning(false);
        result = new MissionResult(missionProgress, patientHealth.HealthPercent,
            missionPerformance.LowCollisionCount, missionPerformance.HighCollisionCount, missionPerformance.CitizenCollisionCount);
        stage = MissionStage.Arriving;
        resultText.text = result.FormatSummary();
        saveStatus.text = string.Empty;
        confirmLabel.text = "확인";
        isSaving = false;
        MissionDispatchSession.Clear();
        LockVehicle();
        RefreshView();
        EnsureArrivalCover();
        arrivalCover.blocksRaycasts = true;
        arrivalRoutine = StartCoroutine(FadeArrival());
    }

    // 기존 UI Canvas의 형제 순서로 검은 덮개와 결과창을 정렬해 별도 Canvas 사이의 정렬 의존을 없앤다.
    private void EnsureArrivalCover()
    {
        if (arrivalCover != null) return;
        var canvas = safeAreaRoot.GetComponentInParent<Canvas>();
        var root = new GameObject("Hospital Arrival Fade", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        root.layer = canvas.gameObject.layer;
        root.transform.SetParent(safeAreaRoot.parent, false);
        arrivalCover = root.GetComponent<CanvasGroup>();
        arrivalCover.alpha = 0f;
        arrivalCover.blocksRaycasts = false;
        var rect = (RectTransform)root.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        root.GetComponent<Image>().color = Color.black;

        // 동일 Canvas에서 주행 UI → 덮개 → 결과 안전 영역 순으로 그려 확인 버튼까지 덮이지 않게 한다.
        root.transform.SetAsLastSibling();
        safeAreaRoot.SetAsLastSibling();
        resultPanel.transform.SetAsLastSibling();
    }

    // 일시정지 동안 진행을 멈추고 완전 암전 후 결과창의 확인 입력을 허용한다.
    private IEnumerator FadeArrival()
    {
        var elapsed = 0f;
        var duration = Mathf.Max(0.01f, arrivalFadeSeconds);
        while (elapsed < duration)
        {
            elapsed += ApplicationPause.ActiveDeltaTime;
            arrivalCover.alpha = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            yield return null;
        }
        while (ApplicationPause.IsPaused) yield return null;
        arrivalCover.alpha = 1f;
        arrivalRoutine = null;
        stage = MissionStage.Result;
        RefreshView();
    }

    // 비활성화와 씬 종료에서 검은 덮개와 입력 차단이 남지 않게 정리한다.
    private void StopArrival()
    {
        if (arrivalRoutine != null) StopCoroutine(arrivalRoutine);
        arrivalRoutine = null;
        if (arrivalCover == null) return;
        arrivalCover.alpha = 0f;
        arrivalCover.blocksRaycasts = false;
    }

    // 같은 결과 후보를 저장하고 성공한 경우에만 다음 일차 Waiting으로 이동한다.
    private void ConfirmResult()
    {
        if (ApplicationPause.IsPaused || !isActiveAndEnabled || stage != MissionStage.Result || result == null || isSaving) return;
        if (!Application.CanStreamedLevelBeLoaded("Waiting"))
        {
            ShowSaveError("대기 화면을 열지 못했습니다.\n다시 시도해주세요.");
            return;
        }
        if (!GameProgressSession.IsLoaded || !ReferenceEquals(GameProgressSession.Current, result.OriginalProgress))
        {
            ShowSaveError("진행 상태를 확인하지 못했습니다.\n게임을 다시 실행해주세요.");
            return;
        }
        isSaving = true;
        confirmButton.interactable = false;
        saveStatus.text = "저장 중…";
        if (!GameProgressSession.TrySave(result.NextProgress, out var error))
        {
            isSaving = false;
            ShowSaveError("진행 데이터를 저장하지 못했습니다.\n다시 시도해주세요.");
            Debug.LogWarning($"하루 완료 저장 실패: {error}", this);
            return;
        }
        stage = MissionStage.Completed;
        saveStatus.text = "저장 완료 · 대기 씬으로 이동 중";
        RefreshView();
        SceneFadeTransition.LoadScene("Waiting");
    }

    // 결과와 후보는 유지하고 저장만 다시 시도할 수 있도록 입력을 복구한다.
    private void ShowSaveError(string message)
    {
        saveStatus.text = message;
        confirmLabel.text = "다시 시도";
        confirmButton.interactable = true;
    }

    // 기존 제안 UI를 숨기고 현재 주행 단계에 맞춰 안내와 입력 허용 여부를 갱신한다.
    private void RefreshView()
    {
        missionButton.gameObject.SetActive(false);
        notification.SetActive(false);
        briefingPanel.SetActive(false);
        missionStatus.gameObject.SetActive(stage != MissionStage.Arriving && stage != MissionStage.Result && stage != MissionStage.Completed);
        resultPanel.SetActive(stage == MissionStage.Result || stage == MissionStage.Completed);
        if (resultPanel.activeSelf)
        {
            // 런타임 내비게이션과 주행 UI보다 결과를 위에 배치해 확인 입력을 가리지 않게 한다.
            safeAreaRoot.SetAsLastSibling();
            resultPanel.transform.SetAsLastSibling();
        }
        confirmButton.interactable = stage == MissionStage.Result && !isSaving;
        driveInput.SetMissionDrivingAllowed(stage == MissionStage.Active || stage == MissionStage.Transporting);
        var transporting = stage == MissionStage.Transporting && IsPatientPickedUp;
        if (patientHealth != null)
        {
            patientHealth.SetTransporting(transporting);
            patientHealth.SetTimerRunning(stage == MissionStage.Active || stage == MissionStage.Rescuing || transporting);
        }
        if (patientECGPanel != null) patientECGPanel.gameObject.SetActive(transporting);
        if (navigation != null)
        {
            navigation.SetGuidanceVisible(IsMissionActive);
        }
        missionStatus.text = stage == MissionStage.Rescuing ? "환자 구출 및 탑승 중"
            : stage == MissionStage.Arriving ? "병원 도착 · 환자 인계 중"
            : stage == MissionStage.Transporting ? "환자 픽업 완료 · 병원 후송"
            : stage == MissionStage.Active ? "환자 픽업 장소로 이동"
            : stage == MissionStage.Result ? "병원 후송 완료 · 결과 확인"
            : stage == MissionStage.Completed ? "병원 후송 완료 · 대기 씬 복귀 중" : "출동 준비 중";
    }

    /// <summary>구출 iterator의 실패를 UI/코루틴 경계에서 한 번 관찰한다. 후송 준비가 끝나야 조작을 복구한다.</summary>
    private IEnumerator RunRescue()
    {
        // iterator를 직접 진행해 예상치 못한 예외도 상태 잠금/비활성화 경로로 정리한다.
        var sequence = rescueCutscene.Play(ambulance, ambulanceCollider);
        while (true)
        {
            // 다음 대기 명령의 존재 여부와 Unity 코루틴에 전달할 managed 대기 객체다.
            bool hasNext;
            object instruction;
            try
            {
                hasNext = sequence.MoveNext();
                instruction = hasNext ? sequence.Current : null;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                enabled = false;
                yield break;
            }
            if (!hasNext)
            {
                break;
            }
            yield return instruction;
        }
        rescueRoutine = null;
        try
        {
            if (!isActiveAndEnabled || stage != MissionStage.Rescuing || rescueCutscene == null || !rescueCutscene.IsCompleted)
            {
                FailConfiguration("구출 컷신이 중단되었습니다. 구출 컴포넌트와 차량 참조를 확인해주세요.");
                yield break;
            }
            IsPatientPickedUp = true;
            // 컷신 완료 뒤에만 새 시드를 확정하고 맵을 연장한다. 사전 검증과 같은 순서를 재현한다.
            if (!mapGenerator.TrySelectTransportSeed(CurrentSeed, out var seed, out var error)
                || !mapGenerator.TryAppendTransport(seed, out error))
            {
                FailConfiguration(error);
                yield break;
            }
            TransportSeed = seed;
            previousPosition = ambulance.position;
            UnlockVehicle();
            stage = MissionStage.Transporting;
            RefreshView();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            enabled = false;
        }
    }

    /// <summary>미션 입력을 비우고 기존 물리 제약을 보관한다. 차량을 순간이동하지 않고 그 자리에서 멈춘다.</summary>
    private void LockVehicle()
    {
        if (isVehicleLocked || ambulance == null)
        {
            return;
        }
        originalConstraints = ambulance.constraints;
        isVehicleLocked = true;
        ambulance.linearVelocity = Vector2.zero;
        ambulance.angularVelocity = 0f;
        ambulance.constraints = RigidbodyConstraints2D.FreezeAll;
    }

    /// <summary>컷신이 소유한 물리 제약만 복구한다. 원래 Inspector 설정은 유지한다.</summary>
    private void UnlockVehicle()
    {
        if (isVehicleLocked && ambulance != null)
        {
            ambulance.constraints = originalConstraints;
        }
        isVehicleLocked = false;
    }

    /// <summary>활성 기간이 끝나면 컷신 코루틴, 임시 표시, 차량 잠금을 함께 정리한다.</summary>
    private void StopRescue()
    {
        if (rescueRoutine != null)
        {
            StopCoroutine(rescueRoutine);
            rescueRoutine = null;
        }
        if (rescueCutscene != null)
        {
            rescueCutscene.Cancel();
        }
        UnlockVehicle();
    }

    /// <summary>기존 씬 참조를 보존하며 필요한 표시 전용 오브젝트만 런타임으로 추가한다.</summary>
    private bool EnsurePresentation()
    {
        if (isPresentationReady)
        {
            if (navigation != null && navigationCamera != null && rescueCutscene != null
                && patientECGPanel != null && patientHealth != null && patientECG != null
                && ambulanceRenderer != null && ambulanceCollider != null && trafficSignal != null && rescueCutscene.isActiveAndEnabled)
            {
                return true;
            }
            FailConfiguration("내비게이션 또는 구출 표시 참조가 사라졌습니다. 씬 구성을 확인해주세요.");
            return false;
        }
        if (!ValidateReferences())
        {
            return false;
        }
        ambulanceRenderer = ambulance.GetComponent<SpriteRenderer>();
        ambulanceCollider = ambulance.GetComponent<BoxCollider2D>();
        if (navigationCamera == null)
        {
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                if (root.name == "Main Camera")
                {
                    navigationCamera = root.GetComponent<Camera>();
                    break;
                }
            }
        }
        if (navigationCamera == null || !navigationCamera.orthographic || ambulanceRenderer == null || ambulanceCollider == null)
        {
            FailConfiguration("Orthographic Main Camera, Ambulance SpriteRenderer와 BoxCollider2D를 연결해주세요.");
            return false;
        }
        if (rescueCutscene == null)
        {
            rescueCutscene = GetComponent<PatientRescueCutscene>();
            if (rescueCutscene == null)
            {
                rescueCutscene = gameObject.AddComponent<PatientRescueCutscene>();
            }
        }
        if (navigationPanel == null)
        {
            // 같은 씬 Canvas 아래 이름이 일치하는 기존 패널만 재사용한다. 매 프레임 검색하지 않는다.
            var canvas = safeAreaRoot.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                FailConfiguration("미션 Safe Area Root는 Canvas 아래에 있어야 합니다.");
                return false;
            }
            foreach (var candidate in canvas.GetComponentsInChildren<RectTransform>(true))
            {
                if (candidate.name == "NavigationPanel")
                {
                    navigationPanel = candidate;
                    break;
                }
            }
        }
        if (navigationPanel == null)
        {
            var panel = new GameObject("NavigationPanel", typeof(RectTransform), typeof(Image));
            panel.layer = safeAreaRoot.gameObject.layer;
            navigationPanel = (RectTransform)panel.transform;
            navigationPanel.SetParent(safeAreaRoot, false);
            navigationPanel.anchorMin = navigationPanel.anchorMax = navigationPanel.pivot = Vector2.one;
            navigationPanel.anchoredPosition = new Vector2(-12f, -58f);
            navigationPanel.sizeDelta = new Vector2(160f, 160f);
            var background = panel.GetComponent<Image>();
            background.color = new Color(0.04f, 0.08f, 0.14f, 0.94f);
            background.raycastTarget = false;
        }
        if (navigationPanel.GetComponent<RectMask2D>() == null)
        {
            navigationPanel.gameObject.AddComponent<RectMask2D>();
        }
        navigation = navigationPanel.GetComponentInChildren<MissionNavigationGraphic>(true);
        if (navigation == null)
        {
            // 표시 컴포넌트의 활성화 콜백 전에 필수 UI 렌더러를 준비한다.
            var display = new GameObject("MissionNavigation", typeof(RectTransform), typeof(CanvasRenderer));
            display.layer = navigationPanel.gameObject.layer;
            var displayRect = (RectTransform)display.transform;
            displayRect.SetParent(navigationPanel, false);
            displayRect.anchorMin = Vector2.zero;
            displayRect.anchorMax = Vector2.one;
            displayRect.offsetMin = displayRect.offsetMax = Vector2.zero;
            navigation = display.AddComponent<MissionNavigationGraphic>();
        }
        navigationPanel.gameObject.SetActive(true);
        navigation.gameObject.SetActive(true);
        if (!rescueCutscene.isActiveAndEnabled)
        {
            FailConfiguration("PatientRescueCutscene은 활성화된 미션 오브젝트에 연결해주세요.");
            return false;
        }
        navigation.Configure(mapGenerator, ambulance.transform, navigationCamera);
        navigation.SetGuidanceVisible(IsMissionActive);
        if (!EnsurePatientPresentation()) return false;
        var traffic = mapGenerator.GetComponent<RoadTrafficController>();
        if (traffic == null)
        {
            FailConfiguration("교차로 신호 표시에는 Map Generator의 RoadTrafficController가 필요합니다.");
            return false;
        }
        // 안내 문구의 아래쪽 중앙에 붙여 기존 안전 영역과 해상도 대응을 그대로 따른다.
        var signalObject = new GameObject("TrafficSignal", typeof(RectTransform), typeof(CanvasRenderer));
        signalObject.layer = missionStatus.gameObject.layer;
        var signalRect = (RectTransform)signalObject.transform;
        signalRect.SetParent(missionStatus.rectTransform, false);
        signalRect.anchorMin = signalRect.anchorMax = new Vector2(0.5f, 0f);
        signalRect.pivot = new Vector2(0.5f, 1f);
        signalRect.anchoredPosition = new Vector2(0f, -8f);
        signalRect.sizeDelta = new Vector2(112f, 32f);
        trafficSignal = signalObject.AddComponent<TrafficSignalGraphic>();
        trafficSignal.Configure(this, mapGenerator, traffic, ambulanceCollider);
        isPresentationReady = true;
        return true;
    }

    // 기존 패널 배치를 보존하고 HP 원본과 마스킹된 표시 전용 자식을 한 번 연결한다.
    private bool EnsurePatientPresentation()
    {
        if (patientECGPanel == null)
        {
            var canvas = safeAreaRoot.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                foreach (var candidate in canvas.GetComponentsInChildren<RectTransform>(true))
                {
                    if (candidate.name != "PatientECGPanel") continue;
                    patientECGPanel = candidate;
                    break;
                }
            }
        }
        if (patientECGPanel == null || patientECGPanel.GetComponent<Image>() == null)
        {
            FailConfiguration("Canvas 아래 PatientECGPanel의 RectTransform과 배경 Image를 연결해주세요.");
            return false;
        }
        if (patientHealth == null)
        {
            patientHealth = ambulance.GetComponent<PatientHealth>();
            if (patientHealth == null) patientHealth = ambulance.gameObject.AddComponent<PatientHealth>();
        }
        patientECGPanel.GetComponent<Image>().raycastTarget = false;
        if (patientECGPanel.GetComponent<RectMask2D>() == null)
            patientECGPanel.gameObject.AddComponent<RectMask2D>();
        patientECG = patientECGPanel.GetComponentInChildren<PatientECGGraphic>(true);
        if (patientECG == null)
        {
            var display = new GameObject("PatientECG", typeof(RectTransform), typeof(CanvasRenderer));
            display.layer = patientECGPanel.gameObject.layer;
            var displayRect = (RectTransform)display.transform;
            displayRect.SetParent(patientECGPanel, false);
            displayRect.anchorMin = Vector2.zero;
            displayRect.anchorMax = Vector2.one;
            displayRect.offsetMin = displayRect.offsetMax = Vector2.zero;
            patientECG = display.AddComponent<PatientECGGraphic>();
        }
        patientHealth.ResetPatient();
        patientECG.ResetPatient(patientHealth);
        patientECGPanel.gameObject.SetActive(false);
        return true;
    }

    /// <summary>조치 가능한 설정 오류를 한 번 기록하고 컴포넌트를 비활성화한다.</summary>
    private void FailConfiguration(string error)
    {
        if (missionStatus != null)
        {
            missionStatus.text = "미션 설정을 확인해주세요";
        }
        Debug.LogError(error, this);
        stage = MissionStage.Waiting;
        CurrentSeed = 0;
        enabled = false;
    }

    /// <summary>외부 공개 호출과 대기 완료에서도 파괴된 참조를 검사한다. 비활성화만으로 외부 호출이 차단되지는 않는다.</summary>
    private bool ValidateReferences()
    {
        if (missionButton == null || notification == null || briefingPanel == null || briefingText == null
            || acceptButton == null || rejectButton == null || missionStatus == null || mapGenerator == null
            || ambulance == null || driveInput == null || safeAreaRoot == null || !ambulance.simulated
            || ambulance.bodyType != RigidbodyType2D.Dynamic || missionPerformance == null
            || !missionPerformance.isActiveAndEnabled || missionPerformance.gameObject != ambulance.gameObject)
        {
            FailConfiguration("CodeBlue Rush > Set Up GamePlay Missions로 UI와 운전 참조를 연결해주세요. "
                + "Ambulance에는 simulated Dynamic Rigidbody2D와 연결된 MissionPerformance가 필요합니다.");
            return false;
        }
        if (resultPanel == null || resultText == null || saveStatus == null || confirmButton == null || confirmLabel == null)
        {
            FailConfiguration("GamePlay의 결과 패널·본문·저장 안내·확인 버튼과 문구를 연결해주세요.");
            return false;
        }
        return true;
    }

    /// <summary>정규화된 화면 좌표로 안전 영역을 적용한다. 버튼의 기존 배치는 유지한다.</summary>
    private void UpdateSafeArea()
    {
        if (safeAreaRoot == null || Screen.width <= 0 || Screen.height <= 0)
        {
            return;
        }
        var safeArea = Screen.safeArea;
        if (safeArea == lastSafeArea && Screen.width == lastScreenWidth && Screen.height == lastScreenHeight)
        {
            return;
        }
        lastSafeArea = safeArea;
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        safeAreaRoot.anchorMin = new Vector2(safeArea.xMin / Screen.width, safeArea.yMin / Screen.height);
        safeAreaRoot.anchorMax = new Vector2(safeArea.xMax / Screen.width, safeArea.yMax / Screen.height);
        safeAreaRoot.offsetMin = safeAreaRoot.offsetMax = Vector2.zero;
    }

    // 미션 준비와 진행은 상호 배타적이다. UI 클릭으로 전이가 중복되지 않게 명시적으로 구분한다.
    private enum MissionStage
    {
        Waiting,
        Preparing,
        Active,
        Rescuing,
        Transporting,
        Arriving,
        Result,
        Completed
    }
}
