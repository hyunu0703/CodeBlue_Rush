using System;
using System.Collections.Generic;
using UnityEngine;

// 경로의 역할과 교차로 진행 방향이다. 유턴 경로는 생성하지 않는다.
public enum RoadLanePathKind { Straight, Approach, Departure, Intersection }
public enum RoadLaneManeuver { Straight, Left, Right }

/// <summary>주행 방향을 가진 불변 경로다. 좌표와 거리는 소유 레이아웃 또는 네트워크의 로컬 유닛이다.</summary>
public sealed class RoadLanePath
{
    // 외부에서 경로와 연결 목록을 수정할 수 없게 읽기 전용 뷰를 한 번 만든다.
    private readonly Vector2[] points;
    private readonly float[] distances;
    // 회전 경로의 각 점에서 공유하는 단위 접선이다. 직선 경로에는 보간이 필요 없다.
    private readonly Vector2[] curveTangents;
    private readonly List<int> successors = new List<int>(3);
    private readonly List<int> conflicts = new List<int>();
    public IReadOnlyList<Vector2> Points { get; }
    public IReadOnlyList<int> Successors { get; }
    public IReadOnlyList<int> Conflicts { get; }
    // Id는 해당 스냅숏 안에서만 유효하다. RoadOrdinal은 생성기의 ActiveRoads 인덱스다.
    public int Id { get; }
    public int RoadOrdinal { get; }
    public RoadLanePathKind Kind { get; }
    public RoadLaneManeuver Maneuver { get; }
    // Entry/Exit는 주행 방향이 아니라 도로 가장자리 방향이며, 차로 번호는 중앙선 쪽부터 1이다.
    public RoadDirection Entry { get; }
    public RoadDirection Exit { get; }
    public int EntryLaneNumber { get; }
    public int ExitLaneNumber { get; }
    public bool StartsAtBoundary => Kind == RoadLanePathKind.Straight || Kind == RoadLanePathKind.Approach;
    public bool EndsAtBoundary => Kind == RoadLanePathKind.Straight || Kind == RoadLanePathKind.Departure;
    public bool HasStopLine => Kind == RoadLanePathKind.Approach;
    public float StopLineDistance => HasStopLine ? Length : -1f;
    public bool RequiresExitClearance => Kind == RoadLanePathKind.Intersection;
    public float Width { get; }
    public float Length => distances[distances.Length - 1];
    // 같은 진행 방향의 인접 차로만 제공한다. 교차로 내부에는 인접 차로가 없다.
    public int LeftNeighborId { get; internal set; } = -1;
    public int RightNeighborId { get; internal set; } = -1;
    public float LaneChangeStartDistance { get; }
    public float LaneChangeEndDistance { get; }

    /// <summary>검증된 점들을 복사한다. 연결과 인접 차로는 빌더만 구성하며 공개 후에는 변경하지 않는다.</summary>
    internal RoadLanePath(int id, int roadOrdinal, RoadLanePathKind kind, RoadLaneManeuver maneuver,
        RoadDirection entry, RoadDirection exit, int entryLaneNumber, int exitLaneNumber,
        float width, Vector2[] points)
    {
        Id = id;
        RoadOrdinal = roadOrdinal;
        Kind = kind;
        Maneuver = maneuver;
        Entry = entry;
        Exit = exit;
        EntryLaneNumber = entryLaneNumber;
        ExitLaneNumber = exitLaneNumber;
        Width = width;
        this.points = (Vector2[])points.Clone();
        distances = new float[points.Length];
        // 누적 호 길이를 사용해 회전 곡선에서도 속도에 따른 거리 진행을 지원한다.
        for (var i = 1; i < points.Length; i++)
        {
            distances[i] = distances[i - 1] + RoadLaneMath.Distance(points[i - 1], points[i]);
        }
        if (kind == RoadLanePathKind.Intersection && maneuver != RoadLaneManeuver.Straight)
        {
            curveTangents = new Vector2[points.Length];
            // 진입/출구 직선의 방향을 정확히 이어 경로 전환 시에도 차체가 꺾이지 않는다.
            curveTangents[0] = -RoadLaneMath.Outward(entry);
            curveTangents[points.Length - 1] = RoadLaneMath.Outward(exit);
            for (var i = 1; i < points.Length - 1; i++)
            {
                // 양쪽 선분의 단위 방향을 평균하여 인접 구간이 동일한 접선을 사용한다.
                var tangent = (points[i] - points[i - 1]) / (distances[i] - distances[i - 1])
                    + (points[i + 1] - points[i]) / (distances[i + 1] - distances[i]);
                curveTangents[i] = tangent / RoadLaneMath.Distance(Vector2.zero, tangent);
            }
        }
        Points = Array.AsReadOnly(this.points);
        Successors = successors.AsReadOnly();
        Conflicts = conflicts.AsReadOnly();
        // 정지선 앞/횡단보도 직후 한 차로 폭은 게임의 보수적인 차로 변경 제한 구간이다.
        LaneChangeStartDistance = kind == RoadLanePathKind.Departure ? width : 0f;
        LaneChangeEndDistance = kind == RoadLanePathKind.Intersection ? 0f
            : Math.Max(LaneChangeStartDistance, Length - (HasStopLine ? width : 0f));
    }

