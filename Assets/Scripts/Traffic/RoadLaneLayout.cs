using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>왕복 2/4/6차로 도로 한 개의 로컬 주행 경로다. 원본 에셋이나 씬 상태를 수정하지 않는다.</summary>
public sealed class RoadLaneLayout
{
    // 고정 이미지 규격에서 계산한 차로 수와 경로를 외부 수정 없이 보관한다.
    private readonly int[] laneCounts;
    public Vector2 ImageSize { get; }
    public float LaneWidth { get; }
    public bool IsIntersection { get; }
    public IReadOnlyList<RoadLanePath> Paths { get; }

    /// <summary>검증된 이미지 크기와 방향별 차로 수에서 모든 경로를 생성한다.</summary>
    private RoadLaneLayout(Vector2 size, int[] counts, bool intersection)
    {
        ImageSize = size;
        LaneWidth = size.x * RoadLaneMath.LaneWidthRatio;
        laneCounts = counts;
        IsIntersection = intersection;
        // 경로 번호는 방향, 차로 번호, 역할 순서로 결정되며 난수를 사용하지 않는다.
        var paths = new List<RoadLanePath>();
        for (var sideIndex = 0; sideIndex < 4; sideIndex++)
        {
            var side = (RoadDirection)sideIndex;
            var count = counts[sideIndex] / 2;
            for (var lane = 1; lane <= count; lane++)
            {
                if (!intersection)
                {
                    var opposite = RoadLaneMath.Rotate(side, 2);
                    AddPath(paths, RoadLanePathKind.Straight, RoadLaneManeuver.Straight, side, opposite, lane, lane,
                        new[] { LanePosition(side, lane, true, EdgeDistance(side)),
                            LanePosition(opposite, lane, false, EdgeDistance(opposite)) });
                    continue;
                }
                // 진입 경로는 실제 정지선 앞에서 끝나며 출발 경로는 횡단보도 바깥에서 시작한다.
                AddPath(paths, RoadLanePathKind.Approach, RoadLaneManeuver.Straight, side, side, lane, lane,
                    new[] { LanePosition(side, lane, true, EdgeDistance(side)),
                        LanePosition(side, lane, true, StopDistance(side)) });
                AddPath(paths, RoadLanePathKind.Departure, RoadLaneManeuver.Straight, side, side, lane, lane,
                    new[] { LanePosition(side, lane, false, StopDistance(side)),
                        LanePosition(side, lane, false, EdgeDistance(side)) });
            }
        }
        if (intersection)
        {
            for (var sideIndex = 0; sideIndex < 4; sideIndex++)
            {
                var side = (RoadDirection)sideIndex;
                var count = counts[sideIndex] / 2;
                for (var lane = 1; lane <= count; lane++)
                {
                    // 직진은 차로 번호 유지, 좌회전은 안쪽 1차로, 우회전은 바깥쪽 마지막 차로만 허용한다.
                    AddMovement(paths, side, RoadLaneMath.Rotate(side, 2), lane, lane, RoadLaneManeuver.Straight);
                    if (lane == 1)
                    {
                        AddMovement(paths, side, RoadLaneMath.Rotate(side, 1), lane, 1, RoadLaneManeuver.Left);
                    }
                    if (lane == count)
                    {
                        var exit = RoadLaneMath.Rotate(side, 3);
                        AddMovement(paths, side, exit, lane, counts[(int)exit] / 2, RoadLaneManeuver.Right);
                    }
                }
            }
        }
        // 세로 진입 2차로에서 Road_T_6Lane는 오른쪽 2차로, Road_T_V6Lane_H4Lane는 오른쪽 1차로로 우회전한다.
        // 기존 경로 뒤에 추가해 이 두 도로 안에서 기존 경로 번호를 유지하며, 회전 배치는 네트워크가 적용한다.
        if (counts[0] == 0 && counts[2] == 6 && counts[1] == counts[3] && (counts[1] == 4 || counts[1] == 6))
        {
            var exitLane = counts[1] == 4 ? 1 : 2;
            AddMovement(paths, RoadDirection.South, RoadDirection.East, 2, exitLane, RoadLaneManeuver.Right);
        }
        // 같은 도로 안의 연결과 인접 차로를 확정한다. 추가한 2차로 회전도 진입/출발 경로에 연결한다.
        foreach (var path in paths)
        {
            foreach (var other in paths)
            {
                if (path.Kind == RoadLanePathKind.Approach && other.Kind == RoadLanePathKind.Intersection
                    && path.Entry == other.Entry && path.EntryLaneNumber == other.EntryLaneNumber)
                {
                    path.AddSuccessor(other.Id);
                }
                if (path.Kind == RoadLanePathKind.Intersection && other.Kind == RoadLanePathKind.Departure
                    && path.Exit == other.Exit && path.ExitLaneNumber == other.ExitLaneNumber)
                {
                    path.AddSuccessor(other.Id);
                }
                if (path.Kind != RoadLanePathKind.Intersection && path.Kind == other.Kind && path.Entry == other.Entry)
                {
                    if (other.EntryLaneNumber == path.EntryLaneNumber - 1)
                    {
                        path.LeftNeighborId = other.Id;
                    }
                    if (other.EntryLaneNumber == path.EntryLaneNumber + 1)
                    {
                        path.RightNeighborId = other.Id;
                    }
                }
            }
        }
        Paths = paths.AsReadOnly();
    }

