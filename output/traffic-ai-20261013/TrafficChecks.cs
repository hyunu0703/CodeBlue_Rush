using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>실제 교통 런타임 순수 로직을 검사한다. Unity 생명주기/콜라이더/물리 실행을 대체하지 않는다.</summary>
internal static class TrafficChecks
{
    // 누적 판정 수와 테스트에 사용하는 타일 크기다. 차로 폭은 실제 레이아웃 비율에서 계산한다.
    private static int assertions;
    private const float TileSize = 12.54f;

    /// <summary>신호 현시, 정차, 차로 변경, 교차로 충돌 및 무효화 동작을 재현 가능한 조건에서 확인한다.</summary>
    private static void Main()
    {
        Signals_AllPhases_RespectKoreanPermissions();
        Follow_StoppedPlayer_StopsWithGapAndRestarts();
        Follow_BrakingLeader_NoOverlap();
        LaneChange_RearAndSideTraffic_RejectsUnsafeMerge();
        LaneChange_ClearLane_CompletesAndFollowerYields();
        Intersection_RedRight_StopsBeforeYielding();
        Intersection_RedStraightAndLeft_DoNotEnter();
        Intersection_BlockedExit_DoesNotEnter();
        Intersection_ConflictingTraffic_ReservationsStayExclusive();
        Intersection_EightVehicles_NoOverlappingBodies();
        Prefabs_AllRoadsAndRotations_TrafficCompletes();
        Session_PausedOrInvalid_DoesNotMove();
        Startup_SmallSteps_AcceleratesWithoutResettingSpeed();
        LaneChange_VaryingSpeed_PositionAndHeadingStayContinuous();
        LaneChange_StoppedMidway_HoldsPoseAndResumesSmoothly();
        Console.WriteLine($"PASS: {assertions} assertions; 15 traffic scenarios; 13 actual road prefabs x 4 rotations; fixed seeds and controlled steps.");
    }

