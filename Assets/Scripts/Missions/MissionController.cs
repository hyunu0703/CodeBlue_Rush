using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>미션 상태의 단일 소유자다. 수락, 픽업 이동, 구출 컷신, 후송을 순서대로 관리한다.</summary>
[DisallowMultipleComponent]
public sealed class MissionController : MonoBehaviour
{
    // 제안 없는 대기 시간은 실제 초 단위다. 일시정지 중에도 알림 대기는 진행한다.
    [SerializeField, Min(0.1f)] private float offerDelaySeconds = 5f;
    // 기존 미션 버튼과 이번 기능에서 추가한 브리핑/알림 UI 참조다.
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

    // 활성 기간의 대기 코루틴과 실제로 리스너를 연결한 버튼 발행자를 보관한다.
    private Coroutine offerRoutine;
    private Button subscribedMissionButton;
    private Button subscribedAcceptButton;
    private Button subscribedRejectButton;
    // 난수 소스와 제안 번호는 이 씬의 미션 관리자 인스턴스 수명에 속한다.
    private readonly System.Random random = new System.Random();
    private int offerNumber;
    private MissionStage stage;
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
    /// <summary>현재 수락한 미션의 8자리 시드다. 대기/제안 단계에는 0이다.</summary>
    public int CurrentSeed { get; private set; }
    /// <summary>픽업 컷신 완료 뒤에 생성한 후송 시드다. 후송 시작 전에는 0이다.</summary>
    public int TransportSeed { get; private set; }
    /// <summary>구출 이미지가 모두 사라진 뒤에만 true가 된다. 다음 미션 대기 진입 시 초기화한다.</summary>
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

    /// <summary>직렬화된 참조를 검증한 뒤 리스너를 연결하고 현재 상태를 즉시 UI에 반영한다.</summary>
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

