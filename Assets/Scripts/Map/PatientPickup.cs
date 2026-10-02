using System.Collections;
using UnityEngine;

/// <summary>현재 신고 현장을 배치하고 실제 정차 후 환자와 구급대원을 뒤쪽 탑승점으로 이동한다</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CircleCollider2D))]
public sealed class PatientPickup : MonoBehaviour
{
    [SerializeField] private PatientReport report;
    [SerializeField] private Rigidbody2D ambulance;
    [SerializeField] private Transform rearBoardingPoint;
    [SerializeField] private Transform patient;
    [SerializeField] private Transform paramedic;
    [SerializeField] private Vector2 patientOffset = new Vector2(-0.45f, 0f);
    [SerializeField] private Vector2 paramedicOffset = new Vector2(0.45f, 0.4f);
    [SerializeField, Min(0f)] private float stoppedSpeed = 0.15f;
    [SerializeField, Min(0f)] private float stoppedAngularSpeed = 5f;
    [SerializeField, Min(0.05f)] private float settleTime = 0.35f;
    [SerializeField, Min(0.1f)] private float walkSpeed = 1.5f;
    [SerializeField, Min(0.01f)] private float arrivalDistance = 0.05f;

    private CircleCollider2D area;
    private Coroutine routine;
    private PatientReport subscribedReport;
    private PatientReport.SpawnPoint site;
    private int missionId;
    private float stoppedTime;
    private static readonly WaitForFixedUpdate PhysicsTick = new WaitForFixedUpdate();

    public bool IsBoarding { get; private set; }
    public bool IsPaused { get; private set; }
    public string Error { get; private set; }

    // 픽업 영역 참조를 한 번 가져온다
    private void Awake()
    {
        area = GetComponent<CircleCollider2D>();
        area.isTrigger = true;
    }

    // 현재 미션을 구독하고 이미 활성화된 현장도 복원한다
    private void OnEnable()
    {
        Subscribe();
        RefreshSite();
    }

    // 중복 이동과 이전 현장 객체의 노출을 방지한다
    private void OnDisable()
    {
        Unsubscribe();
        ResetSite();
    }

    // 직접 참조로 미션과 실제 구급차 및 탑승점을 연결한다
    public void Configure(PatientReport mission, Rigidbody2D vehicle, Transform rear)
    {
        Unsubscribe();
        ResetSite();
        report = mission;
        ambulance = vehicle;
        rearBoardingPoint = rear;
        Subscribe();
        RefreshSite();
    }

    // 보고 상태 변경을 중복 없이 구독한다
    private void Subscribe()
    {
        subscribedReport = report;
        if (subscribedReport && isActiveAndEnabled)
        {
            subscribedReport.ReportChanged -= RefreshSite;
            subscribedReport.ReportChanged += RefreshSite;
        }
    }

    // 구독에 사용한 원래 보고 참조에서 해제한다
    private void Unsubscribe()
    {
        if (subscribedReport)
            subscribedReport.ReportChanged -= RefreshSite;
        subscribedReport = null;
    }