    /// <summary>각 판정을 누적하고 실패 시 즉시 구체적인 동작 조건을 보고한다.</summary>
    private static void Require(bool condition, string message)
    {
        assertions++;
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    /// <summary>왕복 4차로 직선 5개 또는 사거리와 네 방향 직선 출구를 연결한다.</summary>
    private static RoadLaneNetwork Build(bool intersection, int laneCount = 4)
    {
        Require(RoadLaneLayout.TryCreate(new Vector2(TileSize, TileSize), laneCount, 0, laneCount, 0,
            out var straight, out var error), error);
        var roads = new List<RoadLanePlacement>();
        if (intersection)
        {
            Require(RoadLaneLayout.TryCreate(new Vector2(TileSize, TileSize), laneCount, laneCount, laneCount, laneCount,
                out var cross, out error), error);
            roads.Add(new RoadLanePlacement(cross, Vector2.zero, 0));
            for (var side = 0; side < 4; side++)
            {
                roads.Add(new RoadLanePlacement(straight, RoadLaneMath.Outward((RoadDirection)side) * TileSize, side % 2));
            }
        }
        else
        {
            for (var tile = 0; tile < 5; tile++)
            {
                roads.Add(new RoadLanePlacement(straight, new Vector2(0f, tile * TileSize), 0));
            }
        }
        Require(RoadLaneNetwork.TryBuild(roads, out var network, out error), error);
        return network;
    }

    /// <summary>선택한 경로에 작은 테스트 차체를 배치한다. 차체 크기와 속도는 차로 폭 기준이다.</summary>
    private static RoadTrafficVehicle Add(RoadTrafficSimulation simulation, RoadLanePath path, float distance,
        IReadOnlyList<RoadTrafficObstacle> obstacles = null)
    {
        Require(simulation.TryAddVehicle(path.Id, distance, path.Width * 0.9f, path.Width * 0.55f,
            path.Width * 2.2f, obstacles, out var vehicle), "차량 배치 실패: " + path.Id);
        return vehicle;
    }

    /// <summary>원하는 주행 경로의 중앙선과 방향으로 외부 플레이어/장애물을 만든다.</summary>
    private static RoadTrafficObstacle Obstacle(RoadLanePath path, float along, float speed = 0f)
    {
        path.TrySample(along, out var position, out var forward);
        return new RoadTrafficObstacle(-1, position, forward * speed, forward, path.Width * 0.9f, path.Width * 0.55f);
    }

    /// <summary>8개 현시의 경계와 직진/보호 좌회전/일반 및 전용 우회전 정책을 확인한다.</summary>
    private static void Signals_AllPhases_RespectKoreanPermissions()
    {
        var network = Build(true);
        var signals = new RoadTrafficSignals(network);
        for (var phase = 0; phase < 8; phase++)
        {
            foreach (var movement in network.Paths.Where(p => p.Kind == RoadLanePathKind.Intersection))
            {
                var axis = movement.Entry == RoadDirection.North || movement.Entry == RoadDirection.South;
                var active = axis == (phase < 4) && (movement.Maneuver == RoadLaneManeuver.Left) == (phase % 4 >= 2);
                var light = signals.GetLight(0, movement.Entry, movement.Maneuver);
                Require(light == (!active ? RoadTrafficLight.Red : phase % 2 == 0 ? RoadTrafficLight.Green : RoadTrafficLight.Yellow), "현시 불일치");
                if (movement.Maneuver == RoadLaneManeuver.Right)
                {
                    Require(signals.GetPermission(movement) == (light == RoadTrafficLight.Red ? RoadTrafficPermission.StopThenYield
                        : RoadTrafficPermission.Yield), "일반 우회전 신호");
                    signals.SetDedicatedRightSignal(0, true);
                    Require(signals.GetPermission(movement) == (light == RoadTrafficLight.Green
                        ? RoadTrafficPermission.Proceed : RoadTrafficPermission.Stop), "전용 우회전 신호");
                    signals.SetDedicatedRightSignal(0, false);
                }
                signals.SetCrossingBlocked(0, movement.Exit, true);
                Require(signals.GetPermission(movement) == RoadTrafficPermission.Stop, "보행자 차단");
                signals.SetCrossingBlocked(0, movement.Exit, false);
            }
            // 5초 바로 전까지 유지한 뒤 경계에서 현시가 바뀌는지 확인한다.
            signals.Tick(4.98f);
            signals.Tick(0.02f);
        }
        Require(signals.GetLight(0, RoadDirection.North, RoadLaneManeuver.Straight) == RoadTrafficLight.Green, "40초 주기");
        var fixedClock = new RoadTrafficSignals(network);
        for (var tick = 0; tick < 249; tick++)
        {
            fixedClock.Tick(0.02f);
        }
        Require(fixedClock.GetLight(0, RoadDirection.North, RoadLaneManeuver.Straight) == RoadTrafficLight.Green, "5초 직전 유지");
        fixedClock.Tick(0.02f);
        Require(fixedClock.GetLight(0, RoadDirection.North, RoadLaneManeuver.Straight) == RoadTrafficLight.Yellow, "고정 스텝 5초 경계");
    }

    /// <summary>멈춘 플레이어 뒤에서 충분히 감속/정차하고 장애물이 제거되면 다시 출발하는지 확인한다.</summary>
    private static void Follow_StoppedPlayer_StopsWithGapAndRestarts()
    {
        var network = Build(false, 2);
        var path = network.Paths.First(p => p.RoadOrdinal == 0 && p.Entry == RoadDirection.South);
        var obstacles = new[] { Obstacle(path, 8f) };
        var simulation = new RoadTrafficSimulation(network, 1);
        var car = Add(simulation, path, 2f, obstacles);
        for (var tick = 0; tick < 700; tick++)
        {
            simulation.Tick(0.02f, obstacles);
            Require(car.Distance + car.Length <= 8f - path.Width * 0.59f, "플레이어 차체 침범");
        }
        Require(car.Speed < 0.03f && car.Distance > 5f, "정지 플레이어 뒤 정차");
        var stopped = car.Distance;
        for (var tick = 0; tick < 100; tick++)
        {
            simulation.Tick(0.02f, null);
        }
        Require(car.Distance > stopped + 0.5f, "장애물 제거 후 출발");
    }

    /// <summary>전방 차량이 급정지하는 관측 입력에서 뒷차가 차체를 침범하지 않는지 확인한다.</summary>
    private static void Follow_BrakingLeader_NoOverlap()
    {
        var network = Build(false, 2);
        var path = network.Paths.First(p => p.RoadOrdinal == 0 && p.Entry == RoadDirection.South);
        var simulation = new RoadTrafficSimulation(network, 3);
        var car = Add(simulation, path, 1f);
        var leaderDistance = 5f;
        for (var tick = 0; tick < 900; tick++)
        {
            var leaderSpeed = tick < 180 ? path.Width : 0f;
            leaderDistance += leaderSpeed * 0.02f;
            simulation.Tick(0.02f, new[] { Obstacle(path, leaderDistance, leaderSpeed) });
            Require(car.Distance + car.Length <= leaderDistance + 0.01f, "제동 앞차와 겹침");
        }
        Require(car.Speed < 0.03f, "앞차 정지 추종");
    }

    /// <summary>빠르게 접근하는 목표 차로의 후방 차량과 바로 옆 차량이 있으면 변경을 거부한다.</summary>
    private static void LaneChange_RearAndSideTraffic_RejectsUnsafeMerge()
    {
        var network = Build(false);
        var path = network.Paths.First(p => p.RoadOrdinal == 1 && p.Entry == RoadDirection.South && p.EntryLaneNumber == 1);
        var simulation = new RoadTrafficSimulation(network, 2);
        var rear = new[] { Obstacle(network.Paths[path.RightNeighborId], 2f, 3f) };
        var car = Add(simulation, path, 5f, rear);
        car.Speed = 0.5f;
        Require(!simulation.TryChangeLane(car, false), "빠른 후방차 변경 거부");
        simulation.Tick(0.02f, new[] { Obstacle(network.Paths[path.RightNeighborId], 5f) });
        Require(!simulation.TryChangeLane(car, false), "옆차 변경 거부");
        Require(!simulation.TryChangeLane(car, true), "중앙선 너머 변경 거부");
    }

    /// <summary>안전한 변경이 완료되고 끼어드는 플레이어에 대해서는 동일 차로에 들어오기 전부터 감속하는지 확인한다.</summary>
    private static void LaneChange_ClearLane_CompletesAndFollowerYields()
    {
        var network = Build(false);
        var path = network.Paths.First(p => p.RoadOrdinal == 1 && p.Entry == RoadDirection.South && p.EntryLaneNumber == 1);
        var simulation = new RoadTrafficSimulation(network, 7);
        var car = Add(simulation, path, 2f);
        car.Speed = 0.6f;
        var targetId = path.RightNeighborId;
        Require(simulation.TryChangeLane(car, false), "빈 옆차로 변경 허가");
        for (var tick = 0; tick < 1000 && car.IsChangingLane; tick++)
        {
            simulation.Tick(0.02f, null);
        }
        Require(!car.IsChangingLane && car.PathId == targetId, "차로 변경 완료");
        var second = new RoadTrafficSimulation(network, 7);
        var follower = Add(second, path, 3f);
        follower.Speed = 1.5f;
        path.TrySample(5f, out var ahead, out var forward);
        var right = RoadLaneMath.Right(forward);
        var cutIn = new RoadTrafficObstacle(-1, ahead + right * path.Width, -right * path.Width,
            forward, path.Width, path.Width * 0.55f);
        second.Tick(0.02f, new[] { cutIn });
        Require(follower.Speed < 1.5f, "옆차로 끼어들기 예측 감속");
    }

    /// <summary>진입 방향과 진행 방향을 지정한 교차로 차량을 정지선 바로 앞에 준비한다.</summary>
    private static RoadTrafficVehicle AtLine(RoadTrafficSimulation simulation, RoadLaneNetwork network,
        RoadDirection entry, RoadLaneManeuver maneuver)
    {
        var movement = network.Paths.First(p => p.Kind == RoadLanePathKind.Intersection && p.Entry == entry && p.Maneuver == maneuver);
        var approach = network.Paths.First(p => p.Kind == RoadLanePathKind.Approach && p.Entry == entry && p.EntryLaneNumber == movement.EntryLaneNumber);
        var car = Add(simulation, approach, approach.Length - approach.Width * 0.45f);
        car.NextPathId = movement.Id;
        return car;
    }

    /// <summary>적색 우회전은 실제 정지 확인 전에는 못 들어가고, 대기 후 비어 있는 교차로로 진입한다.</summary>
    private static void Intersection_RedRight_StopsBeforeYielding()
    {
        var network = Build(true);
        var simulation = new RoadTrafficSimulation(network, 9);
        var car = AtLine(simulation, network, RoadDirection.East, RoadLaneManeuver.Right);
        var approachId = car.PathId;
        for (var tick = 0; tick < 10; tick++)
        {
            simulation.Tick(0.02f, null);
            Require(car.PathId == approachId && car.Speed == 0f, "우회전 정지 미준수");
        }
        for (var tick = 0; tick < 100; tick++)
        {
            simulation.Tick(0.02f, null);
        }
        Require(car.PathId != approachId, "적색 우회전 양보 후 진입");
    }

    /// <summary>적색 직진과 좌회전이 선을 넘지 않고 해당 녹색 현시에 출발하는지 확인한다.</summary>
    private static void Intersection_RedStraightAndLeft_DoNotEnter()
    {
        var network = Build(true);
        foreach (var maneuver in new[] { RoadLaneManeuver.Straight, RoadLaneManeuver.Left })
        {
            var simulation = new RoadTrafficSimulation(network, 11);
            var car = AtLine(simulation, network, RoadDirection.East, maneuver);
            var startId = car.PathId;
            for (var tick = 0; tick < 200; tick++)
            {
                simulation.Tick(0.02f, null);
                Require(car.PathId == startId && car.Speed == 0f, "적색 직진/좌회전 선 침범");
            }
            for (var tick = 0; tick < 1800 && car.PathId == startId; tick++)
            {
                simulation.Tick(0.02f, null);
            }
            Require(car.PathId != startId, "보호 신호 출발 실패");
        }
    }

    /// <summary>출구가 막힌 녹색 직진 차량이 정지선을 넘지 않는지 확인한다.</summary>
    private static void Intersection_BlockedExit_DoesNotEnter()
    {
        var network = Build(true);
        var simulation = new RoadTrafficSimulation(network, 10);
        var car = AtLine(simulation, network, RoadDirection.North, RoadLaneManeuver.Straight);
        var startId = car.PathId;
        var movement = network.Paths[car.NextPathId];
        var obstacles = new[] { Obstacle(network.Paths[movement.Successors[0]], 0.4f) };
        for (var tick = 0; tick < 200; tick++)
        {
            simulation.Tick(0.02f, obstacles);
            Require(car.PathId == startId, "출구 정체 꼬리물기");
        }
    }

    /// <summary>직진/우회전이 경쟁하는 두 차량과 현시 전환에서 교차로 예약이 동시에 충돌하지 않는지 확인한다.</summary>
    private static void Intersection_ConflictingTraffic_ReservationsStayExclusive()
    {
        var network = Build(true);
        var simulation = new RoadTrafficSimulation(network, 18);
        var straight = AtLine(simulation, network, RoadDirection.North, RoadLaneManeuver.Straight);
        var right = AtLine(simulation, network, RoadDirection.East, RoadLaneManeuver.Right);
        for (var tick = 0; tick < 2000; tick++)
        {
            simulation.Tick(0.02f, null);
            if (straight.ReservedMovement >= 0 && right.ReservedMovement >= 0)
            {
                Require(!network.Paths[straight.ReservedMovement].Conflicts.Contains(right.ReservedMovement), "예약 충돌");
            }
        }
        Require(straight.IsFinished && right.IsFinished, "교차로 통과/예약 해제 교착");
    }

    /// <summary>여덟 차량의 긴 차체를 동시에 배치하고 실제 직사각형 분리축으로 충돌/예약/진행을 검사한다.</summary>
    private static void Intersection_EightVehicles_NoOverlappingBodies()
    {
        foreach (var count in new[] { 4, 6 })
        {
            var network = Build(true, count);
            var simulation = new RoadTrafficSimulation(network, 55);
            var cars = new List<RoadTrafficVehicle>();
            foreach (var path in network.Paths.Where(p => p.Kind == RoadLanePathKind.Approach))
            {
                var car = Add(simulation, path, path.Length - path.Width * 0.7f);
                cars.Add(car);
            }
            for (var tick = 0; tick < 6000; tick++)
            {
                simulation.Tick(0.02f, null);
                for (var a = 0; a < cars.Count; a++)
                {
                    for (var b = a + 1; b < cars.Count; b++)
                    {
                        if (cars[a].IsFinished || cars[b].IsFinished)
                        {
                            continue;
                        }
                        var carA = cars[a];
                        var carB = cars[b];
                        Require(!Overlaps(carA, carB), "동시 차량 차체 충돌: " + carA.PathId + "/" + carB.PathId);
                        if (carA.ReservedMovement >= 0 && carB.ReservedMovement >= 0)
                        {
                            Require(carA.ReservedMovement != carB.ReservedMovement
                                && !network.Paths[carA.ReservedMovement].Conflicts.Contains(carB.ReservedMovement), "다중 예약 충돌");
                        }
                    }
                }
            }
            Require(cars.All(car => car.IsFinished), "다중 차량 교착");
        }
    }

    /// <summary>두 차량의 직사각형 차체가 겹치는지 네 개 분리축으로 판정한다.</summary>
    private static bool Overlaps(RoadTrafficVehicle a, RoadTrafficVehicle b)
    {
        var axes = new[] { a.Forward, RoadLaneMath.Right(a.Forward), b.Forward, RoadLaneMath.Right(b.Forward) };
        var offset = b.Position - a.Position;
        foreach (var axis in axes)
        {
            var spanA = (Math.Abs(RoadLaneMath.Dot(a.Forward, axis)) * a.Length
                + Math.Abs(RoadLaneMath.Dot(RoadLaneMath.Right(a.Forward), axis)) * a.Width) * 0.5f;
            var spanB = (Math.Abs(RoadLaneMath.Dot(b.Forward, axis)) * b.Length
                + Math.Abs(RoadLaneMath.Dot(RoadLaneMath.Right(b.Forward), axis)) * b.Width) * 0.5f;
            if (Math.Abs(RoadLaneMath.Dot(offset, axis)) >= spanA + spanB)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>실제 도로 13종의 직렬화 규격을 읽기만 하고 네 방향 회전 배치의 주행/교차로 예약을 검사한다.</summary>
    private static void Prefabs_AllRoadsAndRotations_TrafficCompletes()
    {
        var files = Directory.GetFiles("Assets/Prefabs/Road", "*.prefab");
        Require(files.Length == 13, "실제 도로 프리팹 개수");
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var size = Regex.Match(text, @"imageSize: \{x: ([0-9.]+), y: ([0-9.]+)\}");
            var dimensions = new Vector2(float.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture),
                float.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture));
            Require(RoadLaneLayout.TryCreate(dimensions, ReadInt(text, "northLaneCount"), ReadInt(text, "eastLaneCount"),
                ReadInt(text, "southLaneCount"), ReadInt(text, "westLaneCount"), out var layout, out var error), error);
            for (var rotation = 0; rotation < 4; rotation++)
            {
                Require(RoadLaneNetwork.TryBuild(new[] { new RoadLanePlacement(layout, Vector2.zero, rotation) },
                    out var network, out error), error);
                var simulation = new RoadTrafficSimulation(network, rotation + 91);
                foreach (var path in network.Paths.Where(p => p.StartsAtBoundary))
                {
                    Add(simulation, path, path.Length * 0.5f);
                }
                for (var tick = 0; tick < 6000; tick++)
                {
                    simulation.Tick(0.02f, null);
                    foreach (var car in simulation.Vehicles)
                    {
                        Require(RoadLaneMath.IsFinite(car.Position) && car.Speed >= 0f, "프리팹 주행 유한 좌표");
                    }
                    if (simulation.Vehicles.All(car => car.IsFinished))
                    {
                        break;
                    }
                }
                Require(simulation.Vehicles.All(car => car.IsFinished), "프리팹 주행 교착: " + file + "/" + rotation);
            }
        }
    }

