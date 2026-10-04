using System;
using UnityEngine;

/// <summary>현재 미션의 병원 경로와 도착 또는 사망의 일회 확정을 소유한다</summary>
[DisallowMultipleComponent]
public sealed class HospitalTransfer : MonoBehaviour
{
    public enum TransferStatus { Idle, Transporting, Arrived, PatientDied, MissingReferences, NoHospital, NoRoute }
    [SerializeField] private PatientReport report;
    [SerializeField] private NavigationRoute navigation;
    [SerializeField] private PatientECG ecg;
    [SerializeField] private Rigidbody2D ambulance;
    [SerializeField] private HospitalArrivalZone[] hospitals = Array.Empty<HospitalArrivalZone>();
    [SerializeField, Min(0f)] private float stoppedSpeed = 0.15f;
    [SerializeField, Min(0f)] private float stoppedAngularSpeed = 5f;
    private PatientReport subscribedReport;
    private PatientECG subscribedECG;
    private CityMap subscribedMap;
    private HospitalArrivalZone[] subscribedZones;
    private CityMap layout;
    private TrafficLane destinationLane;
    private float destinationDistance;
    private bool selecting;
    public TransferStatus Status { get; private set; }
    public HospitalArrivalZone Destination { get; private set; }
    public int MissionId { get; private set; }
    public float ArrivalECG { get; private set; }
    public PatientECG.ECGState ArrivalState { get; private set; }
    public event Action HospitalArrived;

    // 미션과 기존 경로 및 병원 목록을 직접 참조로 연결한다
    public void Configure(PatientReport mission, NavigationRoute route, PatientECG patientECG, Rigidbody2D vehicle, HospitalArrivalZone[] zones)
    {
        bool preserveResult = report == mission && mission && MissionId == mission.MissionId && (Status == TransferStatus.Arrived || Status == TransferStatus.PatientDied);
        Unsubscribe();
        if (!preserveResult)
            ResetTransfer();
        report = mission;
        navigation = route;
        ecg = patientECG;
        ambulance = vehicle;
        hospitals = zones == null ? Array.Empty<HospitalArrivalZone>() : (HospitalArrivalZone[])zones.Clone();
        if (isActiveAndEnabled)
            Bind();
    }

    // 활성화 시 현재 미션과 생성된 도시를 연결한다
    private void OnEnable()
    {
        Bind();
    }

    // 비활성화 동안 도착 이벤트를 받지 않는다
    private void OnDisable()
    {
        Unsubscribe();
    }

    // 데이터 소유자의 이벤트를 중복 없이 구독한다
    private void Bind()
    {
        Unsubscribe();
        if (ambulance && ambulance.TryGetComponent(out AmbulanceCollision collision))
            collision.Configure(report, ecg, this);
        subscribedReport = report;
        subscribedECG = ecg;
        subscribedMap = navigation ? navigation.Map : null;
        subscribedZones = hospitals;
        if (subscribedReport)
        {
            subscribedReport.PatientPickedUp += HandlePickup;
            subscribedReport.ReportChanged += HandleReport;
        }
        if (subscribedECG)
            subscribedECG.PatientDied += HandleDeath;
        if (subscribedMap)
            subscribedMap.StateChanged += HandleMap;
        if (subscribedZones != null)
            foreach (HospitalArrivalZone zone in subscribedZones)
                if (zone)
                {
                    zone.VehicleEntered -= HandleArrival;
                    zone.VehicleEntered += HandleArrival;
                }
        HandleReport();
        HandleMap();
        HandlePickup();
        if (ecg && ecg.HasPatient && ecg.Value <= 0f)
            HandleDeath();
    }

    // 참조 교체 전 실제 구독 대상을 해제한다
    private void Unsubscribe()
    {
        if (subscribedReport)
        {
            subscribedReport.PatientPickedUp -= HandlePickup;
            subscribedReport.ReportChanged -= HandleReport;
        }
        if (subscribedECG)
            subscribedECG.PatientDied -= HandleDeath;
        if (subscribedMap)
            subscribedMap.StateChanged -= HandleMap;
        if (subscribedZones != null)
            foreach (HospitalArrivalZone zone in subscribedZones)
                if (zone)
                    zone.VehicleEntered -= HandleArrival;
        subscribedReport = null;
        subscribedECG = null;
        subscribedMap = null;
        subscribedZones = null;
    }

    // 취소 또는 다른 미션으로 바뀌면 이전 병원 참조를 제거한다
    private void HandleReport()
    {
        if (!report || !report.IsActive || (MissionId != 0 && MissionId != report.MissionId))
            ResetTransfer();
    }

