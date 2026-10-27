using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>인도 배회, 보행 신호 대기와 횡단보도 공동 점유를 처리하는 시민 시뮬레이션이다.</summary>
public sealed class RoadCitizenSimulation
{
    /// <summary>한 시민의 현재 양방향 경로와 횡단 시작 여부다.</summary>
    public sealed class Citizen
    {
        public int EdgeId { get; internal set; } // 현재 이동하거나 진입을 기다리는 구간 번호다.
        public int From { get; internal set; } // 현재 구간에서 출발한 끝점 번호다.
        public float Distance { get; internal set; } // 출발점에서 이동한 맵 로컬 거리다.
        public float Speed { get; internal set; } // 시민별 보행 속도이며 맵 로컬 유닛/초다.
        public bool Crossing { get; internal set; } // 신호가 바뀌어도 유지할 횡단 허가다.
        public bool IsFallen { get; internal set; } // 충돌 이후에는 보행을 재개하지 않고 실제 물리 위치를 따른다.
        public Vector2 Position { get; internal set; } // 표시와 주변 회수에 사용하는 맵 로컬 위치다.
    }

    private readonly List<Citizen> citizens = new List<Citizen>(); // 현재 활성 시민이며 표시 객체의 소유권은 컨트롤러에 있다.
    private readonly System.Random random;                         // 교통 차량의 난수 흐름과 독립적인 시민 난수다.
    private RoadLaneNetwork roads;                                 // 차량 예약 식별자와 같은 차로 스냅숏이다.
    private RoadTrafficSignals signals;                            // 현재 맵의 시민 전용 점유 채널이다.
    public RoadSidewalkNetwork Sidewalks { get; private set; } // 현재 도로 전체의 연결된 인도 그래프다.
    public IReadOnlyList<Citizen> Citizens => citizens;

    // 미션 시드로 보행 경로와 난수 상태를 초기화한다.
    public RoadCitizenSimulation(RoadLaneNetwork roads, RoadTrafficSignals signals, int seed)
    {
        random = new System.Random(seed ^ 0x41c17);
        SetNetwork(roads, signals);
    }

    // 같은 미션의 후송 확장에서 기존 간선 번호와 시민 위치를 유지한다.
    public void SetNetwork(RoadLaneNetwork next, RoadTrafficSignals nextSignals)
    {
        ClearOccupancy();
        roads = next;
        signals = nextSignals;
        Sidewalks = new RoadSidewalkNetwork(next);
        PublishOccupancy();
    }

