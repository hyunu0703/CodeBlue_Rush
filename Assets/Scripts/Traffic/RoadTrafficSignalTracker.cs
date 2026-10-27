using System.Collections.Generic;
using UnityEngine;

/// <summary>교차로 앞 직선 도로부터 신호를 선택하고 교차로 이탈까지 진입 방향을 보존한다.</summary>
public sealed class RoadTrafficSignalTracker
{
    private RoadLaneNetwork network; // 도로 번호가 속한 현재 맵 스냅숏이다.
    private int road = -1;           // 차체가 완전히 벗어날 때까지 유지할 교차로 번호다.
    private RoadDirection entry;    // 회전 중에도 바뀌지 않는 맵 기준 진입 가장자리다.
    private int approach = -1;      // 표시를 시작할 교차로 직전 직선 도로 번호다.

    // 새 맵이나 미션 종료 시 이전 교차로의 진입 기록을 비운다.
    public void Reset()
    {
        network = null;
        road = -1;
        approach = -1;
    }

    // 안내 경로의 교차로 직전 직선 도로에 닿으면 신호를 선택하고 교차로 이탈까지 유지한다.
    public bool TryTrack(RoadLaneNetwork current, Rect vehicle, Vector2 forward,
        out int roadOrdinal, out RoadDirection entrySide, IReadOnlyList<Vector2> route = null)
    {
        roadOrdinal = -1;
        entrySide = default;
        if (current == null || !current.IsValid)
        {
            Reset();
            return false;
        }
        if (!ReferenceEquals(network, current))
        {
            Reset();
            network = current;
        }
        if (road >= 0 && !Overlaps(current.Roads[road], vehicle)
            && (approach < 0 || !Overlaps(current.Roads[approach], vehicle)))
        {
            road = -1;
            approach = -1;
        }
        if (road < 0)
        {
            for (var index = 0; index < current.Roads.Count; index++)
            {
                var placement = current.Roads[index];
                if (!placement.Layout.IsIntersection) continue;
                var offset = vehicle.center - placement.Position;
                var incoming = forward;
                if (route != null && !TryGetIncoming(placement.Position, route, out incoming)) continue;
                entry = Mathf.Abs(incoming.y) >= Mathf.Abs(incoming.x)
                    ? (incoming.y >= 0f ? RoadDirection.South : RoadDirection.North)
                    : (incoming.x >= 0f ? RoadDirection.West : RoadDirection.East);
                // 출구 쪽에서 멀어지는 차량이나 T자 도로의 닫힌 가장자리는 새 진입으로 보지 않는다.
                if (Vector2.Dot(offset, RoadLaneMath.Outward(entry)) < 0f
                    || placement.Layout.GetLaneCount(RoadLaneMath.Rotate(entry, -placement.ClockwiseQuarterTurns)) == 0)
                    continue;
                var nextApproach = route != null ? FindApproach(current, index, entry) : -1;
                // 직선 도로의 시작 경계부터 미리 표시한다. 직선이 없으면 기존 교차로 진입 면을 사용한다.
                if (nextApproach >= 0)
                {
                    if (!Overlaps(current.Roads[nextApproach], vehicle)) continue;
                }
                else if (!Overlaps(placement, vehicle) || (route != null && !TouchesEntry(placement, vehicle, entry))) continue;
                road = index;
                approach = nextApproach;
                break;
            }
        }
        if (road < 0) return false;
        roadOrdinal = road;
        entrySide = entry;
        return true;
    }

    // 해당 진입 가장자리에 직접 연결된 직선 도로를 실제 차로 그래프에서 찾는다.
    private static int FindApproach(RoadLaneNetwork current, int junction, RoadDirection side)
    {
        for (var index = 0; index < current.Paths.Count; index++)
        {
            var path = current.Paths[index];
            if (path.Kind != RoadLanePathKind.Straight) continue;
            for (var next = 0; next < path.Successors.Count; next++)
            {
                var target = current.Paths[path.Successors[next]];
                if (target.RoadOrdinal == junction && target.Kind == RoadLanePathKind.Approach && target.Entry == side)
                    return path.RoadOrdinal;
            }
        }
        return -1;
    }

    // 내비게이션이 교차로 중심으로 들어오는 선분에서 진입 방향을 구한다.
    private static bool TryGetIncoming(Vector2 center, IReadOnlyList<Vector2> route, out Vector2 incoming)
    {
        incoming = Vector2.zero;
        for (var index = 1; index < route.Count; index++)
        {
            if ((route[index] - center).sqrMagnitude > 0.0001f) continue;
            incoming = route[index] - route[index - 1];
            if (incoming.sqrMagnitude > 0.0001f) return true;
        }
        return false;
    }

    // 차체가 직선 도로와 교차로가 맞닿은 진입 면에 도달했는지 검사한다.
    private static bool TouchesEntry(RoadLanePlacement placement, Rect vehicle, RoadDirection side)
    {
        var half = placement.Layout.ImageSize * 0.5f;
        switch (side)
        {
            case RoadDirection.North:
                return vehicle.yMin <= placement.Position.y + half.y && vehicle.yMax >= placement.Position.y + half.y;
            case RoadDirection.South:
                return vehicle.yMax >= placement.Position.y - half.y && vehicle.yMin <= placement.Position.y - half.y;
            case RoadDirection.East:
                return vehicle.xMin <= placement.Position.x + half.x && vehicle.xMax >= placement.Position.x + half.x;
            default:
                return vehicle.xMax >= placement.Position.x - half.x && vehicle.xMin <= placement.Position.x - half.x;
        }
    }

    // 정사각 도로 타일의 진입·진출 구간을 포함하여 차체와 경계 접촉을 검사한다.
    private static bool Overlaps(RoadLanePlacement placement, Rect vehicle)
    {
        var half = placement.Layout.ImageSize * 0.5f;
        return vehicle.xMax >= placement.Position.x - half.x && vehicle.xMin <= placement.Position.x + half.x
            && vehicle.yMax >= placement.Position.y - half.y && vehicle.yMin <= placement.Position.y + half.y;
    }
}
