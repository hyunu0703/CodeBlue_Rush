using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class RoadPrefab : MonoBehaviour
{
    // 원본 이미지에 대응하는 고정 Index.
    [SerializeField] private RoadIndex index;
    // 중심 pivot 기준 이미지 전체 크기. 단위는 원본 PPU를 적용한 월드 유닛이다.
    [SerializeField] private Vector2 imageSize;
    // 회전 전 각 가장자리의 양방향 총 차선 수. 0은 닫힌 방향이며 현재 지원 값은 2, 4, 6이다.
    [SerializeField] private int northLaneCount;
    // 회전 전 동쪽 가장자리의 총 차선 수. 0은 연결 불가다.
    [SerializeField] private int eastLaneCount;
    // 회전 전 남쪽 가장자리의 총 차선 수. 0은 연결 불가다.
    [SerializeField] private int southLaneCount;
    // 회전 전 서쪽 가장자리의 총 차선 수. 0은 연결 불가다.
    [SerializeField] private int westLaneCount;

    // 불변 로컬 차로 레이아웃이다. 풀 반환 시에는 외부 도로 참조가 없으므로 그대로 재사용한다.
    private RoadLaneLayout laneLayout;

    /// <summary>카탈로그 조회에 사용하는 이미지별 고정 Index를 반환한다.</summary>
    public RoadIndex Index => index;
    /// <summary>스케일 1에서 이미지 전체의 월드 유닛 크기를 반환한다.</summary>
    public Vector2 ImageSize => imageSize;

    /// <summary>이 프리팹의 모든 주행 경로를 출력한다. 기존 직렬화 규격을 사용하므로 별도 프리팹 설정이 필요 없다.</summary>
    public bool TryGetLaneLayout(out RoadLaneLayout layout, out string error)
    {
        // 에디터 수정/핫 리로드로 규격이 달라졌다면 캐시를 새로 만든다. 영구 에셋에 계산 결과를 저장하지 않는다.
        if (laneLayout != null && (laneLayout.ImageSize != imageSize
            || laneLayout.GetLaneCount(RoadDirection.North) != northLaneCount
            || laneLayout.GetLaneCount(RoadDirection.East) != eastLaneCount
            || laneLayout.GetLaneCount(RoadDirection.South) != southLaneCount
            || laneLayout.GetLaneCount(RoadDirection.West) != westLaneCount))
        {
            laneLayout = null;
        }
        error = null;
        if (laneLayout == null && !RoadLaneLayout.TryCreate(imageSize, northLaneCount, eastLaneCount,
            southLaneCount, westLaneCount, out laneLayout, out error))
        {
            layout = null;
            return false;
        }
        layout = laneLayout;
        return true;
    }

#if UNITY_EDITOR
    /// <summary>선택한 도로의 주행 방향, 회전 경로 및 정지선을 표시한다. 에셋이나 씬을 수정하지 않는다.</summary>
    private void OnDrawGizmosSelected()
    {
        if (!TryGetLaneLayout(out var layout, out _))
        {
            return;
        }
        // 기존 Gizmos 전역 표시 상태를 보존한다. 경로는 로컬 좌표이고 렌더링 때 Transform을 적용한다.
        var previousColor = Gizmos.color;
        foreach (var path in layout.Paths)
        {
            Gizmos.color = path.Kind != RoadLanePathKind.Intersection ? Color.cyan
                : path.Maneuver == RoadLaneManeuver.Left ? Color.yellow
                : path.Maneuver == RoadLaneManeuver.Right ? Color.green : Color.white;
            for (var i = 1; i < path.Points.Count; i++)
            {
                Gizmos.DrawLine(transform.TransformPoint(path.Points[i - 1]), transform.TransformPoint(path.Points[i]));
            }
            if (path.TrySample(path.Length * 0.5f, out var center, out var forward))
            {
                var tip = center + forward * (layout.LaneWidth * 0.25f);
                var right = new Vector2(forward.y, -forward.x);
                Gizmos.DrawLine(transform.TransformPoint(tip), transform.TransformPoint(center + right * (layout.LaneWidth * 0.12f)));
                Gizmos.DrawLine(transform.TransformPoint(tip), transform.TransformPoint(center - right * (layout.LaneWidth * 0.12f)));
            }
            if (path.HasStopLine)
            {
                path.TrySample(path.Length, out var stop, out var forwardAtStop);
                var halfWidth = new Vector2(forwardAtStop.y, -forwardAtStop.x) * (layout.LaneWidth * 0.45f);
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.TransformPoint(stop - halfWidth), transform.TransformPoint(stop + halfWidth));
            }
        }
        Gizmos.color = previousColor;
    }
