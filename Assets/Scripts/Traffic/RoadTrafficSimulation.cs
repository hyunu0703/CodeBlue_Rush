using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>차로 그래프 기반 교통 시뮬레이션이다. 한 스텝의 관측→판단→예약→이동을 순서대로 처리하며 Unity 물리는 호출하지 않는다.</summary>
public sealed class RoadTrafficSimulation
{
    // 초 단위 반응/예측 시간이다. 공간 튜닝은 실제 맵의 차로 폭에서 환산한다.
    public const float HeadwaySeconds = 1.4f;
    // 순항 속도 기준 최소 계획 시간이다. 저속/정차 중에는 실제 전진 거리에 따라 더 오래 걸릴 수 있다.
    public const float LaneChangeSeconds = 3f;
    // 횡방향 최대 기울기를 낮춰 저속에서도 차체가 갑자기 큰 각도로 회전하지 않게 하는 계획 거리다.
    private const float MinimumLaneChangeWidths = 6f;
    // 좌우 회전의 목표 속도는 차량별 평소 순항 속도의 90%다.
    private const float CurveSpeedRatio = 0.9f;
    private const float PredictionSeconds = 0.8f;
    private const float StopHoldSeconds = 0.3f;
    private const float FormationBoostSeconds = 6f; // 나란한 차량 한 대에 적용할 임시 순항 보정 시간(초)이다.
    private const float FormationCooldownSeconds = 10f; // 같은 차량의 반복 보정 사이에 둘 최소 시간(초)이다.
    private RoadLaneNetwork network;
    private readonly List<RoadTrafficVehicle> vehicles = new List<RoadTrafficVehicle>();
    private readonly List<RoadTrafficObstacle> observations = new List<RoadTrafficObstacle>();
    private readonly System.Random random;
    private int nextId = 1;
    private int pickupPathId = -1;
    private float pickupStart;
    private float pickupEnd;
    private float pickupApproach;
    private float pickupMinimumChangeDistance;

