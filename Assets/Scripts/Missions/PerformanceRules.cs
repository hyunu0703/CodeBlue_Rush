using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>병원 인계 시의 환자 상태를 나타내며 실적 배점에는 별도 가중치를 주지 않는다.</summary>
public enum PatientState
{
    Stable,
    AtRisk,
    Critical,
    Dead
}

/// <summary>첫 접촉 순간의 충격 속도로 구분한 차량 충돌 등급이다.</summary>
public enum CollisionLevel
{
    Low,
    High
}

/// <summary>씬과 차량 수명에 의존하지 않고 실적, 표시, 충돌 등급과 누적 목표를 계산한다.</summary>
public static class PerformanceRules
{
    public const int MaximumDailyScore = 130; // HP 100%와 생존 무사고 보너스를 합한 이론상 하루 최대 실적이다.
    public const int SafeBonus = 30;          // 생존하고 차량·시민 충돌이 없을 때만 지급하는 실적이다.
    public const int LowPenalty = 1;          // 저속 충돌 1회에 적용할 실적 감점이다.
    public const int HighPenalty = 3;         // 고속 충돌 1회에 적용할 실적 감점이다.
    public const int CitizenPenalty = 5;      // 같은 시민 한 명과의 충돌에 미션당 한 번 적용하는 감점이다.
    private const double HighSpeedKmh = 60d;  // 이 값 이상인 법선 방향 충격을 고속으로 판정한다.

    // 인계 시 실제 HP와 확정 충돌 횟수로 계산하고, 하한 적용 뒤 마지막에만 사사오입한다.
    public static int CalculateScore(double healthPercent, int lowCollisions, int highCollisions, int citizenCollisions = 0)
    {
        ValidateHealth(healthPercent);
        if (lowCollisions < 0) throw new ArgumentOutOfRangeException(nameof(lowCollisions));
        if (highCollisions < 0) throw new ArgumentOutOfRangeException(nameof(highCollisions));
        if (citizenCollisions < 0) throw new ArgumentOutOfRangeException(nameof(citizenCollisions));
        if (healthPercent == 0d) return 0;

        var bonus = lowCollisions == 0 && highCollisions == 0 && citizenCollisions == 0 ? SafeBonus : 0;
        var score = Math.Max(0d, healthPercent + bonus
            - (double)lowCollisions * LowPenalty - (double)highCollisions * HighPenalty
            - (double)citizenCollisions * CitizenPenalty);
        // 기본 반올림의 짝수 쪽 처리 대신, 정확히 0.5인 경우에도 항상 올린다.
        return (int)Math.Round(score, 0, MidpointRounding.AwayFromZero);
    }

    // 표시용 내림이나 반올림 이전의 실제 HP로 상태 경계를 판정한다.
    public static PatientState GetPatientState(double healthPercent)
    {
        ValidateHealth(healthPercent);
        if (healthPercent == 0d) return PatientState.Dead;
        if (healthPercent < 30d) return PatientState.Critical;
        return healthPercent < 70d ? PatientState.AtRisk : PatientState.Stable;
    }

    // HP 원본을 바꾸지 않고 내림한 백분율을 만들며, 극소량 생존과 사망을 구별한다.
    public static string FormatHealth(double healthPercent)
    {
        ValidateHealth(healthPercent);
        if (healthPercent == 0d) return "0.0% · 사망";
        if (healthPercent < 0.1d) return "0.1% 미만";
        var displayed = Math.Floor(healthPercent * 10d) / 10d;
        return displayed.ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }

    // 실제 충격 속도를 그대로 비교하여 정확히 60km/h이면 고속으로 판정한다.
    public static CollisionLevel GetCollisionLevel(double impactSpeedKmh)
    {
        ValidateFinite(impactSpeedKmh, nameof(impactSpeedKmh));
        if (impactSpeedKmh < 0d) throw new ArgumentOutOfRangeException(nameof(impactSpeedKmh));
        return impactSpeedKmh < HighSpeedKmh ? CollisionLevel.Low : CollisionLevel.High;
    }

    // 첫 접촉의 상대 속도와 단위 법선들에서 최대 법선 충격을 구해 1유닛=6m로 환산한다.
    public static double CalculateImpactSpeedKmh(Vector2 relativeVelocity, IReadOnlyList<Vector2> contactNormals)
    {
        ValidateFinite(relativeVelocity.x, nameof(relativeVelocity));
        ValidateFinite(relativeVelocity.y, nameof(relativeVelocity));
        if (contactNormals == null) throw new ArgumentNullException(nameof(contactNormals));
        if (contactNormals.Count == 0) throw new ArgumentException("접촉 법선이 필요합니다.", nameof(contactNormals));

        var maximum = 0d;
        for (var index = 0; index < contactNormals.Count; index++)
        {
            var normal = contactNormals[index];
            ValidateFinite(normal.x, nameof(contactNormals));
            ValidateFinite(normal.y, nameof(contactNormals));
            var lengthSquared = (double)normal.x * normal.x + (double)normal.y * normal.y;
            if (Math.Abs(lengthSquared - 1d) > 0.001d)
                throw new ArgumentException("접촉 법선은 단위 벡터여야 합니다.", nameof(contactNormals));
            var speed = Math.Abs((double)relativeVelocity.x * normal.x + (double)relativeVelocity.y * normal.y);
            maximum = Math.Max(maximum, speed);
        }
        return maximum * 6d * 3.6d;
    }

    // 1부터 시작하는 평가 구간 번호로 기준 요구량의 누적 합계를 계산한다.
    public static long GetCumulativeTarget(int evaluationPeriod)
    {
        if (evaluationPeriod < 1) throw new ArgumentOutOfRangeException(nameof(evaluationPeriod));
        return checked(200L * evaluationPeriod + 10L * evaluationPeriod * (evaluationPeriod - 1L));
    }

    // 잘못된 입력을 사망이나 정상 실적으로 조용히 바꾸지 않도록 HP 계약을 검사한다.
    private static void ValidateHealth(double healthPercent)
    {
        ValidateFinite(healthPercent, nameof(healthPercent));
        if (healthPercent < 0d || healthPercent > 100d)
            throw new ArgumentOutOfRangeException(nameof(healthPercent));
    }

    // 비정상 물리·HP 값이 계산과 판정에 전파되지 않도록 입력 경계에서 거부한다.
    private static void ValidateFinite(double value, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(parameterName, "유한한 값이 필요합니다.");
    }
}