    /// <summary>유한한 거리에서 위치와 단위 접선을 출력한다. 범위 밖 거리는 양 끝으로 제한한다.</summary>
    public bool TrySample(float distance, out Vector2 position, out Vector2 forward)
    {
        position = Vector2.zero;
        forward = Vector2.zero;
        if (!RoadLaneMath.IsFinite(distance))
        {
            return false;
        }
        // 경로의 호 길이에 대응하는 첫 선분을 찾는다. 점 수는 회전당 고정 25개 이하이다.
        var clamped = Math.Max(0f, Math.Min(Length, distance));
        for (var i = 1; i < points.Length; i++)
        {
            if (clamped > distances[i] && i < points.Length - 1)
            {
                continue;
            }
            var delta = points[i] - points[i - 1];
            var length = distances[i] - distances[i - 1];
            if (length <= 0f)
            {
                continue;
            }
            forward = delta / length;
            position = points[i - 1] + forward * (clamped - distances[i - 1]);
            if (curveTangents != null)
            {
                SampleCurve(i, (clamped - distances[i - 1]) / length, length, out position, out forward);
            }
            return true;
        }
        return false;
    }

    /// <summary>구간 양끝의 위치와 공유 접선으로 3차 Hermite 곡선을 계산한다. 방향은 실제 곡선의 미분이다.</summary>
    private void SampleCurve(int endIndex, float progress, float length, out Vector2 position, out Vector2 forward)
    {
        // 접선을 구간 길이로 환산한다. 거리 기준 미분이 경계 양쪽에서 같아 이동과 회전이 연속이다.
        var startTangent = curveTangents[endIndex - 1] * length;
        var endTangent = curveTangents[endIndex] * length;
        // 0~1 구간 진행률의 거듭제곱과 구간 양끝 위치다.
        var squared = progress * progress;
        var cubed = squared * progress;
        var start = points[endIndex - 1];
        var delta = points[endIndex] - start;
        // 상대 좌표로 계산해 맵 원점에서 먼 도로에서도 큰 절대 좌표의 상쇄 오차를 피한다.
        position = start
            + startTangent * (cubed - 2f * squared + progress)
            + delta * (-2f * cubed + 3f * squared)
            + endTangent * (cubed - squared);
        // 위치 차분으로 방향을 추정하지 않아 정차 후 재출발에서도 같은 자세를 유지한다.
        var derivative = startTangent * (3f * squared - 4f * progress + 1f)
            + delta * (-6f * squared + 6f * progress)
            + endTangent * (3f * squared - 2f * progress);
        forward = derivative / RoadLaneMath.Distance(Vector2.zero, derivative);
    }

