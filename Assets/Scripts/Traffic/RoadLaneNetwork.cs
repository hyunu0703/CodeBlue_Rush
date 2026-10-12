using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>불변 로컬 레이아웃과 맵 부모 기준 배치다. Unity 오브젝트 참조를 보관하지 않는다.</summary>
public readonly struct RoadLanePlacement
{
    // 위치는 맵 로컬 유닛, 회전은 시계방향 90도 횟수다. 레이아웃의 스케일은 1이다.
    public RoadLaneLayout Layout { get; }
    public Vector2 Position { get; }
    public int ClockwiseQuarterTurns { get; }

    /// <summary>배치 정보를 복사하고 회전을 0~3으로 정규화한다.</summary>
    public RoadLanePlacement(RoadLaneLayout layout, Vector2 position, int clockwiseQuarterTurns)
    {
        Layout = layout;
        Position = position;
        ClockwiseQuarterTurns = ((clockwiseQuarterTurns % 4) + 4) % 4;
    }
}

/// <summary>생성된 맵의 차로 그래프 스냅숏이다. 좌표/거리는 맵 로컬 기준이며 차량이나 신호를 제어하지 않는다.</summary>
public sealed class RoadLaneNetwork
{
    // 생성 시에만 사용하는 경계 위치 오차이며 차로 폭이나 차간 거리가 아니다.
    private const float ConnectionTolerance = 0.001f;
    public IReadOnlyList<RoadLanePath> Paths { get; }
    public IReadOnlyList<RoadLanePlacement> Roads { get; }
    // 생성기는 새 네트워크를 공개할 때 이전 스냅숏을 무효화한다. 이전 데이터는 읽을 수 있지만 사용하면 안 된다.
    public bool IsValid { get; private set; } = true;
    public int ConnectedBoundaryCount { get; private set; }

    /// <summary>빌드 중 구성할 읽기 전용 목록을 보관한다.</summary>
    private RoadLaneNetwork(List<RoadLanePath> paths, RoadLanePlacement[] roads)
    {
        Paths = paths.AsReadOnly();
        Roads = Array.AsReadOnly(roads);
    }

