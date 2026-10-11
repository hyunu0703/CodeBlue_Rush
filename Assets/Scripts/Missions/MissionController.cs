using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>미션 상태의 단일 소유자다. 대기, 제안, 브리핑, 맵 준비, 진행을 관리하며 수락 뒤에만 시드를 만든다.</summary>
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

    /// <summary>수락 및 맵 준비가 끝난 미션이 진행 중인지 반환한다.</summary>
    public bool IsMissionActive => stage == MissionStage.Active;
    /// <summary>현재 수락한 미션의 8자리 시드다. 대기/제안 단계에는 0이다.</summary>
    public int CurrentSeed { get; private set; }
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
            RefreshView();
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
        if (!isActiveAndEnabled || stage != MissionStage.Briefing || !ValidateReferences())
        {
            return;
        }
        stage = MissionStage.Preparing;
        RefreshView();
        try
        {
            // 세 번 회전하며 자기 경로를 덮는 시드는 제외한다. 고정 상한으로 무한 재시도를 막는다.
            var seed = 0;
            for (var attempt = 0; attempt < 256; attempt++)
            {
                var candidate = MissionSeedRules.CreateSeed(random);
                if (MissionSeedRules.TryBuildRoute(candidate, out _, out _))
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

            // 입력은 아직 차단된 상태다. 속도를 비운 후 Rigidbody 위치로 순간이동하고 보간 잔상을 방지한다.
            var interpolation = ambulance.interpolation;
            ambulance.interpolation = RigidbodyInterpolation2D.None;
            ambulance.linearVelocity = Vector2.zero;
            ambulance.angularVelocity = 0f;
            ambulance.position = mapGenerator.StartPosition;
            ambulance.rotation = mapGenerator.StartRotation;
            ambulance.interpolation = interpolation;
            CurrentSeed = seed;
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
        CurrentSeed = 0;
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
        // 픽업/컷신/병원 단계는 후속 구현이다. 현재 브리핑은 이번 단계의 도착지 이동 임무를 안내한다.
        briefingText.text = $"출동 요청 #{offerNumber:000}\n\n"
            + "긴급 출동 요청이 도착했습니다.\n\n"
            + "수락하면 새 도로 구역과 이동 경로가 생성됩니다. 구급차를 운전해 마지막 도로의 도착 표시까지 이동하세요.\n\n"
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
        driveInput.SetMissionDrivingAllowed(IsMissionActive);
        missionStatus.text = IsMissionActive ? $"출동 중 · 시드 {CurrentSeed:D8}" : "출동 요청 대기 중";
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
        Active
    }
}