    // 테스트 및 주변 생성기가 지정한 인도 위에만 시민을 추가한다. 횡단 중 생성은 허용하지 않는다.
    public Citizen Spawn(int edgeId, float fraction, float speed)
    {
        var edge = Sidewalks.Edges[edgeId];
        if (edge.IsCrossing || !RoadLaneMath.IsFinite(speed) || speed <= 0f
            || !RoadLaneMath.IsFinite(fraction) || fraction < 0f || fraction > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(edgeId), "시민은 유효한 속도로 인도 위에 생성해야 합니다.");
        }
        var from = random.Next(2) == 0 ? edge.A : edge.B;
        var to = from == edge.A ? edge.B : edge.A;
        var citizen = new Citizen
        {
            EdgeId = edgeId,
            From = from,
            Distance = Vector2.Distance(Sidewalks.Points[from], Sidewalks.Points[to]) * fraction,
            Speed = speed
        };
        citizen.Position = Vector2.Lerp(Sidewalks.Points[from], Sidewalks.Points[to], fraction);
        citizens.Add(citizen);
        return citizen;
    }

    // 스트리밍 또는 거리 조건으로 회수한 시민의 점유는 다음 판단 전에 즉시 다시 집계한다.
    public void Remove(Citizen citizen)
    {
        citizens.Remove(citizen);
        PublishOccupancy();
    }

    // 신호 판단과 점유 등록을 차량 스텝보다 먼저 처리해 같은 스텝에 새 차량이 진입하지 못하게 한다.
    public void Tick(float seconds, IReadOnlyList<RoadTrafficVehicle> vehicles,
        IReadOnlyList<RoadTrafficObstacle> obstacles, Func<int, bool> isRoadActive)
    {
        if (!RoadLaneMath.IsFinite(seconds) || seconds < 0f || seconds > 0.1f)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }
        if (!roads.IsValid || seconds == 0f)
        {
            return;
        }
        foreach (var citizen in citizens)
        {
            if (citizen.IsFallen) continue;
            var edge = Sidewalks.Edges[citizen.EdgeId];
            if (!isRoadActive(edge.Road))
            {
                continue;
            }
            if (edge.IsCrossing && !citizen.Crossing)
            {
                if (!CanStartCrossing(edge, vehicles, obstacles))
                {
                    continue;
                }
                citizen.Crossing = true;
            }
            var destination = citizen.From == edge.A ? edge.B : edge.A;
            var start = Sidewalks.Points[citizen.From];
            var end = Sidewalks.Points[destination];
            var length = Vector2.Distance(start, end);
            citizen.Distance = Mathf.Min(length, citizen.Distance + citizen.Speed * seconds);
            citizen.Position = Vector2.Lerp(start, end, citizen.Distance / length);
            if (citizen.Distance >= length)
            {
                ChooseNext(citizen, destination, isRoadActive);
            }
        }
        PublishOccupancy();
    }

    // 차량 뒤가 횡단보도를 벗어날 때까지 기다린다. 플레이어와 물리 제어 차량은 근접 관측으로 확인한다.
    private bool CanStartCrossing(RoadSidewalkNetwork.Edge edge, IReadOnlyList<RoadTrafficVehicle> vehicles,
        IReadOnlyList<RoadTrafficObstacle> obstacles)
    {
        if (signals.GetPedestrianLight(edge.Road, edge.Side) != RoadTrafficLight.Green)
        {
            return false;
        }
        for (var index = 0; index < vehicles.Count; index++)
        {
            var vehicle = vehicles[index];
            // 충돌 뒤 AI 위치는 갱신되지 않는다. 해당 차체는 호출자가 전달한 실제 물리 관측으로 검사한다.
            if (vehicle.IsFinished || vehicle.IsPhysicsControlled)
            {
                continue;
            }
            if (vehicle.ReservedMovement >= 0)
            {
                var movement = roads.Paths[vehicle.ReservedMovement];
                if (movement.RoadOrdinal == edge.Road && (movement.Entry == edge.Side || movement.Exit == edge.Side))
                {
                    return false;
                }
            }
        }
        if (obstacles != null)
        {
            for (var index = 0; index < obstacles.Count; index++)
            {
                var obstacle = obstacles[index];
                var radius = (obstacle.Length + obstacle.Width) * 0.5f;
                if (IsNearCrossing(edge, obstacle.Position, radius)
                    || IsNearCrossing(edge, obstacle.Position + obstacle.Velocity, radius))
                {
                    return false;
                }
            }
        }
        return true;
    }

    // 선분과의 거리에 차체 반경과 시민 여유를 더해 보수적인 진입 대기 영역을 구한다.
    private bool IsNearCrossing(RoadSidewalkNetwork.Edge edge, Vector2 point, float radius)
    {
        var a = Sidewalks.Points[edge.A];
        var delta = Sidewalks.Points[edge.B] - a;
        var fraction = Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude);
        var gap = roads.Roads[edge.Road].Layout.LaneWidth * 0.3f;
        return (point - a - delta * fraction).sqrMagnitude < (radius + gap) * (radius + gap);
    }

    // 원래 구간으로의 즉시 왕복을 피하되 활성 도로의 막다른 인도에서는 되돌아간다.
    private void ChooseNext(Citizen citizen, int point, Func<int, bool> isRoadActive)
    {
        var chosen = citizen.EdgeId;
        var candidates = 0;
        foreach (var id in Sidewalks.Connections[point])
        {
            if (id == citizen.EdgeId || !isRoadActive(Sidewalks.Edges[id].Road))
            {
                continue;
            }
            if (random.Next(++candidates) == 0)
            {
                chosen = id;
            }
        }
        citizen.EdgeId = chosen;
        citizen.From = point;
        citizen.Distance = 0f;
        citizen.Crossing = false;
    }

    // 여러 시민이 한 횡단보도를 이용해도 마지막 시민이 나갈 때까지 차단한다.
    private void PublishOccupancy()
    {
        ClearOccupancy();
        foreach (var citizen in citizens)
        {
            if (citizen.Crossing && !citizen.IsFallen)
            {
                var edge = Sidewalks.Edges[citizen.EdgeId];
                signals.SetPedestrianCrossingOccupied(edge.Road, edge.Side, true);
            }
        }
    }

    // 시민 채널만 해제하며 외부 시스템의 별도 횡단보도 차단은 보존한다.
    public void ClearOccupancy()
    {
        if (Sidewalks == null || signals == null)
        {
            return;
        }
        foreach (var edge in Sidewalks.Edges)
        {
            if (edge.IsCrossing)
            {
                signals.SetPedestrianCrossingOccupied(edge.Road, edge.Side, false);
            }
        }
    }
}