#endif

    /// <summary>배치 방향과 시계방향 90도 단위 회전으로 양방향 총 차선 수를 조회한다. 0은 연결 불가다.</summary>
    public int GetLaneCount(RoadDirection direction, int clockwiseQuarterTurns = 0)
    {
        // 배치된 방향을 원본 이미지의 방향으로 되돌려 저장된 차선 수를 조회한다.
        var localDirection = GetLocalDirection(direction, clockwiseQuarterTurns);
        switch (localDirection)
        {
            case RoadDirection.North: return northLaneCount;
            case RoadDirection.East: return eastLaneCount;
            case RoadDirection.South: return southLaneCount;
            case RoadDirection.West: return westLaneCount;
            default: throw new ArgumentOutOfRangeException(nameof(direction));
        }
    }

    /// <summary>배치 방향과 90도 단위 회전에 맞는 연결 위치를 출력한다. 이미지 중심 기준 월드 유닛이며 닫힌 방향은 false다.</summary>
    public bool TryGetConnectionPosition(RoadDirection direction, int clockwiseQuarterTurns, out Vector2 position)
    {
        position = Vector2.zero;
        if (GetLaneCount(direction, clockwiseQuarterTurns) == 0)
        {
            return false;
        }

        // 원본의 가로/세로 축과 중심에서 가장자리까지의 거리를 구한다. 위치 이동과 스케일은 호출자가 적용한다.
        var localDirection = GetLocalDirection(direction, clockwiseQuarterTurns);
        float distance = localDirection == RoadDirection.North || localDirection == RoadDirection.South
            ? imageSize.y * 0.5f : imageSize.x * 0.5f;
        switch (direction)
        {
            case RoadDirection.North: position = Vector2.up * distance; break;
            case RoadDirection.East: position = Vector2.right * distance; break;
            case RoadDirection.South: position = Vector2.down * distance; break;
            case RoadDirection.West: position = Vector2.left * distance; break;
        }
        return true;
    }

    /// <summary>배치 방향과 회전에 맞는 일자 도로 Index 및 시계방향 회전을 출력한다. Z 회전은 -90 * 회전 값이며 닫힌 방향은 false다.</summary>
    public bool TryGetStraightRoad(RoadDirection direction, int clockwiseQuarterTurns,
        out RoadIndex straightIndex, out int straightClockwiseQuarterTurns)
    {
        // 일자 도로 원본은 세로이므로 동서 연결에는 시계방향으로 한 번 회전한다.
        straightClockwiseQuarterTurns = direction == RoadDirection.East || direction == RoadDirection.West ? 1 : 0;
        switch (GetLaneCount(direction, clockwiseQuarterTurns))
        {
            case 2: straightIndex = RoadIndex.Straight2Lane; return true;
            case 4: straightIndex = RoadIndex.Straight4Lane; return true;
            case 6: straightIndex = RoadIndex.Straight6Lane; return true;
            default:
                straightIndex = RoadIndex.None;
                straightClockwiseQuarterTurns = 0;
                return false;
        }
    }

    /// <summary>배치된 방향을 원본 방향으로 변환한다. 정수 회전을 0~3으로 정규화하며 잘못된 방향은 예외로 알린다.</summary>
    private static RoadDirection GetLocalDirection(RoadDirection direction, int clockwiseQuarterTurns)
    {
        if ((int)direction < 0 || (int)direction > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }
        // 음수와 여러 바퀴 회전을 포함해 정규화한 시계방향 90도 단위 회전 값.
        int rotation = ((clockwiseQuarterTurns % 4) + 4) % 4;
        return (RoadDirection)(((int)direction - rotation + 4) % 4);
    }
}
