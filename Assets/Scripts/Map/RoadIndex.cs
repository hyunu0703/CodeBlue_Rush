// 이미지별 고정 식별자. 새 도로를 추가할 때 기존 숫자를 바꾸지 않는다.
public enum RoadIndex
{
    None = 0,
    Straight2Lane = 1,
    Straight4Lane = 2,
    Straight6Lane = 3,
    Cross2Lane = 4,
    Cross4Lane = 5,
    Cross6Lane = 6,
    T2Lane = 7,
    T4Lane = 8,
    T6Lane = 9,
    TV2LaneH4Lane = 10,
    TV4LaneH2Lane = 11,
    TV4LaneH6Lane = 12,
    TV6LaneH4Lane = 13
}

// XY 평면의 시계방향 순서. 북쪽은 +Y, 동쪽은 +X이며 값은 회전 계산에 사용한다.
public enum RoadDirection
{
    North = 0,
    East = 1,
    South = 2,
    West = 3
}