    /// <summary>현재 도로 규격만 허용하며 불일치 시 생성하지 않고 실패 이유를 반환한다.</summary>
    public static bool TryCreate(Vector2 size, int north, int east, int south, int west,
        out RoadLaneLayout layout, out string error)
    {
        layout = null;
        error = null;
        if (!RoadLaneMath.IsFinite(size) || size.x <= 0f || Math.Abs(size.x - size.y) > 0.001f)
        {
            error = "차로 생성에는 유한한 중심 pivot 정사각 Image Size가 필요합니다.";
            return false;
        }
        // 열린 방향 수와 마주 보는 도로의 차로 수를 확인한다. 임의의 축소/합류를 만들어내지 않는다.
        var counts = new[] { north, east, south, west };
        var open = 0;
        foreach (var count in counts)
        {
            if (count != 0 && count != 2 && count != 4 && count != 6)
            {
                error = "도로 가장자리의 왕복 차로 수는 0, 2, 4, 6이어야 합니다.";
                return false;
            }
            if (count > 0)
            {
                open++;
            }
        }
        if (open < 2 || (north > 0 && south > 0 && north != south)
            || (east > 0 && west > 0 && east != west)
            || (open == 2 && !((north > 0 && south > 0) || (east > 0 && west > 0))))
        {
            error = "지원 도로는 같은 폭의 일자 도로 또는 반대쪽 차로 수가 같은 T/십자 교차로입니다.";
            return false;
        }
        layout = new RoadLaneLayout(size, counts, open >= 3);
        return true;
    }

    /// <summary>가장자리의 왕복 총 차로 수를 반환한다.</summary>
    public int GetLaneCount(RoadDirection side)
    {
        RoadLaneMath.Outward(side);
        return laneCounts[(int)side];
    }

    /// <summary>로컬 도로 중심에서 가장자리까지의 거리를 반환한다.</summary>
    internal float EdgeDistance(RoadDirection side) => (side == RoadDirection.North || side == RoadDirection.South
        ? ImageSize.y : ImageSize.x) * 0.5f;

    /// <summary>교차하는 도로의 폭과 이미지의 횡단보도/정지선 여백으로 정지선 거리를 계산한다.</summary>
    private float StopDistance(RoadDirection side)
    {
        // 혼합 T에서도 해당 방향과 수직인 도로의 폭을 사용한다.
        var perpendicular = side == RoadDirection.North || side == RoadDirection.South
            ? Math.Max(laneCounts[1], laneCounts[3]) : Math.Max(laneCounts[0], laneCounts[2]);
        return perpendicular * LaneWidth * 0.5f + ImageSize.x * RoadLaneMath.StopLineMarginRatio;
    }

    /// <summary>중앙선에서 차로 중심까지의 오프셋을 주행 방향 오른쪽으로 적용한다.</summary>
    private Vector2 LanePosition(RoadDirection side, int lane, bool incoming, float radialDistance)
    {
        var outward = RoadLaneMath.Outward(side);
        var forward = incoming ? -outward : outward;
        return outward * radialDistance + RoadLaneMath.Right(forward) * (LaneWidth * (lane - 0.5f));
    }

    /// <summary>경로 목록 끝에 불변 경로를 추가한다.</summary>
    private void AddPath(List<RoadLanePath> paths, RoadLanePathKind kind, RoadLaneManeuver maneuver,
        RoadDirection entry, RoadDirection exit, int entryLane, int exitLane, Vector2[] points)
    {
        paths.Add(new RoadLanePath(paths.Count, -1, kind, maneuver, entry, exit, entryLane, exitLane, LaneWidth, points));
    }

    /// <summary>존재하는 출구의 지정 차로로 연결한다. 기본 회전 및 두 T자 도로의 추가 2차로 우회전에 사용한다.</summary>
    private void AddMovement(List<RoadLanePath> paths, RoadDirection entry, RoadDirection exit,
        int entryLane, int exitLane, RoadLaneManeuver maneuver)
    {
        if (exitLane < 1 || exitLane > laneCounts[(int)exit] / 2)
        {
            return;
        }
        // 정지선에서 출구 횡단보도 바깥까지의 점과 접선이다.
        var start = LanePosition(entry, entryLane, true, StopDistance(entry));
        var end = LanePosition(exit, exitLane, false, StopDistance(exit));
        if (maneuver == RoadLaneManeuver.Straight)
        {
            AddPath(paths, RoadLanePathKind.Intersection, maneuver, entry, exit, entryLane, exitLane, new[] { start, end });
            return;
        }
        // 원호의 3차 베지어 근사로 진입/출구 접선을 맞춘다. 혼합 폭에서는 타원 사분면이 된다.
        const float quarterArcFactor = 0.55228475f;
        const int curveSteps = 24;
        var incoming = -RoadLaneMath.Outward(entry);
        var outgoing = RoadLaneMath.Outward(exit);
        var delta = end - start;
        var controlA = start + incoming * (RoadLaneMath.Dot(delta, incoming) * quarterArcFactor);
        var controlB = end - outgoing * (RoadLaneMath.Dot(delta, outgoing) * quarterArcFactor);
        var points = new Vector2[curveSteps + 1];
        for (var i = 0; i <= curveSteps; i++)
        {
            var t = i / (float)curveSteps;
            var u = 1f - t;
            points[i] = start * (u * u * u) + controlA * (3f * u * u * t)
                + controlB * (3f * u * t * t) + end * (t * t * t);
        }
        AddPath(paths, RoadLanePathKind.Intersection, maneuver, entry, exit, entryLane, exitLane, points);
    }
}