    // 신고 SpawnPoint를 기준으로 동일한 두 현장 객체와 영역을 재사용한다
    public void RefreshSite()
    {
        ResetSite();
        Error = null;
        if (!Application.isPlaying || !isActiveAndEnabled || !report || !report.IsActive || report.IsPatientOnBoard)
            return;
        if (!ValidReferences())
        {
            Error = "환자, 구급대원, 구급차, RearBoardingPoint와 현장 영역 연결을 확인하세요.";
            return;
        }
        site = report.Patient;
        missionId = report.MissionId;
        if (!site.TryGetPose(out Vector3 position, out Vector3 direction))
        {
            Error = "현재 미션의 환자 위치가 유효하지 않습니다.";
            return;
        }
        transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, direction));
        patient.localPosition = patientOffset;
        paramedic.localPosition = paramedicOffset;
        patient.gameObject.SetActive(true);
        paramedic.gameObject.SetActive(true);
        area.enabled = true;
        routine = StartCoroutine(RunPickup());
    }

    // 참조 누락과 서로 겹친 객체 및 잘못된 탑승점 소유권을 검사한다
    private bool ValidReferences()
    {
        return area && area.isTrigger && ambulance && ambulance.simulated && rearBoardingPoint && rearBoardingPoint != ambulance.transform && rearBoardingPoint.IsChildOf(ambulance.transform) && rearBoardingPoint.gameObject.activeInHierarchy && patient && paramedic && patient != paramedic && patient.parent == transform && paramedic.parent == transform;
    }

    // 현장과 요청 미션이 여전히 동일한지 직접 참조로 검사한다
    private bool CurrentSite()
    {
        return report && report.isActiveAndEnabled && report.IsActive && !report.IsPatientOnBoard && missionId == report.MissionId && report.Patient.Lane == site.Lane && report.Patient.Map == site.Map && report.Patient.Distance == site.Distance && site.TryGetPose(out _, out _);
    }

    // 실제 차량 속도와 이 현장의 영역을 사용하여 정차를 판정한다
    private bool CanBoard()
    {
        if (!CurrentSite() || !ValidReferences() || !ambulance.gameObject.activeInHierarchy || !area.enabled || !area.gameObject.activeInHierarchy)
            return false;
        site.TryGetPose(out Vector3 position, out _);
        return ((Vector2)transform.position - (Vector2)position).sqrMagnitude < 0.0025f && area.OverlapPoint(ambulance.position) && ambulance.linearVelocity.sqrMagnitude <= stoppedSpeed * stoppedSpeed && Mathf.Abs(ambulance.angularVelocity) <= stoppedAngularSpeed;
    }

    // 영역 진입뿐 아니라 연속 정차 시간이 충족된 경우에만 한 번 시작한다
    public bool TryStartPickup()
    {
        if (!isActiveAndEnabled || routine == null || IsBoarding || stoppedTime < settleTime || !CanBoard() || !patient.gameObject.activeInHierarchy || !paramedic.gameObject.activeInHierarchy)
            return false;
        IsBoarding = true;
        IsPaused = false;
        return true;
    }

    // 활성 현장에서만 일정한 물리 주기로 정차와 두 객체의 짧은 이동을 처리한다
    private IEnumerator RunPickup()
    {
        while (CurrentSite())
        {
            yield return PhysicsTick;
            if (!CurrentSite())
                break;
            if (!ValidReferences() || !patient.gameObject.activeInHierarchy || !paramedic.gameObject.activeInHierarchy)
            {
                Error = "픽업 중 필수 객체가 사라지거나 비활성화되었습니다. 설정 복구 후 RefreshSite를 호출하세요.";
                break;
            }
            if (!CanBoard())
            {
                stoppedTime = 0f;
                IsPaused = IsBoarding;
                continue;
            }
            stoppedTime += Time.fixedDeltaTime;
            if (stoppedTime < settleTime)
                continue;
            if (!IsBoarding)
                TryStartPickup();
            if (!IsBoarding)
                continue;
            IsPaused = false;
            Vector3 target = rearBoardingPoint.position;
            Vector3 medicTarget = target + rearBoardingPoint.right * 0.3f;
            float step = walkSpeed * Time.fixedDeltaTime;
            patient.position = Vector3.MoveTowards(patient.position, target, step);
            paramedic.position = Vector3.MoveTowards(paramedic.position, medicTarget, step);
            if ((patient.position - target).sqrMagnitude > arrivalDistance * arrivalDistance || (paramedic.position - medicTarget).sqrMagnitude > arrivalDistance * arrivalDistance)
                continue;

            int completedMission = missionId;
            routine = null;
            ResetSite();
            report.TryCompletePickup(completedMission);
            yield break;
        }
        routine = null;
        ResetSite();
    }

    // 취소와 새 맵 및 탑승 완료에서 이동과 이전 위치 참조를 정리한다
    private void ResetSite()
    {
        if (routine != null)
            StopCoroutine(routine);
        routine = null;
        IsBoarding = false;
        IsPaused = false;
        stoppedTime = 0f;
        site = default;
        missionId = 0;
        if (area)
            area.enabled = false;
        if (patient)
            patient.gameObject.SetActive(false);
        if (paramedic)
            paramedic.gameObject.SetActive(false);
    }
}