        Unsubscribe();
        subscribedMissionButton = missionButton;
        subscribedAcceptButton = acceptButton;
        subscribedRejectButton = rejectButton;
        subscribedMissionButton.onClick.AddListener(OpenBriefing);
        subscribedAcceptButton.onClick.AddListener(AcceptMission);
        subscribedRejectButton.onClick.AddListener(RejectMission);
        UpdateSafeArea();
        if (stage == MissionStage.Waiting)
        {
            BeginWaiting();
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

    /// <summary>씬의 Awake가 끝난 뒤 표시 참조를 연결한다. 이미 저장된 NavigationPanel이 있으면 재사용한다.</summary>
    private void Start()
    {
        EnsurePresentation();
    }

    /// <summary>이전 물리 위치부터 현재 위치까지 검사해 빠른 이동으로 도착 영역을 건너뛰어도 한 번만 처리한다.</summary>
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
        // 픽업은 차량 이미지와 콜라이더 전체가 주차 사각형 안에 들어오면 시작한다. 병원에는 기존 이동 선분 감지를 유지한다.
        var currentPosition = ambulance.position;
        mapGenerator.UpdateRoadStreaming(ambulanceCollider.bounds, ambulance.linearVelocity);
        var movement = currentPosition - previousPosition;
        var fraction = movement.sqrMagnitude > 0f
            ? Mathf.Clamp01(Vector2.Dot(mapGenerator.DestinationPosition - previousPosition, movement) / movement.sqrMagnitude) : 0f;
        var closest = previousPosition + movement * fraction;
        previousPosition = currentPosition;
        var hasArrived = stage == MissionStage.Active
            ? rescueCutscene.IsVehicleInsidePickup(ambulance, ambulanceRenderer, ambulanceCollider)
            : (closest - mapGenerator.DestinationPosition).sqrMagnitude <= arrivalRadius * arrivalRadius;
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
            ambulance.linearVelocity = Vector2.zero;
            ambulance.angularVelocity = 0f;
            CompleteMission();
            missionStatus.text = "병원 후송 완료 · 다음 출동 요청 대기 중";
        }
    }

    /// <summary>화면 크기와 모바일 브라우저 안전 영역 변경을 감지한다. 변경되지 않은 UI는 다시 쓰지 않는다.</summary>
    private void Update()
    {
        UpdateSafeArea();
    }

    /// <summary>컴포넌트 비활성화 시에도 대기를 명시적으로 중지하고 UI와 운전 입력을 정리한다.</summary>
    private void OnDisable()
    {
        StopWaiting();
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
        Unsubscribe();
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

    /// <summary>대기 중인 제안이 있을 때만 브리핑을 연다. 진행 중인 미션이나 중복 클릭은 무시한다.</summary>
    public void OpenBriefing()
    {
        if (!isActiveAndEnabled || stage != MissionStage.Available || !ValidateReferences())
        {
            return;
        }
        stage = MissionStage.Briefing;
        RefreshView();
    }

    /// <summary>열린 제안을 수락하면 시드를 만들고 맵을 준비한다. 성공한 경우에만 진행 판정과 시드를 공개한다.</summary>
    public void AcceptMission()
    {
        if (!isActiveAndEnabled || stage != MissionStage.Briefing || !ValidateReferences() || !EnsurePresentation())
        {
            return;
        }
        stage = MissionStage.Preparing;
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
            previousPosition = ambulance.position;
            // 픽업 장소는 노란 테두리 자체로 표시한다. 기존 글자가 사각형과 차량을 가리지 않게 비운다.
            mapGenerator.SetDestinationLabel(string.Empty);
            rescueCutscene.ShowPickup(pickupSidewalkPosition, pickupRotation, ambulanceRenderer);
            stage = MissionStage.Active;
            RefreshView();
            OnMissionAccepted?.Invoke(seed);
        }
        catch (Exception exception)
        {
            // UI 이벤트 경계에서 예상치 못한 실패를 한 번 관찰하고 추가 수락과 조작을 막는다.
            Debug.LogException(exception, this);
            stage = MissionStage.Briefing;
            CurrentSeed = 0;
            enabled = false;
        }
    }

    /// <summary>현재 제안을 거절한다. 시드와 수락 판정 없이 새 제안까지 다시 5초를 기다린다.</summary>
    public void RejectMission()
    {
        if (!isActiveAndEnabled || stage != MissionStage.Briefing || !ValidateReferences())
        {
            return;
        }
        BeginWaiting();
    }

    /// <summary>후속 결과 시스템이 미션 종료를 알릴 진입점이다. 이전 맵은 다음 수락까지 유지한다.</summary>
    public void CompleteMission()
    {
        if (!isActiveAndEnabled || !IsMissionActive || !ValidateReferences())
        {
            return;
        }
        BeginWaiting();
    }

    /// <summary>제안과 시드를 비우고 대기 상태에 진입한다. 대기의 단일 소유자는 이 컴포넌트다.</summary>
    private void BeginWaiting()
    {
        StopWaiting();
        StopRescue();
        CurrentSeed = 0;
        TransportSeed = 0;
        IsPatientPickedUp = false;
        stage = MissionStage.Waiting;
        RefreshView();
        offerRoutine = StartCoroutine(WaitForOffer());
    }

    /// <summary>실제 시간으로 대기한 후 새 브리핑 번호와 알림을 표시한다. 비활성화 시 StopWaiting에서 중지한다.</summary>
    private IEnumerator WaitForOffer()
    {
        yield return new WaitForSecondsRealtime(offerDelaySeconds);
        offerRoutine = null;
        if (!isActiveAndEnabled || stage != MissionStage.Waiting || !ValidateReferences())
        {
            yield break;
        }
        offerNumber++;
        stage = MissionStage.Available;
        // 픽업과 후송의 두 목적지를 순서대로 안내한다. 구출과 탑승은 도착 후 자동으로 진행한다.
        briefingText.text = $"출동 요청 #{offerNumber:000}\n\n"
            + "긴급 출동 요청이 도착했습니다.\n\n"
            + "수락하면 픽업 장소와 내비게이션 경로가 생성됩니다. 환자를 구출한 뒤 이어지는 도로를 따라 병원으로 후송하세요.\n\n"
            + "수락: 출동 시작\n거절: 5초 뒤 새 요청 대기";
        RefreshView();
    }

    /// <summary>현재 상태를 UI와 입력 허용 여부에 동기화한다. 게임 상태를 UI에서 별도로 결정하지 않는다.</summary>
    private void RefreshView()
    {
        missionButton.gameObject.SetActive(stage == MissionStage.Available || stage == MissionStage.Briefing);
        missionButton.interactable = stage == MissionStage.Available;
        notification.SetActive(stage == MissionStage.Available);
        briefingPanel.SetActive(stage == MissionStage.Briefing || stage == MissionStage.Preparing);
        acceptButton.interactable = rejectButton.interactable = stage == MissionStage.Briefing;
        driveInput.SetMissionDrivingAllowed(stage == MissionStage.Active || stage == MissionStage.Transporting);
        if (navigation != null)
        {
            navigation.SetGuidanceVisible(IsMissionActive);
        }
        missionStatus.text = stage == MissionStage.Rescuing ? "환자 구출 및 탑승 중"
            : stage == MissionStage.Transporting ? "환자 픽업 완료 · 병원 후송"
            : stage == MissionStage.Active ? "환자 픽업 장소로 이동" : "출동 요청 대기 중";
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
            mapGenerator.SetDestinationLabel("병원");
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
                && ambulanceRenderer != null && ambulanceCollider != null && rescueCutscene.isActiveAndEnabled)
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
        isPresentationReady = true;
        return true;
    }

    /// <summary>활성 기간의 진행 중 대기를 중지한다. 재활성화된 대기 상태는 5초를 새로 센다.</summary>
    private void StopWaiting()
    {
        if (offerRoutine != null)
        {
            StopCoroutine(offerRoutine);
            offerRoutine = null;
        }
    }

    /// <summary>실제로 연결했던 동일 버튼에서 동일 메서드 리스너만 제거한다.</summary>
    private void Unsubscribe()
    {
        if (subscribedMissionButton != null)
        {
            subscribedMissionButton.onClick.RemoveListener(OpenBriefing);
        }
        if (subscribedAcceptButton != null)
        {
            subscribedAcceptButton.onClick.RemoveListener(AcceptMission);
        }
        if (subscribedRejectButton != null)
        {
            subscribedRejectButton.onClick.RemoveListener(RejectMission);
        }
        subscribedMissionButton = subscribedAcceptButton = subscribedRejectButton = null;
    }

    /// <summary>조치 가능한 설정 오류를 한 번 기록하고 컴포넌트를 비활성화한다.</summary>
    private void FailConfiguration(string error)
    {
        if (missionStatus != null)
        {
            missionStatus.text = "미션 설정을 확인해주세요";
        }
        Debug.LogError(error, this);
        stage = offerNumber > 0 ? MissionStage.Briefing : MissionStage.Waiting;
        CurrentSeed = 0;
        enabled = false;
    }

    /// <summary>외부 공개 호출과 대기 완료에서도 파괴된 참조를 검사한다. 비활성화만으로 외부 호출이 차단되지는 않는다.</summary>
    private bool ValidateReferences()
    {
        if (missionButton == null || notification == null || briefingPanel == null || briefingText == null
            || acceptButton == null || rejectButton == null || missionStatus == null || mapGenerator == null
            || ambulance == null || driveInput == null || safeAreaRoot == null || !ambulance.simulated
            || ambulance.bodyType != RigidbodyType2D.Dynamic)
        {
            FailConfiguration("CodeBlue Rush > Set Up GamePlay Missions로 UI와 운전 참조를 연결해주세요. "
                + "Ambulance에는 simulated Dynamic Rigidbody2D가 필요합니다.");
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
        Available,
        Briefing,
        Preparing,
        Active,
        Rescuing,
        Transporting
    }
}