    /// <summary>완전한 후보 그래프를 만든다. 맞닿은 도로의 닫힌 출구/폭 불일치/겹침은 실패하고 원본은 수정하지 않는다.</summary>
    public static bool TryBuild(IReadOnlyList<RoadLanePlacement> placements, out RoadLaneNetwork network, out string error)
    {
        network = null;
        error = null;
        if (placements == null || placements.Count == 0)
        {
            error = "차로 네트워크에는 한 개 이상의 도로 배치가 필요합니다.";
            return false;
        }
        // 생성 순서를 유지해 후송을 뒤에 추가해도 기존 도로와 경로의 식별자를 보존한다.
        var roads = new RoadLanePlacement[placements.Count];
        var offsets = new int[placements.Count];
        var paths = new List<RoadLanePath>();
        for (var ordinal = 0; ordinal < roads.Length; ordinal++)
        {
            var placement = placements[ordinal];
            if (placement.Layout == null || !RoadLaneMath.IsFinite(placement.Position))
            {
                error = $"도로 {ordinal}: 유효한 차로 레이아웃과 배치 위치가 필요합니다.";
                return false;
            }
            roads[ordinal] = placement;
            offsets[ordinal] = paths.Count;
            foreach (var source in placement.Layout.Paths)
            {
                // 로컬 경로를 맵 로컬로 옮긴다. 행렬이나 네이티브 오브젝트 없이 검사할 수 있다.
                var points = new Vector2[source.Points.Count];
                for (var i = 0; i < points.Length; i++)
                {
                    points[i] = placement.Position + RoadLaneMath.Rotate(source.Points[i], placement.ClockwiseQuarterTurns);
                }
                var path = new RoadLanePath(paths.Count, ordinal, source.Kind, source.Maneuver,
                    RoadLaneMath.Rotate(source.Entry, placement.ClockwiseQuarterTurns),
                    RoadLaneMath.Rotate(source.Exit, placement.ClockwiseQuarterTurns),
                    source.EntryLaneNumber, source.ExitLaneNumber, source.Width, points);
                path.LeftNeighborId = source.LeftNeighborId < 0 ? -1 : offsets[ordinal] + source.LeftNeighborId;
                path.RightNeighborId = source.RightNeighborId < 0 ? -1 : offsets[ordinal] + source.RightNeighborId;
                foreach (var next in source.Successors)
                {
                    path.AddSuccessor(offsets[ordinal] + next);
                }
                paths.Add(path);
            }
        }
        var candidate = new RoadLaneNetwork(paths, roads);
        for (var a = 0; a < roads.Length; a++)
        {
            for (var b = a + 1; b < roads.Length; b++)
            {
                // 전체 이미지 구역의 겹침과 면 단위 접촉을 검사한다. 모서리 접촉은 도로 연결이 아니다.
                var delta = roads[b].Position - roads[a].Position;
                var span = (roads[a].Layout.ImageSize.x + roads[b].Layout.ImageSize.x) * 0.5f;
                if (Math.Abs(delta.x) < span - ConnectionTolerance && Math.Abs(delta.y) < span - ConnectionTolerance)
                {
                    error = $"도로 {a}/{b}: 차로 네트워크의 도로 구역이 겹칩니다.";
                    return false;
                }
                for (var sideIndex = 0; sideIndex < 4; sideIndex++)
                {
                    var side = (RoadDirection)sideIndex;
                    if (RoadLaneMath.Distance(delta, RoadLaneMath.Outward(side) * span) > ConnectionTolerance)
                    {
                        continue;
                    }
                    var opposite = RoadLaneMath.Rotate(side, 2);
                    var countA = roads[a].Layout.GetLaneCount(RoadLaneMath.Rotate(side, -roads[a].ClockwiseQuarterTurns));
                    var countB = roads[b].Layout.GetLaneCount(RoadLaneMath.Rotate(opposite, -roads[b].ClockwiseQuarterTurns));
                    if (countA == 0 && countB == 0)
                    {
                        continue;
                    }
                    if (countA != countB || Math.Abs(roads[a].Layout.LaneWidth - roads[b].Layout.LaneWidth) > ConnectionTolerance)
                    {
                        error = $"도로 {a}/{b}: 맞닿은 {side} 출구의 차로 수 또는 차로 폭이 다릅니다.";
                        return false;
                    }
                    if (!ConnectBoundary(paths, offsets, roads, a, b, side, opposite)
                        || !ConnectBoundary(paths, offsets, roads, b, a, opposite, side))
                    {
                        error = $"도로 {a}/{b}: 차로 경계 위치나 주행 접선이 이어지지 않습니다.";
                        return false;
                    }
                    candidate.ConnectedBoundaryCount++;
                }
            }
        }
        // 교차로의 공통 입구/출구 및 경로 폭이 겹치는 구간을 미래의 양보/예약 판단용으로 제공한다.
        for (var ordinal = 0; ordinal < roads.Length; ordinal++)
        {
            var start = offsets[ordinal];
            var end = start + roads[ordinal].Layout.Paths.Count;
            for (var a = start; a < end; a++)
            {
                if (paths[a].Kind != RoadLanePathKind.Intersection)
                {
                    continue;
                }
                for (var b = a + 1; b < end; b++)
                {
                    if (paths[b].Kind == RoadLanePathKind.Intersection && CorridorsOverlap(paths[a], paths[b]))
                    {
                        paths[a].AddConflict(b);
                        paths[b].AddConflict(a);
                    }
                }
            }
        }
        network = candidate;
        return true;
    }

    /// <summary>차량 초기 배치용 근접 경로를 찾는다. forward가 0이면 방향 필터가 없고, 그 외에는 60도 이내 경로만 허용한다.</summary>
    public bool TryFindNearest(Vector2 position, Vector2 forward, float maxDistance, out int pathId, out float distance)
    {
        pathId = -1;
        distance = 0f;
        if (!IsValid || !RoadLaneMath.IsFinite(position) || !RoadLaneMath.IsFinite(forward)
            || !RoadLaneMath.IsFinite(maxDistance) || maxDistance < 0f)
        {
            return false;
        }
        // 전체 검색은 스폰/경로 재획득용이다. 주행 중에는 현재 Id와 거리로 직접 샘플링한다.
        var bestSquared = maxDistance * maxDistance;
        var forwardLength = RoadLaneMath.Distance(Vector2.zero, forward);
        foreach (var path in Paths)
        {
            if (!path.TryProject(position, out var along, out var squared) || squared > bestSquared
                || !path.TrySample(along, out _, out var tangent)
                || (forwardLength > 0f && RoadLaneMath.Dot(forward / forwardLength, tangent) < 0.5f))
            {
                continue;
            }
            if (pathId >= 0 && squared == bestSquared)
            {
                continue;
            }
            bestSquared = squared;
            pathId = path.Id;
            distance = along;
        }
        return pathId >= 0;
    }

