#if UNITY_EDITOR
using System;
using NUnit.Framework;

/// <summary>인계 결과의 점수·표시와 변경되지 않는 진행 후보를 씬 없이 검증한다.</summary>
public sealed class MissionResultTests
{
    // 표시값으로 다시 계산하지 않고 실제 HP의 마지막 1회 반올림을 유지한다.
    [TestCase(72.46d, 0, 0, 102)]
    [TestCase(72.50d, 0, 0, 103)]
    [TestCase(72.46d, 3, 2, 46)]
    [TestCase(100d, 0, 0, 130)]
    [TestCase(1d, 2, 0, 0)]
    [TestCase(0d, 0, 0, 0)]
    [TestCase(0d, 2, 1, 0)]
    public void Snapshot_CalculatesOnceAndLeavesOriginalUnchanged(double health, int low, int high, int score)
    {
        var original = new GameProgress();
        var result = new MissionResult(original, health, low, high);
        Assert.That(result.DailyScore, Is.EqualTo(score));
        Assert.That(result.HealthPercent, Is.EqualTo(health));
        Assert.That(result.LowCollisionCount, Is.EqualTo(low));
        Assert.That(result.HighCollisionCount, Is.EqualTo(high));
        Assert.That(result.OriginalProgress, Is.SameAs(original));
        Assert.That(original.CurrentDay, Is.EqualTo(1));
        Assert.That(original.CumulativeScore, Is.Zero);
        Assert.That(result.NextProgress.CurrentDay, Is.EqualTo(2));
        Assert.That(result.NextProgress.CumulativeScore, Is.EqualTo(score));
    }

    // 내림한 HP 표시와 실제 값의 상태 판정을 구분하고 사망 중복 퍼센트 표시를 막는다.
    [TestCase(69.99d, "69.9%", "위험")]
    [TestCase(29.99d, "29.9%", "위독")]
    [TestCase(70d, "70.0%", "안정")]
    [TestCase(30d, "30.0%", "위험")]
    [TestCase(0.09d, "0.1% 미만", "위독")]
    [TestCase(0d, "0.0% · 사망", "사망")]
    public void Summary_UsesFixedHealthAndPatientState(double health, string displayed, string state)
    {
        var result = new MissionResult(new GameProgress(), health, 2, 1);
        var text = result.FormatSummary();
        Assert.That(text, Does.Contain("1일차 후송 결과"));
        Assert.That(text, Does.Contain("환자 잔여 HP: " + displayed));
        Assert.That(text, Does.Contain("환자 상태: " + state));
        Assert.That(text, Does.Contain("차량 충돌: 있음"));
        Assert.That(text, Does.Contain("저속 충돌: 2회"));
        Assert.That(text, Does.Contain("고속 충돌: 1회"));
        Assert.That(text, Does.Not.Contain("%%"));
        Assert.That(result.FormatSummary(), Is.EqualTo(text));
    }

    // 다섯째 날의 결과 후보에는 다음 일차와 평가 대기를 함께 담되 평가 자체는 하지 않는다.
    [Test]
    public void FifthDay_StagesCumulativeEvaluationWithoutApplyingIt()
    {
        Assert.That(GameProgress.TryRestore(5, 170, 1, ProgressStage.Ready, out var original), Is.True);
        var result = new MissionResult(original, 72.5d, 0, 0);
        Assert.That(original.CurrentDay, Is.EqualTo(5));
        Assert.That(original.CumulativeScore, Is.EqualTo(170));
        Assert.That(result.NextProgress.CurrentDay, Is.EqualTo(6));
        Assert.That(result.NextProgress.CumulativeScore, Is.EqualTo(273));
        Assert.That(result.NextProgress.EvaluationPeriod, Is.EqualTo(1));
        Assert.That(result.NextProgress.Stage, Is.EqualTo(ProgressStage.EvaluationPending));
        Assert.That(result.FormatSummary(), Does.Contain("누적 실적: 273점"));
    }

    // 평가 대기 상태를 다음 미션 결과로 잘못 확정하지 않는다.
    [Test]
    public void NonReadyProgress_CannotCreateAnotherDayResult()
    {
        GameProgress.TryRestore(6, 200, 1, ProgressStage.EvaluationPending, out var pending);
        Assert.Throws<InvalidOperationException>(() => new MissionResult(pending, 100d, 0, 0));
    }
}
#endif