    /// <summary>픽업 사각형과 구급차 세 대 길이의 접근 거리를 맵 로컬 단위로 받는다.</summary>
    internal void ConfigurePickupZone(int pathId, float start, float end, float approach)
    {
        if (pathId < 0 || pathId >= network.Paths.Count || !RoadLaneMath.IsFinite(start)
            || !RoadLaneMath.IsFinite(end) || end <= start || !RoadLaneMath.IsFinite(approach) || approach <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(pathId));
        }
        pickupPathId = pathId;
        pickupStart = start;
        pickupEnd = end;
        pickupApproach = approach;
        // 일반 추월보다 짧은 픽업 접근 구간에서도 같은 보간 곡선으로 변경을 완료한다.
        pickupMinimumChangeDistance = Math.Min(network.Paths[pathId].Width * MinimumLaneChangeWidths, approach * 0.85f);
    }
    public IReadOnlyList<RoadTrafficVehicle> Vehicles { get; }
    public RoadTrafficSignals Signals { get; private set; }

    /// <summary>맵 스냅숏과 재현 가능한 시드로 새 세션을 만든다. 호출자가 맵 교체 시 이 인스턴스를 폐기한다.</summary>
    public RoadTrafficSimulation(RoadLaneNetwork network, int seed)
    {
        this.network = network ?? throw new ArgumentNullException(nameof(network));
        random = new System.Random(seed);
        Signals = new RoadTrafficSignals(network);
        Vehicles = vehicles.AsReadOnly();
    }

    /// <summary>기존 배치를 접두부로 보존하는 확장만 적용한다. 차량과 난수/예약 상태는 초기화하지 않는다.</summary>
    internal bool TryExtendNetwork(RoadLaneNetwork next)
    {
        if (next == null || !next.IsValid || next.Roads.Count <= network.Roads.Count)
        {
            return false;
        }
        // 도로 번호와 경로 번호가 유지되는 동일 배치인지 교체 전에 확인한다.
        for (var index = 0; index < network.Roads.Count; index++)
        {
            var previousRoad = network.Roads[index];
            var nextRoad = next.Roads[index];
            if (!ReferenceEquals(previousRoad.Layout, nextRoad.Layout)
                || previousRoad.Position != nextRoad.Position
                || previousRoad.ClockwiseQuarterTurns != nextRoad.ClockwiseQuarterTurns
                || previousRoad.IsTrafficClosed != nextRoad.IsTrafficClosed
                || previousRoad.ConstructionBarrierSide != nextRoad.ConstructionBarrierSide)
            {
                return false;
            }
        }
        // 기존 신호 위상과 차량 자세를 유지하고 새로 열린 막다른 길의 다음 경로만 선택한다.
        var nextSignals = new RoadTrafficSignals(next);
        nextSignals.CopyStateFrom(Signals);
        network = next;
        Signals = nextSignals;
        observations.Clear();
        foreach (var vehicle in vehicles)
        {
            if (vehicle.NextPathId < 0 && network.Paths[vehicle.PathId].Successors.Count > 0)
            {
                vehicle.IsFinished = false;
                ChooseNext(vehicle);
            }
        }
        return true;
    }

    /// <summary>차로 중앙에 정지 상태로 등록한다. 경계/정지선/기존 차량 간격이 부족하면 생성하지 않는다.</summary>
    public bool TryAddVehicle(int pathId, float distance, float length, float width, float cruiseSpeed,
        IReadOnlyList<RoadTrafficObstacle> external, out RoadTrafficVehicle vehicle)
    {
        return TryActivateVehicle(new RoadTrafficVehicle(), pathId, distance, length, width, cruiseSpeed, external, out vehicle);
    }

    /// <summary>컨트롤러 풀의 상태를 초기화해 재등록한다. 실패한 후보는 등록하지 않으며 기존 public 생성 API를 보존한다.</summary>
    internal bool TryActivateVehicle(RoadTrafficVehicle candidate, int pathId, float distance, float length, float width,
        float cruiseSpeed, IReadOnlyList<RoadTrafficObstacle> external, out RoadTrafficVehicle vehicle)
    {
        vehicle = null;
        if (candidate == null || vehicles.Contains(candidate) || !network.IsValid || pathId < 0 || pathId >= network.Paths.Count
            || !RoadLaneMath.IsFinite(distance) || !RoadLaneMath.IsFinite(length) || length <= 0f
            || !RoadLaneMath.IsFinite(width) || width <= 0f || !RoadLaneMath.IsFinite(cruiseSpeed) || cruiseSpeed <= 0f)
        {
            return false;
        }
        // 교차로 안에 직접 생성하지 않고 차폭과 앞뒤 범퍼의 배치 여유를 확인한다.
        var path = network.Paths[pathId];
        if (!path.IsTrafficAllowed || path.Kind == RoadLanePathKind.Intersection || width > path.Width * 0.85f
            || (pathId == pickupPathId && distance + length * 0.5f >= pickupStart - pickupApproach
                && distance - length * 0.5f <= pickupEnd)
            || cruiseSpeed > path.Width * 3f
            || distance < length * 0.5f || distance > path.Length - length * 0.5f)
        {
            return false;
        }
        // 풀 반환 전의 예약, 차로 변경, 정차 시간과 속도를 모두 제거한다.
        candidate.Id = nextId;
        candidate.PathId = pathId;
        candidate.Distance = distance;
        candidate.Length = length;
        candidate.Width = width;
        candidate.CruiseSpeed = cruiseSpeed;
        candidate.Speed = 0f;
        candidate.IsFinished = false;
        candidate.IsPhysicsControlled = false;
        candidate.TargetLaneId = -1;
        candidate.NextPathId = -1;
        candidate.ReservedMovement = -1;
        candidate.ExitClearanceRemaining = -1f;
        candidate.StoppedSeconds = 0f;
        candidate.LaneChangeStartDistance = 0f;
        candidate.LaneChangeDistance = 0f;
        candidate.LaneChangeCooldown = 0f;
        candidate.TargetSpeed = 0f;
        candidate.ClearDistance = float.PositiveInfinity;
        candidate.FormationBoostRemaining = 0f;
        candidate.FormationBoostCooldown = 0f;
        UpdatePose(candidate);
        BuildObservations(external);
        if (!HasLaneSpace(candidate, pathId, distance, length + path.Width))
        {
            return false;
        }
        ChooseNext(candidate);
        vehicles.Add(candidate);
        nextId++;
        vehicle = candidate;
        return true;
    }

    /// <summary>소유 차량만 제거한다. 외부 차량이나 네트워크 상태는 변경하지 않는다.</summary>
    public void RemoveVehicle(RoadTrafficVehicle vehicle)
    {
        if (vehicle != null)
        {
            vehicles.Remove(vehicle);
        }
    }

    /// <summary>최대 0.1초의 시뮬레이션 경계를 진행한다. 0초 또는 무효화된 맵에서는 움직이지 않는다.</summary>
    public void Tick(float seconds, IReadOnlyList<RoadTrafficObstacle> external)
    {
        if (!RoadLaneMath.IsFinite(seconds) || seconds < 0f || seconds > 0.1f)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }
        if (!network.IsValid || seconds == 0f)
        {
            return;
        }
        Signals.Tick(seconds);
        BuildObservations(external);
        foreach (var vehicle in vehicles)
        {
            if (vehicle.IsFinished || vehicle.IsPhysicsControlled)
            {
                continue;
            }
            vehicle.LaneChangeCooldown = Math.Max(0f, vehicle.LaneChangeCooldown - seconds);
            EvaluateSpeed(vehicle, seconds);
        }
        UpdateFormationSeparation(seconds);
        // 판단은 동일 관측값을 사용한다. 예약과 차로 변경 의도만 ID 순서대로 확정하여 동시 진입 충돌을 막는다.
        foreach (var vehicle in vehicles)
        {
            if (vehicle.IsFinished || vehicle.IsPhysicsControlled)
            {
                continue;
            }
            if (!RouteAroundPickup(vehicle))
            {
                TryOvertake(vehicle);
            }
            Advance(vehicle, seconds);
        }
    }

    /// <summary>동일 진행 방향의 바로 옆 차로만 요청한다. 앞/뒤 차량과 다른 차량의 변경 의도를 확인한 뒤 양쪽 차로를 점유한다.</summary>
    public bool TryChangeLane(RoadTrafficVehicle vehicle, bool toLeft)
    {
        if (!network.IsValid || vehicle == null || !vehicles.Contains(vehicle) || vehicle.IsFinished || vehicle.IsPhysicsControlled
            || vehicle.IsChangingLane || vehicle.LaneChangeCooldown > 0f
            || (vehicle.Speed <= 0.05f && !(vehicle.PathId == pickupPathId && toLeft)))
        {
            return false;
        }
        // 정지선 부근과 교차로 내부에서는 변경을 시작하지 않는다. 완료할 전방 길이도 확보한다.
        var path = network.Paths[vehicle.PathId];
        var targetId = toLeft ? path.LeftNeighborId : path.RightNeighborId;
        // 시간과 차로 폭 양쪽 기준을 만족하는 전진 거리를 확보한 경우에만 부드러운 변경 궤적을 시작한다.
        var changeDistance = Math.Max(path.Width * MinimumLaneChangeWidths, vehicle.CruiseSpeed * LaneChangeSeconds);
        if (vehicle.PathId == pickupPathId && toLeft)
        {
            // 픽업 도로에서 즉시 시작하되 앞범퍼가 픽업 구역에 닿기 전에 변경을 완료한다.
            changeDistance = Math.Min(changeDistance, pickupStart - vehicle.Length * 0.5f - vehicle.Distance);
            if (changeDistance < pickupMinimumChangeDistance)
            {
                return false;
            }
        }
        if (targetId == pickupPathId && vehicle.Distance + changeDistance + vehicle.Length * 0.5f >= pickupStart - pickupApproach
            && vehicle.Distance - vehicle.Length * 0.5f <= pickupEnd)
        {
            return false;
        }
        if (targetId < 0 || !path.CanChangeLaneAt(vehicle.Distance)
            || vehicle.Distance + changeDistance + vehicle.Length > path.LaneChangeEndDistance)
        {
            return false;
        }
        var target = network.Paths[targetId];
        if (vehicle.Width > target.Width * 0.85f
            || !target.TryProject(vehicle.Position, out var along, out _)
            || !HasLaneSpace(vehicle, targetId, along, target.Width * 0.6f))
        {
            return false;
        }
        foreach (var other in vehicles)
        {
            // 반대 방향 맞교환과 동시에 같은 차로로 합류하는 경우까지 변경 의도로 보호한다.
            if (other != vehicle && other.IsChangingLane
                && (other.TargetLaneId == targetId || other.PathId == targetId || other.TargetLaneId == vehicle.PathId)
                && Math.Abs(other.Distance - vehicle.Distance) < vehicle.Length + other.Length
                    + Math.Max(vehicle.Speed, other.Speed) * HeadwaySeconds)
            {
                return false;
            }
        }
        vehicle.TargetLaneId = targetId;
        vehicle.LaneChangeStartDistance = vehicle.Distance;
        vehicle.LaneChangeDistance = changeDistance;
        vehicle.LaneChangeCooldown = 6f;
        return true;
    }

    /// <summary>한 스텝의 AI와 외부 차량 관측값을 보관한다. 외부 관측은 살아 있는 Rigidbody/Collider를 호출자가 검증한다.</summary>
    private void BuildObservations(IReadOnlyList<RoadTrafficObstacle> external)
    {
        observations.Clear();
        foreach (var vehicle in vehicles)
        {
            // 경로 끝 차량도 화면 밖으로 회수될 때까지 차체가 남으므로 뒤차의 정지 관측에 포함한다.
            if (!vehicle.IsPhysicsControlled)
            {
                observations.Add(new RoadTrafficObstacle(vehicle.Id, vehicle.Position,
                    vehicle.Forward * vehicle.Speed, vehicle.Forward, vehicle.Length, vehicle.Width));
                if (vehicle.IsChangingLane)
                {
                    // 변경 시작부터 완료까지 두 차로 모두 차체가 있는 것으로 관측해 뒤차의 양보를 미리 유도한다.
                    network.Paths[vehicle.PathId].TrySample(vehicle.Distance, out var sourcePoint, out var sourceForward);
                    network.Paths[vehicle.TargetLaneId].TrySample(vehicle.Distance, out var targetPoint, out var targetForward);
                    observations.Add(new RoadTrafficObstacle(vehicle.Id, sourcePoint,
                        sourceForward * vehicle.Speed, sourceForward, vehicle.Length, vehicle.Width));
                    observations.Add(new RoadTrafficObstacle(vehicle.Id, targetPoint,
                        targetForward * vehicle.Speed, targetForward, vehicle.Length, vehicle.Width));
                }
            }
        }
        if (external != null)
        {
            for (var index = 0; index < external.Count; index++)
            {
                var obstacle = external[index];
                if (RoadLaneMath.IsFinite(obstacle.Position) && RoadLaneMath.IsFinite(obstacle.Velocity)
                    && RoadLaneMath.IsFinite(obstacle.Forward) && RoadLaneMath.IsFinite(obstacle.Length)
                    && RoadLaneMath.IsFinite(obstacle.Width) && obstacle.Length > 0f && obstacle.Width > 0f)
                {
                    observations.Add(obstacle);
                }
            }
        }
    }

    /// <summary>곡선 너머 차량까지 경로에 투영하고 속도에 따른 차간 거리와 제동 거리를 만족하는 목표 속도를 구한다.</summary>
    private void EvaluateSpeed(RoadTrafficVehicle vehicle, float seconds)
    {
        // 차로 폭을 게임 공간의 기준으로 쓰며 미터 단위로 가정하지 않는다.
        var path = network.Paths[vehicle.PathId];
        var braking = path.Width * 3f;
        var minimumGap = path.Width * 0.6f;
        // 좌우 커브에서만 10% 감속한다. 직진 및 교통 상황에 필요한 감속은 별도로 유지한다.
        var cruise = vehicle.FormationBoostRemaining > 0f && CanUseFormationBoost(vehicle)
            ? GetFormationSpeed(vehicle) : vehicle.CruiseSpeed;
        vehicle.TargetSpeed = path.Kind == RoadLanePathKind.Intersection && path.Maneuver != RoadLaneManeuver.Straight
            ? vehicle.CruiseSpeed * CurveSpeedRatio : cruise;
        vehicle.ClearDistance = float.PositiveInfinity;
        LimitForTraffic(vehicle, vehicle.PathId, vehicle.Distance, braking, minimumGap);
        if (vehicle.PathId == pickupPathId && !vehicle.IsChangingLane
            && vehicle.Distance - vehicle.Length * 0.5f <= pickupEnd)
        {
            // 합류가 막히면 노란 구역을 침범하지 않고 정지한 뒤 다시 시도한다.
            var stopAt = Math.Max(0f, pickupStart - vehicle.Length * 0.5f
                - pickupMinimumChangeDistance - Math.Min(path.Width * 0.1f, pickupApproach * 0.05f));
            LimitForStop(vehicle, Math.Max(0f, stopAt - vehicle.Distance), braking);
        }
        if (vehicle.IsChangingLane)
        {
            network.Paths[vehicle.TargetLaneId].TryProject(vehicle.Position, out var targetAlong, out _);
            LimitForTraffic(vehicle, vehicle.TargetLaneId, targetAlong, braking, minimumGap);
        }
        if (path.HasStopLine && vehicle.NextPathId >= 0 && vehicle.ReservedMovement < 0)
        {
            // 정지선은 앞범퍼 기준이다. 적색 우회전은 선 앞 실제 정지 상태를 최소 시간 동안 확인한다.
            var lineGap = Math.Max(0f, path.Length - vehicle.Distance - vehicle.Length * 0.5f);
            if (lineGap < path.Width * 0.04f && vehicle.Speed < path.Width * 0.04f)
            {
                vehicle.StoppedSeconds += seconds;
            }
            else
            {
                vehicle.StoppedSeconds = 0f;
            }
            if (!CanEnter(vehicle))
            {
                LimitForStop(vehicle, lineGap, braking);
            }
        }
        if (path.HasStopLine && vehicle.NextPathId >= 0
            && network.Paths[vehicle.NextPathId].Maneuver != RoadLaneManeuver.Straight)
        {
            // 앞범퍼부터 차량 중심까지 커브에 진입하는 동안에도 감속 목표를 유지한다.
            // 남은 전방 거리에 맞춰 90% 속도로 접근하며, 신호/앞차가 요구한 더 낮은 속도는 보존한다.
            var curveGap = Math.Max(0f, path.Length - vehicle.Distance - vehicle.Length * 0.5f);
            var curveSpeed = vehicle.CruiseSpeed * CurveSpeedRatio;
            vehicle.TargetSpeed = Math.Min(vehicle.TargetSpeed,
                (float)Math.Sqrt(curveSpeed * curveSpeed + 2f * braking * curveGap));
        }
        if (vehicle.NextPathId < 0)
        {
            LimitForStop(vehicle, Math.Max(0f, path.Length - vehicle.Distance - vehicle.Length * 0.5f), braking);
        }
    }

    // 차로 중앙을 유지하면서 다른 차로의 차량과 차체 길이 이상의 앞뒤 간격을 두고 생성한다.
    internal bool HasStaggeredSpawnSpace(int pathId, float distance, float length)
    {
        var path = network.Paths[pathId];
        if (path.Kind != RoadLanePathKind.Straight || !path.TrySample(distance, out var position, out var forward)) return true;
        foreach (var other in vehicles)
        {
            if (TryGetParallelOffset(path, position, forward, other, out var along)
                && Math.Abs(along) < (length + other.Length) * 0.5f + path.Width * 0.5f)
            {
                return false;
            }
        }
        return true;
    }

    // 타일 번호가 달라도 동일 직선의 같은 방향 다른 차로인지 실제 위치와 접선으로 판단한다.
    private bool TryGetParallelOffset(RoadLanePath path, Vector2 position, Vector2 forward,
        RoadTrafficVehicle other, out float along)
    {
        along = 0f;
        if (other.IsFinished || other.IsPhysicsControlled || other.IsChangingLane
            || network.Paths[other.PathId].Kind != RoadLanePathKind.Straight
            || RoadLaneMath.Dot(forward, other.Forward) < 0.99f) return false;
        var delta = other.Position - position;
        var lateral = Math.Abs(RoadLaneMath.Dot(delta, RoadLaneMath.Right(forward)));
        if (lateral < path.Width * 0.5f || lateral > path.Width * 2.2f) return false;
        along = RoadLaneMath.Dot(delta, forward);
        return true;
    }

    // 기본 순항 속도를 보존하고 기존 생성 속도 상한 안에서 안쪽 차로부터 다른 보정 폭을 사용한다.
    private float GetFormationSpeed(RoadTrafficVehicle vehicle)
    {
        var path = network.Paths[vehicle.PathId];
        return Math.Min(path.Width * 3f, vehicle.CruiseSpeed + path.Width * 0.75f / Math.Max(1, path.EntryLaneNumber));
    }

    // 정지·변경·픽업·교차로 접근은 제외하고 보정을 마칠 전방 직선 공간이 있는지 확인한다.
    private bool CanUseFormationBoost(RoadTrafficVehicle vehicle)
    {
        var path = network.Paths[vehicle.PathId];
        if (vehicle.IsFinished || vehicle.IsPhysicsControlled || vehicle.IsChangingLane
            || path.Kind != RoadLanePathKind.Straight || vehicle.Speed < path.Width * 0.7f
            || (pickupPathId >= 0 && path.RoadOrdinal == network.Paths[pickupPathId].RoadOrdinal)) return false;
        var required = Math.Max(vehicle.Length * 2f + path.Width, GetFormationSpeed(vehicle) * HeadwaySeconds * 2f);
        var available = path.Length - vehicle.Distance - vehicle.Length * 0.5f;
        for (var hop = 0; hop < 3; hop++)
        {
            if (available >= required) return true;
            if (path.Successors.Count != 1) return false;
            path = network.Paths[path.Successors[0]];
            if (path.Kind != RoadLanePathKind.Straight
                || (pickupPathId >= 0 && path.RoadOrdinal == network.Paths[pickupPathId].RoadOrdinal)) return false;
            available += path.Length;
        }
        return false;
    }

    // 일반 교통 판단이 이미 감속을 요구하거나 가속할 여유가 없는 차량은 후보로 선택하지 않는다.
    private bool CanStartFormationBoost(RoadTrafficVehicle vehicle)
    {
        return vehicle.FormationBoostRemaining <= 0f && vehicle.FormationBoostCooldown <= 0f
            && CanUseFormationBoost(vehicle) && vehicle.TargetSpeed >= vehicle.CruiseSpeed * 0.95f
            && GetFormationSpeed(vehicle) > vehicle.Speed + network.Paths[vehicle.PathId].Width * 0.1f
            && vehicle.ClearDistance > Math.Max(vehicle.Length + network.Paths[vehicle.PathId].Width,
                GetFormationSpeed(vehicle) * HeadwaySeconds);
    }

    // 같은 관측 프레임에서 한 대를 먼저 선택하고 안전 속도 제한을 다시 적용한 뒤 기존 가속도로 이동한다.
    private void UpdateFormationSeparation(float seconds)
    {
        foreach (var vehicle in vehicles)
        {
            var wasBoosted = vehicle.FormationBoostRemaining > 0f;
            vehicle.FormationBoostRemaining = Math.Max(0f, vehicle.FormationBoostRemaining - seconds);
            vehicle.FormationBoostCooldown = Math.Max(0f, vehicle.FormationBoostCooldown - seconds);
            if (!wasBoosted) continue;
            if (!CanUseFormationBoost(vehicle) || vehicle.TargetSpeed < vehicle.CruiseSpeed * 0.95f)
                vehicle.FormationBoostRemaining = 0f;
            if (!vehicle.IsFinished && !vehicle.IsPhysicsControlled) EvaluateSpeed(vehicle, 0f);
        }
        foreach (var vehicle in vehicles)
        {
            if (!CanStartFormationBoost(vehicle)) continue;
            var path = network.Paths[vehicle.PathId];
            var hasNeighbor = false;
            var isPreferred = true;
            foreach (var other in vehicles)
            {
                if (other == vehicle || !TryGetParallelOffset(path, vehicle.Position, vehicle.Forward, other, out var along)
                    || Math.Abs(along) > (vehicle.Length + other.Length) * 0.5f + path.Width * 0.5f) continue;
                // 진행 중인 보정이 먼저 틈을 만들도록 기다려 3개 차로가 동시에 가속하지 않게 한다.
                if (other.FormationBoostRemaining > 0f)
                {
                    isPreferred = false;
                    break;
                }
                if (other.Speed < path.Width * 0.7f || Math.Abs(vehicle.Speed - other.Speed) > path.Width * 0.3f) continue;
                hasNeighbor = true;
                var otherLane = network.Paths[other.PathId].EntryLaneNumber;
                if ((otherLane < path.EntryLaneNumber || (otherLane == path.EntryLaneNumber && other.Id < vehicle.Id))
                    && CanStartFormationBoost(other)) isPreferred = false;
            }
            if (!hasNeighbor || !isPreferred) continue;
            vehicle.FormationBoostRemaining = FormationBoostSeconds;
            vehicle.FormationBoostCooldown = FormationCooldownSeconds;
            EvaluateSpeed(vehicle, 0f);
        }
    }

    /// <summary>관측한 차량을 현재 경로와 미리 선택한 다음 경로들에 투영한다. 전방의 차로 변경 차량은 예측 위치도 검사한다.</summary>
    private void LimitForTraffic(RoadTrafficVehicle vehicle, int pathId, float distance, float braking, float gap)
    {
        // 회전 뒤 출구와 다음 타일까지 최대 세 경로를 본다. 이 범위를 넘는 고속 주행은 생성 시 제한한다.
        var offset = -distance;
        for (var hop = 0; hop < 3 && pathId >= 0; hop++)
        {
            var path = network.Paths[pathId];
            foreach (var obstacle in observations)
            {
                if (obstacle.Id == vehicle.Id)
                {
                    continue;
                }
                // 평상시에는 현재 차체 기준 전방만 예측한다. 뒤차의 방향이나 예측 위치로 앞뒤 관계를 바꾸지 않는다.
                if (RoadLaneMath.Dot(obstacle.Position - vehicle.Position, vehicle.Forward) < 0f)
                {
                    continue;
                }
                for (var prediction = 0; prediction < 2; prediction++)
                {
                    var point = obstacle.Position + obstacle.Velocity * (prediction * PredictionSeconds);
                    if (!TryCorridorProjection(path, point, vehicle.Width, obstacle, out var along, out var halfLength))
                    {
                        continue;
                    }
                    var centerAhead = offset + along;
                    if (centerAhead < -vehicle.Length * 0.25f)
                    {
                        continue;
                    }
                    var clearance = Math.Max(0f, centerAhead - halfLength - vehicle.Length * 0.5f - gap);
                    path.TrySample(along, out _, out var tangent);
                    var leaderSpeed = Math.Max(0f, RoadLaneMath.Dot(obstacle.Velocity, tangent));
                    vehicle.TargetSpeed = Math.Min(vehicle.TargetSpeed,
                        Math.Min(leaderSpeed + clearance / HeadwaySeconds,
                            (float)Math.Sqrt(leaderSpeed * leaderSpeed + 2f * braking * clearance)));
                    vehicle.ClearDistance = Math.Min(vehicle.ClearDistance, clearance);
                }
            }
            offset += path.Length;
            pathId = hop == 0 && path.Id == vehicle.PathId ? vehicle.NextPathId
                : path.Successors.Count == 1 ? path.Successors[0] : -1;
        }
    }

    /// <summary>차체 방향에 따른 길이/폭을 경로 접선에 투영한다. 끝점에서 멀리 떨어진 차량은 경로 위로 취급하지 않는다.</summary>
    private static bool TryCorridorProjection(RoadLanePath path, Vector2 position, float ownWidth,
        RoadTrafficObstacle obstacle, out float along, out float halfLength)
    {
        halfLength = 0f;
        if (!path.TryProject(position, out along, out _) || !path.TrySample(along, out var point, out var tangent))
        {
            return false;
        }
        // 차체 방향이 차로와 다르면 가로질러 들어오는 차의 길이도 차로 폭 점유로 계산한다.
        var right = RoadLaneMath.Right(tangent);
        var otherRight = RoadLaneMath.Right(obstacle.Forward);
        var halfWidth = (Math.Abs(RoadLaneMath.Dot(obstacle.Forward, right)) * obstacle.Length
            + Math.Abs(RoadLaneMath.Dot(otherRight, right)) * obstacle.Width) * 0.5f;
        halfLength = (Math.Abs(RoadLaneMath.Dot(obstacle.Forward, tangent)) * obstacle.Length
            + Math.Abs(RoadLaneMath.Dot(otherRight, tangent)) * obstacle.Width) * 0.5f;
        var delta = position - point;
        return Math.Abs(RoadLaneMath.Dot(delta, right)) <= halfWidth + ownWidth * 0.5f + path.Width * 0.12f
            && Math.Abs(RoadLaneMath.Dot(delta, tangent)) <= halfLength + path.Width * 0.1f;
    }

    /// <summary>목표 차로의 앞뒤 간격과 후방 접근 속도를 검사한다. 차량 크기와 두 차량 중 빠른 쪽의 시간 간격을 반영한다.</summary>
    private bool HasLaneSpace(RoadTrafficVehicle vehicle, int pathId, float distance, float minimumGap, bool includeRear = true)
    {
        var path = network.Paths[pathId];
        foreach (var obstacle in observations)
        {
            // 생성/차로 변경은 앞뒤 안전 간격을 확인하지만 주행 중 교차로 출구 검사는 전방만 확인한다.
            if (obstacle.Id == vehicle.Id
                || (!includeRear && RoadLaneMath.Dot(obstacle.Position - vehicle.Position, vehicle.Forward) < 0f))
            {
                continue;
            }
            for (var prediction = 0; prediction < 2; prediction++)
            {
                var point = obstacle.Position + obstacle.Velocity * (prediction * PredictionSeconds);
                if (!TryCorridorProjection(path, point, vehicle.Width, obstacle, out var along, out var halfLength))
                {
                    continue;
                }
                path.TrySample(along, out _, out var tangent);
                var ahead = along - distance;
                var otherSpeed = Math.Max(0f, RoadLaneMath.Dot(obstacle.Velocity, tangent));
                var required = halfLength + vehicle.Length * 0.5f + minimumGap
                    + (ahead >= 0f ? vehicle.Speed : otherSpeed) * HeadwaySeconds;
                if (Math.Abs(ahead) < required)
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>정지선까지의 제동 거리와 한 스텝 이동 상한을 추가한다.</summary>
    private static void LimitForStop(RoadTrafficVehicle vehicle, float distance, float braking)
    {
        vehicle.TargetSpeed = Math.Min(vehicle.TargetSpeed, (float)Math.Sqrt(2f * braking * distance));
        vehicle.ClearDistance = Math.Min(vehicle.ClearDistance, distance);
    }

    /// <summary>진행 허가, 보행자, 충돌 경로 예약, 교차로 출구의 여유를 모두 확인한다.</summary>
    private bool CanEnter(RoadTrafficVehicle vehicle)
    {
        var movement = network.Paths[vehicle.NextPathId];
        var permission = Signals.GetPermission(movement);
        if (permission == RoadTrafficPermission.Stop
            || (permission == RoadTrafficPermission.StopThenYield && vehicle.StoppedSeconds < StopHoldSeconds))
        {
            return false;
        }
        foreach (var other in vehicles)
        {
            if (other != vehicle && !other.IsFinished && !other.IsPhysicsControlled
                && other.ReservedMovement >= 0 && Conflicts(movement, other.ReservedMovement))
            {
                return false;
            }
        }
        // 우회전은 신호를 받은 접근 차량에도 양보한다. 플레이어는 신호 준수를 가정하지 않고 모든 교차 경로에서 검사한다.
        foreach (var pathId in movement.Conflicts)
        {
            var conflicting = network.Paths[pathId];
            foreach (var obstacle in observations)
            {
                // 뒤쪽 플레이어의 예상 위치 때문에 교차로 앞차를 정지시키지 않는다.
                if (obstacle.Id == vehicle.Id
                    || RoadLaneMath.Dot(obstacle.Position - vehicle.Position, vehicle.Forward) < 0f)
                {
                    continue;
                }
                if (obstacle.Id < 0 && (TryCorridorProjection(conflicting, obstacle.Position, vehicle.Width,
                    obstacle, out _, out _) || TryCorridorProjection(conflicting,
                    obstacle.Position + obstacle.Velocity * 2f, vehicle.Width, obstacle, out _, out _)))
                {
                    return false;
                }
            }
            if (permission == RoadTrafficPermission.Yield || permission == RoadTrafficPermission.StopThenYield)
            {
                foreach (var other in vehicles)
                {
                    if (other == vehicle || other.NextPathId != pathId || other.IsFinished || other.IsPhysicsControlled
                        || Signals.GetPermission(conflicting) != RoadTrafficPermission.Proceed)
                    {
                        continue;
                    }
                    var approach = network.Paths[other.PathId];
                    var arrival = approach.Length - other.Distance - other.Length * 0.5f;
                    if (arrival < Math.Max(approach.Width, other.Speed * 3f))
                    {
                        return false;
                    }
                }
            }
        }
        if (movement.Successors.Count == 0)
        {
            return false;
        }
        // 교차로를 통과한 뒤 차체 전체가 들어갈 자리가 없으면 꼬리물기하지 않는다.
        return HasLaneSpace(vehicle, movement.Successors[0], 0f, vehicle.Length + movement.Width * 0.6f, false);
    }

    /// <summary>동일 교차 경로 또는 그래프가 알려준 교차/합류 관계를 판정한다.</summary>
    private static bool Conflicts(RoadLanePath movement, int otherId)
    {
        if (movement.Id == otherId)
        {
            return true;
        }
        foreach (var conflict in movement.Conflicts)
        {
            if (conflict == otherId)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>픽업 도로의 우측 차로에서는 거리 대기 없이 안전한 좌측 합류를 시도한다.</summary>
    private bool RouteAroundPickup(RoadTrafficVehicle vehicle)
    {
        if (pickupPathId < 0)
        {
            return false;
        }
        var pickup = network.Paths[pickupPathId];
        var path = network.Paths[vehicle.PathId];
        if (path.RoadOrdinal != pickup.RoadOrdinal || path.Entry != pickup.Entry)
        {
            return false;
        }
        if (vehicle.IsChangingLane)
        {
            return true;
        }
        var isBeforeClearance = vehicle.Distance - vehicle.Length * 0.5f <= pickupEnd;
        if (vehicle.PathId == pickupPathId && isBeforeClearance)
        {
            // 접근 거리나 이전 변경 쿨다운을 기다리지 않는다. 옆 차로가 막히면 정차 후 재시도한다.
            vehicle.LaneChangeCooldown = 0f;
            if (TryChangeLane(vehicle, true))
            {
                EvaluateSpeed(vehicle, 0f);
            }
            return true;
        }
        if (vehicle.PathId != pickupPathId && path.RightNeighborId >= 0)
        {
            TryChangeLane(vehicle, false);
        }
        return true;
    }

    /// <summary>신호 대기 중에는 추월하지 않으며 느린 앞차가 있을 때만 비어 있는 인접 차로를 선택한다.</summary>
    private void TryOvertake(RoadTrafficVehicle vehicle)
    {
        var path = network.Paths[vehicle.PathId];
        if (vehicle.IsChangingLane || vehicle.TargetSpeed > vehicle.CruiseSpeed * 0.5f
            || vehicle.ClearDistance == float.PositiveInfinity
            || (path.HasStopLine && (vehicle.NextPathId < 0 || !CanEnter(vehicle))))
        {
            return;
        }
        if (!TryChangeLane(vehicle, true))
        {
            TryChangeLane(vehicle, false);
        }
    }

    /// <summary>가감속을 적분하고 앞범퍼 진입 시 교차 경로를 예약한다. 곡선 이동과 차로 변경 자세도 갱신한다.</summary>
    private void Advance(RoadTrafficVehicle vehicle, float seconds)
    {
        var path = network.Paths[vehicle.PathId];
        var rate = path.Width * (vehicle.TargetSpeed < vehicle.Speed ? 3f : 0.9f);
        var newSpeed = MoveTowards(vehicle.Speed, vehicle.TargetSpeed, rate * seconds);
        // 목표 이동량은 한 번만 float로 확정한다. 서로 다른 정밀도로 재계산한 값과 비교해 정상 가속을 정지로 오인하지 않는다.
        var requestedTravel = (vehicle.Speed + newSpeed) * 0.5f * seconds;
        var travel = Math.Min(requestedTravel, vehicle.ClearDistance);
        if (path.HasStopLine && vehicle.ReservedMovement < 0 && vehicle.NextPathId >= 0
            && vehicle.Distance + travel + vehicle.Length * 0.5f >= path.Length)
        {
            if (CanEnter(vehicle))
            {
                vehicle.ReservedMovement = vehicle.NextPathId;
            }
            else
            {
                travel = Math.Max(0f, path.Length - vehicle.Distance - vehicle.Length * 0.5f);
                newSpeed = 0f;
            }
        }
        vehicle.Speed = travel < requestedTravel ? 0f : newSpeed;
        vehicle.Distance += travel;
        if (vehicle.ExitClearanceRemaining >= 0f)
        {
            vehicle.ExitClearanceRemaining -= travel;
            if (vehicle.ExitClearanceRemaining <= 0f)
            {
                vehicle.ReservedMovement = -1;
                vehicle.ExitClearanceRemaining = -1f;
            }
        }
        if (vehicle.IsChangingLane)
        {
            // 계획 전진 거리 끝에서만 목표 차로로 넘긴다. 정차는 횡방향 진행과 조향을 함께 멈춘다.
            if (vehicle.Distance >= vehicle.LaneChangeStartDistance + vehicle.LaneChangeDistance)
            {
                vehicle.PathId = vehicle.TargetLaneId;
                vehicle.TargetLaneId = -1;
                ChooseNext(vehicle);
                path = network.Paths[vehicle.PathId];
            }
        }
        // 한 스텝은 최대 0.1초이며 생성 속도 상한으로 여러 타일을 건너뛰지 않는다. 짧은 연결은 반복 처리한다.
        while (vehicle.Distance >= path.Length && vehicle.NextPathId >= 0 && !vehicle.IsChangingLane)
        {
            var leavingIntersection = path.Kind == RoadLanePathKind.Intersection;
            vehicle.Distance -= path.Length;
            vehicle.PathId = vehicle.NextPathId;
            vehicle.StoppedSeconds = 0f;
            path = network.Paths[vehicle.PathId];
            if (leavingIntersection)
            {
                vehicle.ExitClearanceRemaining = vehicle.Length * 0.5f + path.Width * 0.2f - vehicle.Distance;
                if (vehicle.ExitClearanceRemaining <= 0f)
                {
                    vehicle.ReservedMovement = -1;
                    vehicle.ExitClearanceRemaining = -1f;
                }
            }
            ChooseNext(vehicle);
        }
        if (vehicle.NextPathId < 0 && vehicle.Distance >= path.Length - vehicle.Length * 0.5f - path.Width * 0.01f)
        {
            vehicle.IsFinished = true;
            vehicle.Speed = 0f;
        }
        UpdatePose(vehicle);
    }

    /// <summary>분기를 한 번 선택하고 대기 중에도 진행 방향을 유지한다. 연결이 끝나는 경계에서는 안전하게 정지 후 회수된다.</summary>
    private void ChooseNext(RoadTrafficVehicle vehicle)
    {
        var successors = network.Paths[vehicle.PathId].Successors;
        vehicle.NextPathId = successors.Count == 0 ? -1 : successors[random.Next(successors.Count)];
    }

    /// <summary>전진 거리 기반 5차 곡선으로 차로를 보간한다. 차체는 속도와 무관한 궤적 접선을 따라 회전한다.</summary>
    private void UpdatePose(RoadTrafficVehicle vehicle)
    {
        network.Paths[vehicle.PathId].TrySample(vehicle.Distance, out var position, out var forward);
        if (vehicle.IsChangingLane)
        {
            var target = network.Paths[vehicle.TargetLaneId];
            target.TrySample(vehicle.Distance, out var targetPosition, out _);
            // 시작/끝에서 횡방향 기울기와 그 변화량이 모두 0이 되어 조향의 진입/복귀가 연속이다.
            var progress = Math.Max(0f, Math.Min(1f,
                (vehicle.Distance - vehicle.LaneChangeStartDistance) / vehicle.LaneChangeDistance));
            var smooth = progress * progress * progress * (10f + progress * (-15f + 6f * progress));
            var slope = 30f * progress * progress * (1f - progress) * (1f - progress) / vehicle.LaneChangeDistance;
            var tangent = forward + (targetPosition - position) * slope;
            position += (targetPosition - position) * smooth;
            forward = tangent / RoadLaneMath.Distance(Vector2.zero, tangent);
        }
        vehicle.Position = position;
        vehicle.Forward = forward;
    }

    /// <summary>Unity 네이티브 실행 없이 목표 속도로 한 스텝만큼 접근한다.</summary>
    private static float MoveTowards(float current, float target, float amount)
    {
        return current < target ? Math.Min(target, current + amount) : Math.Max(target, current - amount);
    }
}
