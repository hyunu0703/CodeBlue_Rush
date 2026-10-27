using System;

/// <summary>병원 인계 시의 기록과 다음 진행 후보를 고정하여 표시·저장 재시도에서 재계산하지 않는다.</summary>
public sealed class MissionResult
{
    public GameProgress OriginalProgress { get; }
    public GameProgress NextProgress { get; }
    public double HealthPercent { get; }
    public int LowCollisionCount { get; }
    public int HighCollisionCount { get; }
    public int DailyScore { get; }

    // 표시용 HP로 가공하기 전에 실제 값으로 배점과 하루 완료 후보를 한 번 확정한다.
    public MissionResult(GameProgress progress, double healthPercent, int lowCollisions, int highCollisions)
    {
        if (progress == null) throw new ArgumentNullException(nameof(progress));
        DailyScore = PerformanceRules.CalculateScore(healthPercent, lowCollisions, highCollisions);
        if (!progress.TryCompleteDay(progress.CurrentDay, DailyScore, out var next))
            throw new InvalidOperationException("일반 진행 중인 일차만 결과를 만들 수 있습니다.");
        OriginalProgress = progress;
        NextProgress = next;
        HealthPercent = healthPercent;
        LowCollisionCount = lowCollisions;
        HighCollisionCount = highCollisions;
    }

    // 결과에는 이번 날을 포함한 누적 후보를 보여주되 실제 진행 객체를 수정하지 않는다.
    public string FormatSummary()
    {
        var state = PerformanceRules.GetPatientState(HealthPercent);
        var patient = state == PatientState.Stable ? "안정" : state == PatientState.AtRisk ? "위험"
            : state == PatientState.Critical ? "위독" : "사망";
        var collision = LowCollisionCount > 0 || HighCollisionCount > 0 ? "있음" : "없음";
        return $"{OriginalProgress.CurrentDay}일차 후송 결과\n\n"
            + $"환자 잔여 HP: {PerformanceRules.FormatHealth(HealthPercent)}\n환자 상태: {patient}\n\n"
            + $"차량 충돌: {collision}\n저속 충돌: {LowCollisionCount}회\n고속 충돌: {HighCollisionCount}회\n\n"
            + $"당일 실적: {DailyScore}점\n누적 실적: {NextProgress.CumulativeScore}점";
    }
}
