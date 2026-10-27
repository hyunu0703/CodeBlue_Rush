using System;

/// <summary>미션 진행 허용, 정기 평가 대기와 해고를 구분하는 플레이 진행 상태다.</summary>
public enum ProgressStage
{
    Ready,
    EvaluationPending,
    Fired
}

/// <summary>일차와 누적 실적을 보관하고, 완료 요청마다 원본을 보존한 다음 상태 후보를 만든다.</summary>
public sealed class GameProgress
{
    private readonly int currentDay;        // 다음에 수행할 일차이며, 하루 완료 후에만 증가한다.
    private readonly long cumulativeScore;  // 완료한 날의 확정 정수 실적 합계로, 평가 통과 시에도 유지한다.
    private readonly int evaluationPeriod;  // 아직 완료 처리하지 않은 평가의 1부터 시작하는 구간 번호다.
    private readonly ProgressStage stage;   // 일반 진행, 평가 대기 또는 해고 상태를 보관한다.

    public int CurrentDay => currentDay;
    public long CumulativeScore => cumulativeScore;
    public int EvaluationPeriod => evaluationPeriod;
    public ProgressStage Stage => stage;
    public long CumulativeTarget => PerformanceRules.GetCumulativeTarget(evaluationPeriod);
    public long RequiredScore => Math.Max(0L, CumulativeTarget - cumulativeScore);
    public bool CanPassEvaluation => stage == ProgressStage.EvaluationPending && cumulativeScore >= CumulativeTarget;

    // 첫 목표 200점의 1일차로 시작하며 기존 게임이나 씬의 상태를 변경하지 않는다.
    public GameProgress() : this(1, 0L, 1, ProgressStage.Ready)
    {
    }

    // 유효한 진행 전환 안에서만 다음 후보를 생성해 외부의 임의 상태 조합을 막는다.
    private GameProgress(int currentDay, long cumulativeScore, int evaluationPeriod, ProgressStage stage)
    {
        this.currentDay = currentDay;
        this.cumulativeScore = cumulativeScore;
        this.evaluationPeriod = evaluationPeriod;
        this.stage = stage;
    }

    // 저장된 일차·실적·평가 이력이 서로 모순되지 않을 때만 진행 상태를 복원한다.
    public static bool TryRestore(int currentDay, long cumulativeScore, int evaluationPeriod,
        ProgressStage stage, out GameProgress restored)
    {
        restored = null;
        if (currentDay < 1 || evaluationPeriod < 1 || cumulativeScore < 0L
            || cumulativeScore > (currentDay - 1L) * PerformanceRules.MaximumDailyScore) return false;
        var completedDays = currentDay - 1;
        if (stage == ProgressStage.Ready)
        {
            if (evaluationPeriod != completedDays / 5 + 1) return false;
        }
        else if (stage == ProgressStage.EvaluationPending || stage == ProgressStage.Fired)
        {
            if (completedDays == 0 || completedDays % 5 != 0 || evaluationPeriod != completedDays / 5) return false;
        }
        else return false;

        // 이전 평가를 통과한 이력과 이후 실적이 감소하지 않는 누적 규칙을 함께 확인한다.
        if (evaluationPeriod > 1 && cumulativeScore < PerformanceRules.GetCumulativeTarget(evaluationPeriod - 1))
            return false;
        if (stage == ProgressStage.Fired && cumulativeScore >= PerformanceRules.GetCumulativeTarget(evaluationPeriod))
            return false;
        restored = new GameProgress(currentDay, cumulativeScore, evaluationPeriod, stage);
        return true;
    }

    // 현재 일차의 확정 실적을 한 번 합산한 후보를 만들고, 매 5일 완료 시 평가를 대기시킨다.
    public bool TryCompleteDay(int completedDay, int dailyScore, out GameProgress next)
    {
        if (dailyScore < 0 || dailyScore > PerformanceRules.MaximumDailyScore)
            throw new ArgumentOutOfRangeException(nameof(dailyScore));
        next = this;
        if (stage != ProgressStage.Ready || completedDay != currentDay) return false;

        var followingDay = checked(currentDay + 1);
        var total = checked(cumulativeScore + dailyScore);
        var followingStage = currentDay % 5 == 0 ? ProgressStage.EvaluationPending : ProgressStage.Ready;
        // 이 단계는 후보만 반환한다. 저장과 실제 게임 반영은 이후 연결 단계의 호출부가 맡는다.
        next = new GameProgress(followingDay, total, evaluationPeriod, followingStage);
        return true;
    }

    // 대기 중인 평가를 완료한 후보를 만든다. 통과는 계속하기 입력 시, 미달은 해고 처리 시 호출한다.
    public bool TryCompleteEvaluation(int expectedPeriod, out GameProgress next)
    {
        next = this;
        if (stage != ProgressStage.EvaluationPending || expectedPeriod != evaluationPeriod) return false;
        if (!CanPassEvaluation)
        {
            next = new GameProgress(currentDay, cumulativeScore, evaluationPeriod, ProgressStage.Fired);
            return true;
        }

        var followingPeriod = checked(evaluationPeriod + 1);
        // 목표는 인상해도 초과 실적을 포함한 누적 실적에서 어떤 값도 차감하지 않는다.
        next = new GameProgress(currentDay, cumulativeScore, followingPeriod, ProgressStage.Ready);
        return true;
    }

    // 해고 화면의 다시 시작에 사용할 초기 상태 후보를 만들며 일반 진행 중 초기화는 허용하지 않는다.
    public bool TryRestart(out GameProgress next)
    {
        next = this;
        if (stage != ProgressStage.Fired) return false;
        next = new GameProgress();
        return true;
    }
}
