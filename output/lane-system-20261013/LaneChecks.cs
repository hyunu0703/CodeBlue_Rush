using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>실제 런타임 차로 코드와 프리팹 데이터를 사용한다. Unity 생명주기나 물리는 실행하지 않는다.</summary>
internal static class LaneChecks
{
    // 검사 횟수와 실제 에셋에서 읽은 레이아웃이다. 고정 난수로 시드 표본을 재현한다.
    private static int assertions;
    private static readonly Dictionary<RoadIndex, RoadLaneLayout> layouts = new Dictionary<RoadIndex, RoadLaneLayout>();
    private static readonly Dictionary<RoadIndex, string> names = new Dictionary<RoadIndex, string>();

    /// <summary>13종 프리팹, 네 방향 경계, 혼합 교차로와 두 단계 생성 표본을 검사한다.</summary>
    private static void Main()
    {
        // 실행 디렉터리는 프로젝트 루트이며 에셋/메타데이터는 읽기만 한다.
        foreach (var file in Directory.GetFiles("Assets/Prefabs/Road", "*.prefab").OrderBy(p => p))
        {
            var text = File.ReadAllText(file);
            var index = (RoadIndex)ReadInt(text, "index");
            var size = Regex.Match(text, @"imageSize: \{x: ([0-9.]+), y: ([0-9.]+)\}");
            var dimensions = new Vector2(float.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture),
                float.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture));
            Require(RoadLaneLayout.TryCreate(dimensions, ReadInt(text, "northLaneCount"), ReadInt(text, "eastLaneCount"),
                ReadInt(text, "southLaneCount"), ReadInt(text, "westLaneCount"), out var layout, out var error), file + ": " + error);
            layouts.Add(index, layout);
            names.Add(index, Path.GetFileNameWithoutExtension(file));
        }
        Require(layouts.Count == 13, "13종 프리팹 누락");
        CheckLayouts();
        var boundaryCases = CheckRotatedBoundaries();
        CheckConflictsAndQueries();
        CheckInvalidInputs();
        var seedPairs = CheckSeedPairs();
        ExportPaths();
        Console.WriteLine($"PASS: {assertions} assertions; 13 prefabs; {boundaryCases} rotated boundary cases; {seedPairs} pickup/transport seed pairs.");
    }

    /// <summary>프리팹의 정수 필드를 읽는다. 누락된 값을 0으로 추측하지 않는다.</summary>
    private static int ReadInt(string text, string field)
    {
        var match = Regex.Match(text, @"(?m)^  " + field + @": (\d+)\s*$");
        Require(match.Success, "프리팹 필드 누락: " + field);
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>주행 방향, 회전 가능한 차로, 정지선과 보도의 경계를 모든 경로에서 검사한다.</summary>
    private static void CheckLayouts()
    {
        foreach (var pair in layouts)
        {
            var layout = pair.Value;
            foreach (var path in layout.Paths)
            {
                Require(path.Length > 0f, "길이 없는 경로");
                Require(path.TrySample(0f, out var start, out var forward), "시작 샘플 실패");
                Require(path.TrySample(path.Length, out var end, out var endForward), "끝 샘플 실패");
                Require(!path.TrySample(float.NaN, out _, out _), "NaN 거리 허용");
                Require(path.TrySample(-1f, out var clampedStart, out _) && Near(start, clampedStart), "시작 제한 오류");
                Require(path.TrySample(path.Length + 1f, out var clampedEnd, out _) && Near(end, clampedEnd), "끝 제한 오류");
                Require(path.TryProject(start, out var projectedStart, out var offset) && projectedStart < 0.001f && offset < 0.000001f,
                    "경로 투영 오류");
                if (path.StartsAtBoundary)
                {
                    var outward = RoadLaneMath.Outward(path.Entry);
                    Require(RoadLaneMath.Dot(forward, -outward) > 0.999f, "역주행 진입");
                    Require(Math.Abs(RoadLaneMath.Dot(start, RoadLaneMath.Right(-outward))
                        - (path.EntryLaneNumber - 0.5f) * layout.LaneWidth) < 0.0001f, "우측통행 진입 오프셋");
                }
                if (path.EndsAtBoundary)
                {
                    var outward = RoadLaneMath.Outward(path.Exit);
                    Require(RoadLaneMath.Dot(endForward, outward) > 0.999f, "역주행 출발");
                    Require(Math.Abs(RoadLaneMath.Dot(end, RoadLaneMath.Right(outward))
                        - (path.ExitLaneNumber - 0.5f) * layout.LaneWidth) < 0.0001f, "우측통행 출발 오프셋");
                }
                if (path.HasStopLine)
                {
                    var perpendicular = (int)path.Entry % 2 == 0
                        ? Math.Max(layout.GetLaneCount(RoadDirection.East), layout.GetLaneCount(RoadDirection.West))
                        : Math.Max(layout.GetLaneCount(RoadDirection.North), layout.GetLaneCount(RoadDirection.South));
                    // 이미지 정지선의 바깥쪽 끝에 맞아야 한다. 차량 앞범퍼 보정은 향후 AI의 몫이다.
                    Require(Math.Abs(RoadLaneMath.Dot(end, RoadLaneMath.Outward(path.Entry))
                        - (perpendicular * layout.LaneWidth * 0.5f + 1.208f)) < 0.0001f, "정지선 위치 오류");
                    Require(!path.CanChangeLaneAt(path.Length), "정지선에서 차로 변경 허용");
                }
                if (path.Kind == RoadLanePathKind.Intersection)
                {
                    Require(path.Entry != path.Exit, "유턴 연결 생성");
                    Require(path.LeftNeighborId < 0 && path.RightNeighborId < 0 && !path.CanChangeLaneAt(path.Length * 0.5f),
                        "교차로 차로 변경 허용");
                    Require(layout.GetLaneCount(path.Exit) > 0, "막힌 출구에 연결");
                    Require(path.Maneuver != RoadLaneManeuver.Left || (path.EntryLaneNumber == 1 && path.ExitLaneNumber == 1),
                        "좌회전 차로 오류");
                    Require(path.Maneuver != RoadLaneManeuver.Right || (path.EntryLaneNumber == layout.GetLaneCount(path.Entry) / 2
                        && path.ExitLaneNumber == layout.GetLaneCount(path.Exit) / 2), "우회전 차로 오류");
                    Require(path.Maneuver != RoadLaneManeuver.Straight || path.EntryLaneNumber == path.ExitLaneNumber,
                        "직진 차로 번호 변경");
                }
                foreach (var point in path.Points)
                {
                    Require(IsOnAsphalt(layout, point), $"{pair.Key} 경로 {path.Id}: 보도 침범 {point.x}/{point.y}");
                }
                foreach (var id in path.Successors)
                {
                    Require(Near(end, layout.Paths[id].Points[0]), "도로 내부 경로 단절");
                }
                if (path.LeftNeighborId >= 0)
                {
                    Require(layout.Paths[path.LeftNeighborId].RightNeighborId == path.Id, "비대칭 차로 인접 정보");
                }
            }
            // 출구가 있는 모든 진입 차로에서 승인된 진행 방향을 정확히 제공해야 한다.
            foreach (var approach in layout.Paths.Where(p => p.Kind == RoadLanePathKind.Approach))
            {
                var available = approach.Successors.Select(id => layout.Paths[id]).ToArray();
                Require(available.Count(p => p.Maneuver == RoadLaneManeuver.Straight)
                    == (layout.GetLaneCount(RoadLaneMath.Rotate(approach.Entry, 2)) > 0 ? 1 : 0), "직진 연결 누락");
                Require(available.Count(p => p.Maneuver == RoadLaneManeuver.Left)
                    == (approach.EntryLaneNumber == 1 && layout.GetLaneCount(RoadLaneMath.Rotate(approach.Entry, 1)) > 0 ? 1 : 0),
                    "좌회전 연결 누락");
                Require(available.Count(p => p.Maneuver == RoadLaneManeuver.Right)
                    == (approach.EntryLaneNumber == layout.GetLaneCount(approach.Entry) / 2
                        && layout.GetLaneCount(RoadLaneMath.Rotate(approach.Entry, 3)) > 0 ? 1 : 0), "우회전 연결 누락");
            }
        }
        var middle = layouts[RoadIndex.T6Lane].Paths.Single(p => p.Kind == RoadLanePathKind.Approach
            && p.Entry == RoadDirection.South && p.EntryLaneNumber == 2);
        Require(middle.Successors.Count == 0 && middle.LeftNeighborId >= 0 && middle.RightNeighborId >= 0,
            "6차로 T 가운데 차로는 사전 차로 변경이 필요함");
    }

    /// <summary>기존 도로 도색의 직선 도로 영역과 교차로의 모서리 절삭을 판정한다.</summary>
    private static bool IsOnAsphalt(RoadLaneLayout layout, Vector2 point)
    {
        // 통행면의 반폭과 원본 이미지의 보도 모서리 절삭 길이다.
        var halfVertical = Math.Max(layout.GetLaneCount(RoadDirection.North), layout.GetLaneCount(RoadDirection.South)) * layout.LaneWidth * 0.5f;
        var halfHorizontal = Math.Max(layout.GetLaneCount(RoadDirection.East), layout.GetLaneCount(RoadDirection.West)) * layout.LaneWidth * 0.5f;
        var epsilon = 0.0001f;
        if (Math.Abs(point.x) <= halfVertical + epsilon && ((point.y >= 0f && layout.GetLaneCount(RoadDirection.North) > 0)
            || (point.y <= 0f && layout.GetLaneCount(RoadDirection.South) > 0)))
        {
            return true;
        }
        if (Math.Abs(point.y) <= halfHorizontal + epsilon && ((point.x >= 0f && layout.GetLaneCount(RoadDirection.East) > 0)
            || (point.x <= 0f && layout.GetLaneCount(RoadDirection.West) > 0)))
        {
            return true;
        }
        return layout.IsIntersection && Math.Abs(point.x) + Math.Abs(point.y) <= halfVertical + halfHorizontal + 0.368f
            && layout.GetLaneCount(point.x >= 0f ? RoadDirection.East : RoadDirection.West) > 0
            && layout.GetLaneCount(point.y >= 0f ? RoadDirection.North : RoadDirection.South) > 0;
    }

    /// <summary>13종의 모든 열린 가장자리를 네 방향으로 회전해 일자 도로와 왕복으로 연결한다.</summary>
    private static int CheckRotatedBoundaries()
    {
        var cases = 0;
        foreach (var layout in layouts.Values)
        {
            for (var turns = 0; turns < 4; turns++)
            {
                for (var sideIndex = 0; sideIndex < 4; sideIndex++)
                {
                    var side = (RoadDirection)sideIndex;
                    var count = layout.GetLaneCount(side);
                    if (count == 0)
                    {
                        continue;
                    }
                    var worldSide = RoadLaneMath.Rotate(side, turns);
                    var straight = layouts[count == 2 ? RoadIndex.Straight2Lane : count == 4 ? RoadIndex.Straight4Lane : RoadIndex.Straight6Lane];
                    var neighborTurns = (int)worldSide % 2;
                    var roads = new[] { new RoadLanePlacement(layout, Vector2.zero, turns),
                        new RoadLanePlacement(straight, RoadLaneMath.Outward(worldSide) * layout.ImageSize.x, neighborTurns) };
                    Require(RoadLaneNetwork.TryBuild(roads, out var graph, out var error), "회전 경계: " + error);
                    Require(graph.ConnectedBoundaryCount == 1, "도로 경계 연결 개수 오류");
                    Require(graph.Paths.Sum(p => p.Successors.Count(id => graph.Paths[id].RoadOrdinal != p.RoadOrdinal)) == count,
                        "왕복 차로 연결 개수 오류");
                    CheckGraphContinuity(graph);
                    cases++;
                }
            }
        }
        return cases;
    }

    /// <summary>진행 연결의 위치와 차로 번호를 검사한다. 그래프의 읽기 전용 데이터만 사용한다.</summary>
    private static void CheckGraphContinuity(RoadLaneNetwork graph)
    {
        foreach (var path in graph.Paths)
        {
            foreach (var next in path.Successors)
            {
                var target = graph.Paths[next];
                Require(Near(path.Points[path.Points.Count - 1], target.Points[0]), "차로 경로 경계 단절");
                Require(path.ExitLaneNumber == target.EntryLaneNumber, "후속 차로 번호 불일치");
                path.TrySample(path.Length, out _, out var exit);
                target.TrySample(0f, out _, out var entry);
                Require(RoadLaneMath.Dot(exit, entry) > 0.998f, "경로 경계의 주행 방향 불일치");
            }
        }
    }

    /// <summary>교차, 합류, 경로 탐색, 진행 방향 필터와 이전 스냅숏 무효화를 검사한다.</summary>
    private static void CheckConflictsAndQueries()
    {
        Require(RoadLaneNetwork.TryBuild(new[] { new RoadLanePlacement(layouts[RoadIndex.Cross4Lane], Vector2.zero, 0) },
            out var graph, out _), "교차로 그래프 생성");
        var north = graph.Paths.Single(p => p.Kind == RoadLanePathKind.Intersection && p.Entry == RoadDirection.North
            && p.Maneuver == RoadLaneManeuver.Straight && p.EntryLaneNumber == 1);
        var east = graph.Paths.Single(p => p.Kind == RoadLanePathKind.Intersection && p.Entry == RoadDirection.East
            && p.Maneuver == RoadLaneManeuver.Straight && p.EntryLaneNumber == 1);
        Require(north.Conflicts.Contains(east.Id), "십자 직진 경로 충돌 누락");
        // 멀리 배치된 도로에서도 같은 방향의 평행 차로는 동시에 통행 가능한 경로다.
        Require(RoadLaneNetwork.TryBuild(new[] { new RoadLanePlacement(layouts[RoadIndex.Cross6Lane],
            new Vector2(150.48f, -125.4f), 3) }, out var shifted, out _), "이동한 교차로 생성");
        foreach (var path in shifted.Paths.Where(p => p.Kind == RoadLanePathKind.Intersection && p.Maneuver == RoadLaneManeuver.Straight))
        {
            foreach (var parallel in shifted.Paths.Where(p => p.Kind == RoadLanePathKind.Intersection
                && p.Maneuver == RoadLaneManeuver.Straight && p.Entry == path.Entry && p.Id != path.Id))
            {
                Require(!path.Conflicts.Contains(parallel.Id), "평행 직진 차로의 잘못된 충돌 정보");
            }
        }
        foreach (var path in graph.Paths)
        {
            foreach (var conflict in path.Conflicts)
            {
                Require(graph.Paths[conflict].Conflicts.Contains(path.Id), "비대칭 충돌 정보");
            }
        }
        var approach = graph.Paths.Single(p => p.Kind == RoadLanePathKind.Approach && p.Entry == RoadDirection.North && p.EntryLaneNumber == 1);
        var departure = graph.Paths.Single(p => p.Kind == RoadLanePathKind.Departure && p.Exit == RoadDirection.East && p.ExitLaneNumber == 1);
        var route = new List<int>();
        Require(graph.TryFindRoute(approach.Id, departure.Id, route) && route.Count == 3
            && graph.Paths[route[1]].Maneuver == RoadLaneManeuver.Left, "좌회전 경로 탐색 실패");
        Require(!graph.TryFindRoute(departure.Id, approach.Id, route) && route.Count == 0, "역방향/유턴 경로 탐색 허용");
        Require(graph.TryFindNearest(approach.Points[0], Vector2.down, 0.1f, out var nearest, out var distance)
            && nearest == approach.Id && distance < 0.001f, "주행 방향 근접 검색 오류");
        Require(!graph.TryFindNearest(approach.Points[0], Vector2.up, 0.1f, out _, out _), "역주행 방향 근접 검색 허용");
        graph.Invalidate();
        Require(!graph.TryFindRoute(0, 1, route) && !graph.TryFindNearest(Vector2.zero, Vector2.zero, 1f, out _, out _),
            "이전 그래프 사용 허용");
    }

    /// <summary>불일치한 도로 면과 겹침은 네트워크 공개 전에 실패해야 한다.</summary>
    private static void CheckInvalidInputs()
    {
        Require(!RoadLaneLayout.TryCreate(new Vector2(12.54f, 12.54f), 3, 0, 3, 0, out _, out _), "홀수 왕복 차로 허용");
        Require(!RoadLaneLayout.TryCreate(new Vector2(float.NaN, 12.54f), 2, 0, 2, 0, out _, out _), "NaN 크기 허용");
        Require(!RoadLaneLayout.TryCreate(new Vector2(12.54f, 12.54f), 2, 0, 4, 0, out _, out _), "직진 폭 변경 허용");
        var straight = layouts[RoadIndex.Straight2Lane];
        Require(!RoadLaneNetwork.TryBuild(new[] { new RoadLanePlacement(straight, Vector2.zero, 0),
            new RoadLanePlacement(layouts[RoadIndex.Straight4Lane], new Vector2(0f, 12.54f), 0) }, out _, out _), "경계 폭 불일치 허용");
        Require(!RoadLaneNetwork.TryBuild(new[] { new RoadLanePlacement(straight, Vector2.zero, 0),
            new RoadLanePlacement(layouts[RoadIndex.T2Lane], new Vector2(0f, 12.54f), 2) }, out _, out _), "닫힌 면에 연결 허용");
        Require(!RoadLaneNetwork.TryBuild(new[] { new RoadLanePlacement(straight, Vector2.zero, 0),
            new RoadLanePlacement(straight, new Vector2(0f, 1f), 0) }, out _, out _), "구역 겹침 허용");
    }

    /// <summary>시드 계획과 실제 프리팹의 추가 분기까지 포함해 픽업/후송 두 단계 연결을 검사한다.</summary>
    private static int CheckSeedPairs()
    {
        // 실제 미션의 시드 파생 난수를 사용한다. 최소 100개 연결 표본과 네 출구 방향을 확인한다.
        var random = new System.Random(20261013);
        var accepted = 0;
        var rejected = 0;
        var seenDirections = new HashSet<RoadDirection>();
        var seenWidths = new HashSet<int>();
        var oldIdsChecked = 0;
        var records = new List<object>();
        for (var trial = 0; trial < 3000 && accepted < 100; trial++)
        {
            var seed = MissionSeedRules.CreateSeed(random);
            if (!MissionSeedRules.TryBuildRoute(seed, out var pickup, out _))
            {
                continue;
            }
            var pickupRoads = PlacePlan(pickup, Array.Empty<RoadLanePlacement>());
            if (pickupRoads == null || !RoadLaneNetwork.TryBuild(pickupRoads, out var original, out _))
            {
                rejected++;
                continue;
            }
            var nextRandom = new System.Random(seed ^ 0x5F3759DF);
            for (var attempt = 0; attempt < 1024; attempt++)
            {
                var candidate = MissionSeedRules.CreateSeed(nextRandom);
                if (!MissionSeedRules.TryBuildTransportRoute(seed, candidate, out var transport, out _))
                {
                    continue;
                }
                var transportRoads = PlacePlan(transport, pickupRoads);
                if (transportRoads == null)
                {
                    continue;
                }
                var combined = pickupRoads.Concat(transportRoads).ToArray();
                if (!RoadLaneNetwork.TryBuild(combined, out var appended, out _))
                {
                    continue;
                }
                CheckGraphContinuity(appended);
                foreach (var path in original.Paths)
                {
                    var same = appended.Paths[path.Id];
                    Require(path.RoadOrdinal == same.RoadOrdinal && path.Entry == same.Entry && path.Exit == same.Exit
                        && path.Points.SequenceEqual(same.Points), "후송 추가가 기존 차로 Id/기하를 변경함");
                    oldIdsChecked++;
                }
                // 픽업 마지막 도로 -> 후송 첫 도로로 모든 편도 차로가 끊기지 않고 이어져야 한다.
                var lastOrdinal = pickupRoads.Count - 1;
                var lastPiece = pickup[pickup.Length - 1];
                var firstOrdinal = FindRoadOrdinal(appended, transport[0].GridX, transport[0].GridY);
                var crossings = appended.Paths.Where(p => p.RoadOrdinal == lastOrdinal && p.EndsAtBoundary && p.Exit == lastPiece.Outgoing)
                    .Sum(p => p.Successors.Count(id => appended.Paths[id].RoadOrdinal == firstOrdinal));
                Require(crossings == lastPiece.ExitLanes / 2, "픽업->후송 차로 단절");
                var reverseCrossings = appended.Paths.Where(p => p.RoadOrdinal == firstOrdinal && p.EndsAtBoundary
                    && p.Exit == RoadLaneMath.Rotate(lastPiece.Outgoing, 2))
                    .Sum(p => p.Successors.Count(id => appended.Paths[id].RoadOrdinal == lastOrdinal));
                Require(reverseCrossings == lastPiece.ExitLanes / 2, "후송->픽업 차로 단절");
                seenDirections.Add(lastPiece.Outgoing);
                seenWidths.Add(lastPiece.ExitLanes);
                records.Add(new { pickupSeed = seed, transportSeed = candidate, paths = appended.Paths.Count,
                    boundaries = appended.ConnectedBoundaryCount, crossingLanes = crossings });
                accepted++;
                break;
            }
        }
        Require(accepted == 100 && seenDirections.Count == 4 && seenWidths.Count == 3, "시드 표본/방향/폭 커버리지 부족");
        File.WriteAllText("output/lane-system-20261013/seed-results.json", JsonSerializer.Serialize(new
        { accepted, rejected, oldIdsChecked, records }, new JsonSerializerOptions { WriteIndented = true }));
        return accepted;
    }

    /// <summary>시드의 실제 절대 격자 위치로 프리팹을 배치하고 미선택 출구에 직선 두 개씩 추가한다.</summary>
    private static List<RoadLanePlacement> PlacePlan(MissionRoadPiece[] plan, IReadOnlyList<RoadLanePlacement> existing)
    {
        // 주행 도로와 추가 분기를 분리해 실제 생성기와 같이 분기부터 활성 목록에 넣는다.
        var main = new List<RoadLanePlacement>();
        var branches = new List<RoadLanePlacement>();
        var occupied = new HashSet<(int, int)>();
        foreach (var placement in existing)
        {
            occupied.Add(((int)Math.Round(placement.Position.x / 12.54f), (int)Math.Round(placement.Position.y / 12.54f)));
        }
        foreach (var piece in plan)
        {
            if (!occupied.Add((piece.GridX, piece.GridY)))
            {
                return null;
            }
            var layout = layouts[piece.Index];
            var turns = FindRotation(layout, piece);
            Require(turns >= 0, "프리팹의 시드 회전 매칭 실패");
            main.Add(new RoadLanePlacement(layout, new Vector2(piece.GridX * 12.54f, piece.GridY * 12.54f), turns));
        }
        for (var i = 0; i < plan.Length; i++)
        {
            var piece = plan[i];
            var road = main[i];
            if (!road.Layout.IsIntersection)
            {
                continue;
            }
            for (var sideIndex = 0; sideIndex < 4; sideIndex++)
            {
                var side = (RoadDirection)sideIndex;
                var count = road.Layout.GetLaneCount(RoadLaneMath.Rotate(side, -road.ClockwiseQuarterTurns));
                if (count == 0 || side == RoadLaneMath.Rotate(piece.Incoming, 2) || side == piece.Outgoing)
                {
                    continue;
                }
                var straight = layouts[count == 2 ? RoadIndex.Straight2Lane : count == 4 ? RoadIndex.Straight4Lane : RoadIndex.Straight6Lane];
                var outward = RoadLaneMath.Outward(side);
                for (var step = 1; step <= 2; step++)
                {
                    var gridX = piece.GridX + (int)outward.x * step;
                    var gridY = piece.GridY + (int)outward.y * step;
                    if (!occupied.Add((gridX, gridY)))
                    {
                        return null;
                    }
                    branches.Add(new RoadLanePlacement(straight, new Vector2(gridX * 12.54f, gridY * 12.54f), sideIndex % 2));
                }
            }
        }
        branches.AddRange(main);
        return branches;
    }

    /// <summary>시드의 입구/출구 차로 수가 맞는 첫 회전을 찾는다.</summary>
    private static int FindRotation(RoadLaneLayout layout, MissionRoadPiece piece)
    {
        for (var turns = 0; turns < 4; turns++)
        {
            if (layout.GetLaneCount(RoadLaneMath.Rotate(piece.Incoming, 2 - turns)) == piece.EntryLanes
                && layout.GetLaneCount(RoadLaneMath.Rotate(piece.Outgoing, -turns)) == piece.ExitLanes)
            {
                return turns;
            }
        }
        return -1;
    }

    /// <summary>특정 격자 중심에 배치된 도로의 활성 목록 인덱스를 찾는다.</summary>
    private static int FindRoadOrdinal(RoadLaneNetwork graph, int x, int y)
    {
        for (var i = 0; i < graph.Roads.Count; i++)
        {
            if (Near(graph.Roads[i].Position, new Vector2(x * 12.54f, y * 12.54f)))
            {
                return i;
            }
        }
        throw new InvalidOperationException("후송 첫 도로 누락");
    }

    /// <summary>실제 런타임 계산 결과를 이미지 위 시각 검토용으로 내보낸다.</summary>
    private static void ExportPaths()
    {
        var data = layouts.Select(pair => new { name = names[pair.Key], paths = pair.Value.Paths.Select(p => new
        { id = p.Id, kind = p.Kind.ToString(), maneuver = p.Maneuver.ToString(),
            points = p.Points.Select(v => new[] { v.x, v.y }).ToArray(), stop = p.HasStopLine }) }).ToArray();
        File.WriteAllText("output/lane-system-20261013/layout-paths.json", JsonSerializer.Serialize(data));
    }

    /// <summary>허용 오차 안에서 점이 이어지는지 검사한다.</summary>
    private static bool Near(Vector2 a, Vector2 b) => RoadLaneMath.Distance(a, b) < 0.001f;
    /// <summary>실패 즉시 이름과 함께 종료하고 성공한 검사를 센다.</summary>
    private static void Require(bool condition, string message)
    {
        assertions++;
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
