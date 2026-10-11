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

    /// <summary>카탈로그 조회에 사용하는 이미지별 고정 Index를 반환한다.</summary>
    public RoadIndex Index => index;
    /// <summary>스케일 1에서 이미지 전체의 월드 유닛 크기를 반환한다.</summary>
    public Vector2 ImageSize => imageSize;

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
