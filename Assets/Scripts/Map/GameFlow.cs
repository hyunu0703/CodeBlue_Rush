using System;
using UnityEngine;

/// <summary>기존 미션 이벤트를 연결하고 고정 맵에서 출동과 결과 및 미션 초기화를 관리한다</summary>
[DisallowMultipleComponent]
public sealed class GameFlow : MonoBehaviour
{
    public enum Phase { Preparing, Report, DrivingToPatient, Pickup, Transporting, Result, GameOver }
    [SerializeField] private CityMap map;
    [SerializeField] private PatientReport report;
    [SerializeField] private PatientPickup pickup;
    [SerializeField] private PatientECG ecg;
    [SerializeField] private HospitalTransfer transfer;
    [SerializeField] private NavigationRoute navigation;
    [SerializeField] private AmbulanceController ambulance;
    [SerializeField] private HospitalArrivalZone hospital;
    [SerializeField] private Transform ambulanceSpawnPoint;
    [SerializeField] private AmbulanceCamera ambulanceCamera;
    private Rigidbody2D body;
    private SirenController siren;
    private AmbulanceCollision collision;
    private bool started;
    private bool transitioning;
    private int missionId;
    public Phase State { get; private set; }
    public int Stars { get; private set; }
    public string Error { get; private set; }
    public event Action Changed;

    // 모든 컴포넌트 활성화 후 기존 도시 준비와 보고를 시작한다
    private void Start()
    {
        if (!map || !report || !pickup || !ecg || !transfer || !navigation || !ambulance || !hospital || !ambulanceSpawnPoint)
        {
            FailSetup("Gameplay references are missing.");
            return;
        }
        body = ambulance.GetComponent<Rigidbody2D>();
        siren = ambulance.GetComponent<SirenController>();
        collision = ambulance.GetComponent<AmbulanceCollision>();
        started = true;
        Subscribe();
        BeginWorld();
    }

    // 재활성화에서는 현재 미션의 구독만 복구한다
    private void OnEnable()
    {
        if (started)
            Subscribe();
    }

    // 이벤트 중복 구독을 방지한다
    private void Subscribe()
    {
        Unsubscribe();
        pickup.BoardingStarted += HandleBoarding;
        report.PatientPickedUp += HandlePickup;
        report.FatalCollision += HandleFailure;
        ecg.PatientDied += HandleFailure;
        transfer.HospitalArrived += HandleArrival;
    }

    // 실제 연결된 시스템에서 구독을 해제한다
    private void Unsubscribe()
    {
        if (pickup) pickup.BoardingStarted -= HandleBoarding;
        if (report)
        {
            report.PatientPickedUp -= HandlePickup;
            report.FatalCollision -= HandleFailure;
        }
        if (ecg) ecg.PatientDied -= HandleFailure;
        if (transfer) transfer.HospitalArrived -= HandleArrival;
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    // 최초 시작과 미션 초기화에서 고정 맵을 확인하고 소방서 위치로 복귀한다
    private void BeginWorld()
    {
        transitioning = true;
        Error = null;
        SetState(Phase.Preparing);
        SetDriving(false);
        ClearMission();
        bool ready = map.Initialize(out string error);
        if (!ready || !hospital.IsValidFor(map))
        {
            FailSetup(error ?? "No valid hospital in this city.");
            transitioning = false;
            return;
        }
        Vector3 position = ambulanceSpawnPoint.position;
        float angle = ambulanceSpawnPoint.eulerAngles.z;
        body.transform.SetPositionAndRotation(position, ambulanceSpawnPoint.rotation);
        body.position = position;
        body.rotation = angle;
        if (ambulanceCamera)
            ambulanceCamera.ResetFollow();
        Physics2D.SyncTransforms();
        BeginMission();
        transitioning = false;
    }

    // 도시와 플레이어 위치를 유지한 채 새 보고만 발급한다
    private void BeginMission()
    {
        Stars = 0;
        Error = null;
        SetState(Phase.Report);
        SetDriving(true);
        if (!report.TryReport(out string error))
        {
            SetDriving(false);
            FailSetup(error);
            return;
        }
        missionId = report.MissionId;
        SetState(Phase.DrivingToPatient);
    }

    // 결과를 확인한 사용자의 입력으로 다음 미션을 요청한다
    public void Continue()
    {
        if (!isActiveAndEnabled || transitioning)
            return;
        if (State == Phase.GameOver || State == Phase.Result)
            BeginWorld();
    }

    // 기존 취소 이벤트가 픽업, ECG, 병원과 미션 목적지를 초기화한다
    private void ClearMission()
    {
        missionId = 0;
        report.CancelReport();
        navigation.ClearDestination();
        if (collision)
            collision.ResetCollision();
    }

    private bool IsCurrentMission()
    {
        return !transitioning && report.IsActive && missionId != 0 && missionId == report.MissionId;
    }

    private void HandleBoarding()
    {
        if (IsCurrentMission() && State == Phase.DrivingToPatient)
            SetState(Phase.Pickup);
    }

    private void HandlePickup()
    {
        if (IsCurrentMission() && (State == Phase.Pickup || State == Phase.DrivingToPatient))
            SetState(Phase.Transporting);
    }

    // 도착 시스템이 고정한 ECG와 현재 미션 충돌 횟수로 결과를 확정한다
    private void HandleArrival()
    {
        if (!IsCurrentMission() || State != Phase.Transporting || transfer.MissionId != missionId || transfer.Status != HospitalTransfer.TransferStatus.Arrived)
            return;
        if (report.HasFatalCollision || ecg.Value <= 0f || report.CollisionCount >= 2)
        {
            HandleFailure();
            return;
        }
        Stars = Evaluate(transfer.ArrivalState, report.CollisionCount);
        if (Stars == 0)
        {
            HandleFailure();
            return;
        }
        SetDriving(false);
        SetState(Phase.Result);
    }

    // 불가능한 조합은 성공 점수로 처리하지 않는다
    public static int Evaluate(PatientECG.ECGState state, int collisions)
    {
        if (collisions == 0)
            return state == PatientECG.ECGState.Green || state == PatientECG.ECGState.Yellow ? 3 : state == PatientECG.ECGState.Red ? 2 : 0;
        if (collisions == 1)
            return state == PatientECG.ECGState.Yellow ? 2 : state == PatientECG.ECGState.Red ? 1 : 0;
        return 0;
    }

    // 최초 실패만 확정하고 기존 미션을 중지한 뒤 새 미션 입력을 기다린다
    private void HandleFailure()
    {
        if (!IsCurrentMission() || State == Phase.Result || State == Phase.GameOver)
            return;
        State = Phase.GameOver;
        Stars = 0;
        SetDriving(false);
        ClearMission();
        Changed?.Invoke();
    }

    // 결과 화면에서는 입력과 물리 접촉을 함께 중지한다
    private void SetDriving(bool active)
    {
        ambulance.enabled = active;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        body.simulated = active;
        if (!active && siren && siren.IsOn)
            siren.Toggle();
    }

    private void SetState(Phase state)
    {
        State = state;
        Changed?.Invoke();
    }

    private void FailSetup(string error)
    {
        Error = error;
        Debug.LogError(error, this);
        Changed?.Invoke();
    }
}