    // 도시가 바뀌거나 사용 불가능하면 이전 병원을 정리한다
    private void HandleMap()
    {
        if (layout != null && (!navigation || !navigation.Map || !navigation.Map.IsReady || layout != navigation.Map))
            ResetTransfer();
    }

    // 픽업 이벤트 중복 전달은 기존 이송 또는 확정 상태를 유지한다
    private void HandlePickup()
    {
        if (report && report.IsPatientOnBoard && MissionId != report.MissionId)
            TryBeginTransfer();
    }

    // 등록된 후보만 유한하게 시도하고 첫 유효 경로에서 즉시 종료한다
    public bool TryBeginTransfer()
    {
        if (!isActiveAndEnabled || selecting || !report || !report.IsActive || !report.IsPatientOnBoard)
            return false;
        if (MissionId == report.MissionId && (Status == TransferStatus.Transporting || Status == TransferStatus.Arrived || Status == TransferStatus.PatientDied))
            return Status == TransferStatus.Transporting;
        MissionId = report.MissionId;
        if (!navigation || !navigation.isActiveAndEnabled || !ecg || !ecg.isActiveAndEnabled || !ambulance || !navigation.Map || !navigation.Map.IsReady)
        {
            Status = TransferStatus.MissingReferences;
            return false;
        }
        if (ecg.HasPatient && ecg.Value <= 0f)
        {
            Status = TransferStatus.PatientDied;
            return false;
        }
        layout = navigation.Map;
        Status = TransferStatus.NoHospital;
        selecting = true;
        try
        {
            foreach (HospitalArrivalZone zone in hospitals)
            {
                if (!zone || !zone.IsValidFor(navigation.Map))
                    continue;
                Status = TransferStatus.NoRoute;
                if (!navigation.SetDestination(zone.Lane, zone.Distance))
                {
                    navigation.ClearDestination();
                    if (navigation.Map == null || !navigation.Map.IsReady)
                        break;
                    continue;
                }
                if (!report.IsPatientOnBoard || report.MissionId != MissionId || layout != navigation.Map)
                    return false;
                Destination = zone;
                destinationLane = zone.Lane;
                destinationDistance = zone.Distance;
                Status = TransferStatus.Transporting;
                return true;
            }
        }
        finally
        {
            selecting = false;
        }
        return false;
    }

    // 등록된 물리 차량의 지정 병원 진입만 처리한다
    private void HandleArrival(HospitalArrivalZone zone, Rigidbody2D vehicle)
    {
        if (vehicle == ambulance)
            TryArrive(zone);
    }

    // 살아 있는 현재 환자와 올바른 현장만 한 번 도착 확정한다
    public bool TryArrive(HospitalArrivalZone zone)
    {
        if (!ambulance || ambulance.linearVelocity.sqrMagnitude > stoppedSpeed * stoppedSpeed || Mathf.Abs(ambulance.angularVelocity) > stoppedAngularSpeed)
            return false;
        if (!isActiveAndEnabled || Status != TransferStatus.Transporting || !report || !report.IsActive || !report.IsPatientOnBoard || report.MissionId != MissionId || !navigation || !navigation.isActiveAndEnabled || navigation.Status != NavigationRoute.RouteStatus.Ready || !ecg || !ecg.isActiveAndEnabled || !ecg.HasPatient || ecg.Value <= 0f || !zone || zone != Destination || zone.Lane != destinationLane || !zone.IsValidFor(navigation.Map) || layout != navigation.Map || !zone.Contains(ambulance))
            return false;
        Status = TransferStatus.Arrived;
        ecg.StopDecay();
        ArrivalECG = ecg.Value;
        ArrivalState = ecg.State;
        ClearOwnedRoute();
        HospitalArrived?.Invoke();
        return true;
    }

    // 사망이 먼저 확정되면 이후 도착 요청을 차단한다
    private void HandleDeath()
    {
        if (!report || !report.IsPatientOnBoard || report.MissionId != MissionId || Status == TransferStatus.Arrived)
            return;
        Status = TransferStatus.PatientDied;
        ClearOwnedRoute();
    }

    // 다른 시스템의 목적지를 제거하지 않고 자신이 지정한 경로만 정리한다
    private void ClearOwnedRoute()
    {
        if (navigation && MissionId != 0 && navigation.HasDestination && navigation.DestinationLane == destinationLane && navigation.DestinationDistance == destinationDistance)
            navigation.ClearDestination();
    }

    // 다음 미션을 시작하지 않고 이송 데이터만 대기 상태로 정리한다
    private void ResetTransfer()
    {
        ClearOwnedRoute();
        Destination = null;
        destinationLane = null;
        destinationDistance = 0f;
        layout = null;
        MissionId = 0;
        ArrivalECG = 0f;
        ArrivalState = default;
        Status = TransferStatus.Idle;
    }
}
