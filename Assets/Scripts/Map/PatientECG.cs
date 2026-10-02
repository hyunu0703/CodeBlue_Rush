using System;
using System.Collections;
using UnityEngine;

/// <summary>탑승한 환자의 단일 ECG 값과 자연 감소 및 사망 알림을 소유한다</summary>
[DisallowMultipleComponent]
public sealed class PatientECG : MonoBehaviour
{
    public enum ECGState { Green, Yellow, Red, Flatline }

    [SerializeField] private PatientReport report;
    [Min(0.01f)] public float survivalTime = 180f;
    private PatientReport subscribedReport;
    private Coroutine decay;
    private int missionId;
    private double value = 100d;
    private bool stopped;

    public float Value => (float)value;
    public bool HasPatient { get; private set; }
    public bool IsDecaying => decay != null;
    public ECGState State => Value >= 60f ? ECGState.Green : Value >= 10f ? ECGState.Yellow : Value > 0f ? ECGState.Red : ECGState.Flatline;
    public event Action Changed;
    public event Action PatientDied;

    // 미션 참조를 교체하고 현재 탑승 상태를 동기화한다
    public void Configure(PatientReport mission)
    {
        if (report != mission)
            ResetPatient();
        Unsubscribe();
        report = mission;
        if (isActiveAndEnabled)
            Bind();
    }

    // 재활성화 시 같은 미션의 값과 명시적 중지 상태를 유지한다
    private void OnEnable()
    {
        Bind();
    }

    // 비활성 시간에는 감소와 구독을 중지한다
    private void OnDisable()
    {
        Unsubscribe();
        PauseDecay();
    }

    // 이벤트를 한 번만 구독하고 늦게 연결된 탑승 환자도 반영한다
    private void Bind()
    {
        Unsubscribe();
        subscribedReport = report;
        if (subscribedReport)
        {
            subscribedReport.PatientPickedUp += HandlePickup;
            subscribedReport.ReportChanged += HandleReport;
        }
        HandleReport();
        HandlePickup();
    }

    // 실제 구독한 미션에서 이벤트를 해제한다
    private void Unsubscribe()
    {
        if (subscribedReport)
        {
            subscribedReport.PatientPickedUp -= HandlePickup;
            subscribedReport.ReportChanged -= HandleReport;
        }
        subscribedReport = null;
    }

    // 미션 취소와 새 미션에서는 이전 환자 상태를 지운다
    private void HandleReport()
    {
        if (!report || !report.IsActive || (HasPatient && missionId != report.MissionId))
            ResetPatient();
    }

    // 같은 미션의 중복 픽업은 초기화하거나 감소를 추가하지 않는다
    private void HandlePickup()
    {
        if (!isActiveAndEnabled || !report || !report.IsPatientOnBoard)
            return;
        if (!HasPatient || missionId != report.MissionId)
        {
            PauseDecay();
            missionId = report.MissionId;
            value = 100d;
            stopped = false;
            HasPatient = true;
            Changed?.Invoke();
        }
        if (HasPatient && !stopped && value > 0d && decay == null)
            decay = StartCoroutine(Decay());
    }

    // 유효한 탑승 환자만 게임 시간 기준으로 일정하게 감소한다
    private IEnumerator Decay()
    {
        while (true)
        {
            yield return null;
            if (!report || !report.IsPatientOnBoard || report.MissionId != missionId)
            {
                decay = null;
                ResetPatient();
                yield break;
            }
            float duration = float.IsFinite(survivalTime) && survivalTime > 0f ? survivalTime : 180f;
            SetValue(value - 100d * Time.deltaTime / duration);
            if (stopped || !HasPatient || value <= 0d)
            {
                decay = null;
                yield break;
            }
        }
    }

    // 병원 등 외부 요청으로 현재 미션의 자연 감소를 영구 중지한다
    public void StopDecay()
    {
        stopped = true;
        PauseDecay();
    }

    // 명시적인 피해만 반영하고 잘못된 수치와 탑승 전 요청은 무시한다
    public void TakeDamage(float amount)
    {
        if (!isActiveAndEnabled || !HasPatient || !report || !report.IsPatientOnBoard || report.MissionId != missionId || value <= 0d || !float.IsFinite(amount) || amount <= 0f)
            return;
        SetValue(value - amount);
    }

    // 값의 하한을 고정하고 최초 사망을 한 번만 알린다
    private void SetValue(double next)
    {
        bool died = value > 0d && next <= 0d;
        value = Math.Max(0d, Math.Min(100d, next));
        if (died)
            StopDecay();
        int completedMission = missionId;
        Changed?.Invoke();
        if (died && HasPatient && missionId == completedMission && value == 0d)
            PatientDied?.Invoke();
    }

    // 실행 중인 감소 루틴만 해제한다
    private void PauseDecay()
    {
        if (decay != null)
            StopCoroutine(decay);
        decay = null;
    }

    // 다음 환자를 위해 ECG를 대기 상태로 되돌린다
    private void ResetPatient()
    {
        PauseDecay();
        HasPatient = false;
        missionId = 0;
        value = 100d;
        stopped = false;
        Changed?.Invoke();
    }
}