    /// <summary>차로 변경 없이 이어지는 정방향 경로를 출력한다. 결과는 현재 스냅숏에서만 유효하다.</summary>
    public bool TryFindRoute(int startId, int destinationId, List<int> result)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }
        result.Clear();
        if (!IsValid || startId < 0 || destinationId < 0 || startId >= Paths.Count || destinationId >= Paths.Count)
        {
            return false;
        }
        // 탐색마다 상태를 로컬로 소유해 호출 순서와 이전 탐색 결과에 의존하지 않는다.
        var previous = new int[Paths.Count];
        for (var i = 0; i < previous.Length; i++)
        {
            previous[i] = -1;
        }
        var pending = new Queue<int>();
        previous[startId] = startId;
        pending.Enqueue(startId);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (current == destinationId)
            {
                for (var id = current; ; id = previous[id])
                {
                    result.Add(id);
                    if (id == startId)
                    {
                        break;
                    }
                }
                result.Reverse();
                return true;
            }
            foreach (var next in Paths[current].Successors)
            {
                if (previous[next] < 0)
                {
                    previous[next] = current;
                    pending.Enqueue(next);
                }
            }
        }
        return false;
    }

    /// <summary>생성기의 공개 경계에서 이전 그래프를 무효화한다. 경로 데이터 자체는 수정하지 않는다.</summary>
    internal void Invalidate() => IsValid = false;

    /// <summary>같은 번호의 출발/진입 차로를 위치와 접선을 검사해 연결한다.</summary>
    private static bool ConnectBoundary(List<RoadLanePath> paths, int[] offsets, RoadLanePlacement[] roads,
        int from, int to, RoadDirection exit, RoadDirection entry)
    {
        // 편도 차로마다 반드시 한 개의 대응 차로가 있는지 확인한다.
        var matched = 0;
        for (var i = offsets[from]; i < offsets[from] + roads[from].Layout.Paths.Count; i++)
        {
            var source = paths[i];
            if (!source.EndsAtBoundary || source.Exit != exit)
            {
                continue;
            }
            for (var j = offsets[to]; j < offsets[to] + roads[to].Layout.Paths.Count; j++)
            {
                var target = paths[j];
                if (!target.StartsAtBoundary || target.Entry != entry || source.ExitLaneNumber != target.EntryLaneNumber)
                {
                    continue;
                }
                source.TrySample(source.Length, out var end, out var sourceForward);
                target.TrySample(0f, out var start, out var targetForward);
                if (RoadLaneMath.Distance(end, start) > ConnectionTolerance || RoadLaneMath.Dot(sourceForward, targetForward) < 0.999f)
                {
                    return false;
                }
                source.AddSuccessor(target.Id);
                matched++;
                break;
            }
        }
        return matched == roads[from].Layout.GetLaneCount(RoadLaneMath.Rotate(exit, -roads[from].ClockwiseQuarterTurns)) / 2;
    }

    /// <summary>교차/합류하는 차로 폭의 통행 구간을 보수적으로 판정한다. 차체 길이와 동적 점유는 향후 AI가 처리한다.</summary>
    private static bool CorridorsOverlap(RoadLanePath a, RoadLanePath b)
    {
        // 두 경로의 편측 폭 합계보다 중심선이 가까우면 동시에 통행할 수 없는 후보로 분류한다.
        var clearance = (a.Width + b.Width) * 0.5f;
        for (var i = 1; i < a.Points.Count; i++)
        {
            for (var j = 1; j < b.Points.Count; j++)
            {
                // 경계에 정확히 닿는 평행 차로를 부동소수점 위치 오차 때문에 충돌로 분류하지 않는다.
                if (SegmentDistance(a.Points[i - 1], a.Points[i], b.Points[j - 1], b.Points[j]) < clearance - ConnectionTolerance)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>XY 선분 교차 또는 끝점 투영으로 최단 거리를 반환한다.</summary>
    private static float SegmentDistance(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        // 외적의 매개변수로 실제 교차를 판정한 뒤 평행/비교차 선분은 끝점으로 검사한다.
        var u = b - a;
        var v = d - c;
        var delta = c - a;
        var cross = u.x * v.y - u.y * v.x;
        if (Math.Abs(cross) > 0.0000001f)
        {
            var t = (delta.x * v.y - delta.y * v.x) / cross;
            var s = (delta.x * u.y - delta.y * u.x) / cross;
            if (t >= 0f && t <= 1f && s >= 0f && s <= 1f)
            {
                return 0f;
            }
        }
        return Math.Min(Math.Min(PointSegmentDistance(a, c, d), PointSegmentDistance(b, c, d)),
            Math.Min(PointSegmentDistance(c, a, b), PointSegmentDistance(d, a, b)));
    }

    /// <summary>점을 선분에 투영한 거리를 반환한다.</summary>
    private static float PointSegmentDistance(Vector2 point, Vector2 start, Vector2 end)
    {
        var delta = end - start;
        var fraction = Math.Max(0f, Math.Min(1f, RoadLaneMath.Dot(point - start, delta) / RoadLaneMath.Dot(delta, delta)));
        return RoadLaneMath.Distance(point, start + delta * fraction);
    }
}