    /// <summary>위치에 가장 가까운 경로의 호 길이와 제곱 거리를 출력한다. 차량을 움직이지 않는다.</summary>
    public bool TryProject(Vector2 position, out float distance, out float squaredOffset)
    {
        distance = 0f;
        squaredOffset = float.PositiveInfinity;
        if (!RoadLaneMath.IsFinite(position))
        {
            return false;
        }
        for (var i = 1; i < points.Length; i++)
        {
            // 가장 가까운 선분 투영을 호 길이로 환산한다.
            var delta = points[i] - points[i - 1];
            var lengthSquared = RoadLaneMath.Dot(delta, delta);
            var fraction = Math.Max(0f, Math.Min(1f, RoadLaneMath.Dot(position - points[i - 1], delta) / lengthSquared));
            var offset = position - (points[i - 1] + delta * fraction);
            var candidate = RoadLaneMath.Dot(offset, offset);
            if (candidate < squaredOffset)
            {
                squaredOffset = candidate;
                distance = distances[i - 1] + (distances[i] - distances[i - 1]) * fraction;
            }
        }
        return RoadLaneMath.IsFinite(squaredOffset);
    }

    /// <summary>차로 변경 후보 구간인지 반환한다. 교통 상황이나 실제 차량 간격의 안전 판정은 AI가 담당한다.</summary>
    public bool CanChangeLaneAt(float distance)
    {
        return (LeftNeighborId >= 0 || RightNeighborId >= 0) && RoadLaneMath.IsFinite(distance)
            && LaneChangeEndDistance > LaneChangeStartDistance
            && distance >= LaneChangeStartDistance && distance <= LaneChangeEndDistance;
    }

    /// <summary>빌드 중에만 중복 없는 정방향 연결을 추가한다.</summary>
    internal void AddSuccessor(int id)
    {
        if (!successors.Contains(id))
        {
            successors.Add(id);
        }
    }

    /// <summary>빌드 중에만 교차/합류하는 경로의 식별자를 추가한다.</summary>
    internal void AddConflict(int id)
    {
        conflicts.Add(id);
    }
}

/// <summary>Unity 네이티브 상태 없이 계산하는 XY 도로 기하 헬퍼다.</summary>
internal static class RoadLaneMath
{
    // 현재 도로 이미지의 실제 차로 폭 비율이며 미터 단위를 가정하지 않는다.
    internal const float LaneWidthRatio = 84.6f / 1254f;
    internal const float StopLineMarginRatio = (36.8f + 56f + 28f) / 1254f;

    /// <summary>NaN과 무한대를 제외한다.</summary>
    internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    /// <summary>XY 좌표가 유한한지 검사한다.</summary>
    internal static bool IsFinite(Vector2 value) => IsFinite(value.x) && IsFinite(value.y);
    /// <summary>2D 내적을 반환한다.</summary>
    internal static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
    /// <summary>두 점 사이 거리를 로컬 유닛으로 반환한다.</summary>
    internal static float Distance(Vector2 a, Vector2 b) => (float)Math.Sqrt(Dot(a - b, a - b));
    /// <summary>가장자리의 바깥 방향 단위 벡터를 반환한다.</summary>
    internal static Vector2 Outward(RoadDirection side)
    {
        switch (side)
        {
            case RoadDirection.North: return new Vector2(0f, 1f);
            case RoadDirection.East: return new Vector2(1f, 0f);
            case RoadDirection.South: return new Vector2(0f, -1f);
            case RoadDirection.West: return new Vector2(-1f, 0f);
            default: throw new ArgumentOutOfRangeException(nameof(side));
        }
    }
    /// <summary>주행 방향의 오른쪽 벡터를 반환한다.</summary>
    internal static Vector2 Right(Vector2 forward) => new Vector2(forward.y, -forward.x);
    /// <summary>부동소수점 삼각함수 없이 시계방향 90도 단위로 회전한다.</summary>
    internal static Vector2 Rotate(Vector2 point, int turns)
    {
        turns = ((turns % 4) + 4) % 4;
        for (var i = 0; i < turns; i++)
        {
            point = new Vector2(point.y, -point.x);
        }
        return point;
    }
    /// <summary>가장자리 방향을 시계방향 90도 단위로 회전한다.</summary>
    internal static RoadDirection Rotate(RoadDirection side, int turns) => (RoadDirection)(((int)side + turns % 4 + 4) % 4);
}