    /// <summary>프리팹에서 필요한 정수 필드를 읽는다. 누락된 값을 추정하지 않는다.</summary>
    private static int ReadInt(string text, string field)
    {
        var match = Regex.Match(text, @"(?m)^  " + field + @": (\d+)\s*$");
        Require(match.Success, "도로 프리팹 필드 누락: " + field);
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>일시정지와 맵 스냅숏 무효화 시 차량이 그대로 유지되는지 확인한다.</summary>
    private static void Session_PausedOrInvalid_DoesNotMove()
    {
        var network = Build(false);
        var simulation = new RoadTrafficSimulation(network, 22);
        var car = Add(simulation, network.Paths[0], 3f);
        var distance = car.Distance;
        simulation.Tick(0f, null);
        Require(car.Distance == distance, "0초 이동");
        network.Invalidate();
        simulation.Tick(0.02f, null);
        Require(car.Distance == distance, "무효 맵 이동");
        Require(!simulation.TryAddVehicle(0, 3f, 0.5f, 0.3f, 1f, null, out _), "무효 맵 생성");
    }

    /// <summary>생성 직후 작은 고정 스텝에서 정상 가속이 매번 0으로 초기화되지 않는지 확인한다.</summary>
    private static void Startup_SmallSteps_AcceleratesWithoutResettingSpeed()
    {
        foreach (var seconds in new[] { 0.005f, 0.01f, 0.02f, 0.033333335f })
        {
            var network = Build(false, 2);
            var path = network.Paths.First(p => p.RoadOrdinal == 1 && p.Entry == RoadDirection.South);
            var simulation = new RoadTrafficSimulation(network, 104);
            var car = Add(simulation, path, 2f);
            var start = car.Distance;
            var previousSpeed = car.Speed;
            for (var tick = 0; tick < 30; tick++)
            {
                simulation.Tick(seconds, null);
                Require(car.Speed > previousSpeed, "정상 출발 중 속도 초기화: " + seconds);
                Require(car.Distance > start, "출발 후 위치 정지: " + seconds);
                previousSpeed = car.Speed;
            }
            var expectedSpeed = path.Width * 0.9f * seconds * 30;
            Require(Math.Abs(car.Speed - expectedSpeed) < 0.00001f, "출발 가속량 불일치");
        }
    }

    /// <summary>저속/고속 및 양쪽 차로 변경에서 프레임별 위치/차체 각도가 연속이고 목표 차로 중앙에 도달하는지 확인한다.</summary>
    private static void LaneChange_VaryingSpeed_PositionAndHeadingStayContinuous()
    {
        foreach (var speed in new[] { 0.06f, 1.6f })
        {
            foreach (var toLeft in new[] { false, true })
            {
                var network = Build(false);
                var path = network.Paths.First(p => p.RoadOrdinal == 1 && p.Entry == RoadDirection.South
                    && p.EntryLaneNumber == (toLeft ? 2 : 1));
                var simulation = new RoadTrafficSimulation(network, 51);
                var car = Add(simulation, path, 2f);
                car.Speed = speed;
                var target = toLeft ? path.LeftNeighborId : path.RightNeighborId;
                var initialForward = car.Forward;
                Require(simulation.TryChangeLane(car, toLeft), "완만한 차로 변경 시작 실패");
                for (var tick = 0; tick < 1200 && car.IsChangingLane; tick++)
                {
                    var previousPosition = car.Position;
                    var previousForward = car.Forward;
                    simulation.Tick(0.02f, null);
                    Require(RoadLaneMath.Distance(car.Position, previousPosition) < 0.05f, "차로 변경 위치 순간 이동");
                    Require(AngleBetween(previousForward, car.Forward) < 1f, "차로 변경 프레임별 급회전");
                    Require(AngleBetween(initialForward, car.Forward) < 20f, "차로 변경 조향 각도 과다");
                    if (tick == 0 || !car.IsChangingLane)
                    {
                        Require(AngleBetween(initialForward, car.Forward) < 0.1f, "차로 변경 시작/완료 회전 불연속");
                    }
                }
                Require(!car.IsChangingLane && car.PathId == target, "완만한 변경 완료 실패");
                network.Paths[target].TryProject(car.Position, out _, out var squaredOffset);
                Require(squaredOffset < 0.000001f, "목표 차로 중앙 배치 실패");
            }
        }
    }

    /// <summary>변경 도중 양쪽 차로의 앞차 때문에 정차하면 횡이동/차체 회전을 유지하고 제거 후 연속적으로 재출발하는지 확인한다.</summary>
    private static void LaneChange_StoppedMidway_HoldsPoseAndResumesSmoothly()
    {
        var network = Build(false);
        var path = network.Paths.First(p => p.RoadOrdinal == 1 && p.Entry == RoadDirection.South && p.EntryLaneNumber == 1);
        var simulation = new RoadTrafficSimulation(network, 63);
        var car = Add(simulation, path, 2f);
        car.Speed = 1f;
        Require(simulation.TryChangeLane(car, false), "정차 검사 변경 시작 실패");
        for (var tick = 0; tick < 50; tick++)
        {
            simulation.Tick(0.02f, null);
        }
        Require(car.IsChangingLane, "정차 검사 전에 변경 종료");
        var beforePosition = car.Position;
        var beforeForward = car.Forward;
        // 앞차는 차로별로 같은 종방향 위치에 두어 양쪽 경로에서 제동 조건을 만든다.
        var blockerDistance = car.Distance + car.Length + path.Width * 0.6f - 0.0001f;
        var blockers = new[] { Obstacle(path, blockerDistance), Obstacle(network.Paths[car.TargetLaneId], blockerDistance) };
        for (var tick = 0; tick < 100; tick++)
        {
            simulation.Tick(0.02f, blockers);
            Require(car.Speed == 0f, "변경 중 정차 실패");
            Require(RoadLaneMath.Distance(beforePosition, car.Position) < 0.00001f, "정차 중 횡이동");
            Require(AngleBetween(beforeForward, car.Forward) < 0.001f, "정차 중 차체 방향 급변");
        }
        Require(car.IsChangingLane, "정차 중 강제 변경 완료");
        for (var tick = 0; tick < 1000 && car.IsChangingLane; tick++)
        {
            var previousPosition = car.Position;
            var previousForward = car.Forward;
            simulation.Tick(0.02f, null);
            Require(RoadLaneMath.Distance(previousPosition, car.Position) < 0.05f, "변경 재개 위치 순간 이동");
            Require(AngleBetween(previousForward, car.Forward) < 1f, "변경 재개 급회전");
        }
        Require(!car.IsChangingLane, "변경 재개 완료 실패");
    }

    /// <summary>네이티브 Unity 호출 없이 두 차체 방향의 각도 차이를 도 단위로 구한다.</summary>
    private static float AngleBetween(Vector2 a, Vector2 b)
    {
        var cross = a.x * b.y - a.y * b.x;
        return (float)(Math.Abs(Math.Atan2(cross, RoadLaneMath.Dot(a, b))) * 180d / Math.PI);
    }
}
