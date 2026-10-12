using System;
using UnityEngine;

// 표시 장치와 독립적인 차량 신호 상태와 우회전 진입 규칙이다.
public enum RoadTrafficLight { Red, Yellow, Green }
public enum RoadTrafficPermission { Stop, Proceed, StopThenYield, Yield }

/// <summary>교차로별 신호를 소유한다. 시간은 게임 시뮬레이션 초이며 보행자 표시나 영구 에셋을 변경하지 않는다.</summary>
public sealed class RoadTrafficSignals
{
    // 각 교차로의 누적 시간, 전용 우회전 신호 정책, 진입/진출 횡단보도 점유 상태다.
    private readonly double[] clocks;
    private readonly bool[] dedicatedRight;
    private readonly bool[,] crossings;
    private readonly RoadLaneNetwork network;
    public const float PhaseSeconds = 5f;

    /// <summary>새 맵의 신호를 초기화한다. 교차로마다 시작 위상을 달리하여 맵 전체 동시 정지를 피한다.</summary>
    public RoadTrafficSignals(RoadLaneNetwork network)
    {
        this.network = network ?? throw new ArgumentNullException(nameof(network));
        clocks = new double[network.Roads.Count];
        dedicatedRight = new bool[clocks.Length];
        crossings = new bool[clocks.Length, 4];
        for (var road = 0; road < clocks.Length; road++)
        {
            clocks[road] = road % 8 * PhaseSeconds;
        }
    }

    /// <summary>유한한 0 이상의 고정 스텝으로만 신호를 진행한다. 일시정지는 호출자가 진행을 중단한다.</summary>
    public void Tick(float seconds)
    {
        if (!RoadLaneMath.IsFinite(seconds) || seconds < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }
        for (var road = 0; road < clocks.Length; road++)
        {
            clocks[road] = (clocks[road] + seconds) % (8 * PhaseSeconds);
        }
    }

    /// <summary>시각화/검사용 신호다. 남북 직진→황색→남북 좌회전→황색→동서 직진→황색→동서 좌회전→황색 순이다.</summary>
    public RoadTrafficLight GetLight(int road, RoadDirection entry, RoadLaneManeuver maneuver)
    {
        ValidateRoad(road);
        // 진입 가장자리 기준 축과 보호 좌회전 현시를 구분한다. 황색 직진/좌회전에는 새 진입을 허용하지 않는다.
        var phase = (int)((clocks[road] + 0.00001d) / PhaseSeconds) % 8;
        var northSouth = entry == RoadDirection.North || entry == RoadDirection.South;
        var activeAxis = phase < 4;
        var isLeftPhase = phase % 4 >= 2;
        if (northSouth != activeAxis || (maneuver == RoadLaneManeuver.Left) != isLeftPhase)
        {
            return RoadTrafficLight.Red;
        }
        return phase % 2 == 0 ? RoadTrafficLight.Green : RoadTrafficLight.Yellow;
    }

    /// <summary>한국 신호 의미를 진입 정책으로 변환한다. 비보호 좌회전은 생성하지 않으며 우회전의 실제 양보는 AI가 판단한다.</summary>
    public RoadTrafficPermission GetPermission(RoadLanePath movement)
    {
        if (!network.IsValid || movement == null || movement.Kind != RoadLanePathKind.Intersection)
        {
            return RoadTrafficPermission.Stop;
        }
        // 진입과 회전 후 횡단보도 양쪽 모두 보행자 보호 조건을 적용한다.
        var road = movement.RoadOrdinal;
        if (crossings[road, (int)movement.Entry] || crossings[road, (int)movement.Exit])
        {
            return RoadTrafficPermission.Stop;
        }
        var light = GetLight(road, movement.Entry, movement.Maneuver);
        if (movement.Maneuver != RoadLaneManeuver.Right || dedicatedRight[road])
        {
            return light == RoadTrafficLight.Green ? RoadTrafficPermission.Proceed : RoadTrafficPermission.Stop;
        }
        // 일반 원형 황색도 우회전은 보행자/다른 교통에 양보하여 진행할 수 있다. 전용 화살표 황색은 위에서 정지 처리한다.
        return light == RoadTrafficLight.Red ? RoadTrafficPermission.StopThenYield : RoadTrafficPermission.Yield;
    }

    /// <summary>지정한 교차로에 코드상의 전용 우회전 신호 정책을 적용한다. 기본값은 일반 신호에서 일시정지 후 양보다.</summary>
    public void SetDedicatedRightSignal(int road, bool enabled)
    {
        ValidateRoad(road);
        dedicatedRight[road] = enabled;
    }

    /// <summary>향후 보행자 시스템이 횡단 중이거나 횡단하려는 보행자가 있는 가장자리를 차단/해제한다.</summary>
    public void SetCrossingBlocked(int road, RoadDirection side, bool blocked)
    {
        ValidateRoad(road);
        if ((int)side < 0 || (int)side > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }
        crossings[road, (int)side] = blocked;
    }

    /// <summary>외부 호출의 도로 식별자를 검증한다.</summary>
    private void ValidateRoad(int road)
    {
        if (road < 0 || road >= clocks.Length || !network.Roads[road].Layout.IsIntersection)
        {
            throw new ArgumentOutOfRangeException(nameof(road));
        }
    }
}
