using System;
using System.Collections.Generic;

/// <summary>씬과 난수 전역 상태에 의존하지 않는 8자리 시드 규칙이다. 자리 번호는 오른쪽부터 센다.</summary>
public static class MissionSeedRules
{
    // 미션 시드의 포함 범위. 반복 횟수인 짝수 자리는 1~9만 허용한다.
    public const int MinimumSeed = 10000000;
    public const int MaximumSeed = 99999999;

    /// <summary>전달받은 난수 소스로 반복 횟수에 0이 없는 시드를 만든다. 맵 겹침 검사는 별도로 수행한다.</summary>
    public static int CreateSeed(Random random)
    {
        if (random == null)
        {
            throw new ArgumentNullException(nameof(random));
        }

        // 각 자리 숫자를 오른쪽부터 합산한다. 8번째 자리도 반복 횟수이므로 앞자리 0이 없다.
        var seed = 0;
        var placeValue = 1;
        for (var place = 1; place <= 8; place++)
        {
            seed += random.Next(place % 2 == 0 ? 1 : 0, 10) * placeValue;
            placeValue *= 10;
        }
        return seed;
    }

    /// <summary>자리 번호 1~8의 숫자를 반환한다. 범위를 벗어난 자리 번호는 오류다.</summary>
    public static int GetDigit(int seed, int place)
    {
        if (place < 1 || place > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(place));
        }
        for (var position = 1; position < place; position++)
        {
            seed /= 10;
        }
        return seed % 10;
    }

    /// <summary>시드로 도로 종류와 연결 순서를 만든다. 범위, 반복 횟수 0, 자기 겹침은 실패 이유를 반환한다.</summary>
    public static bool TryBuildRoute(int seed, out MissionRoadPiece[] route, out string error)
    {
        route = Array.Empty<MissionRoadPiece>();
        error = null;
        if (seed < MinimumSeed || seed > MaximumSeed)
        {
            error = "시드는 10000000~99999999여야 합니다.";
            return false;
        }
        for (var place = 2; place <= 8; place += 2)
        {
            if (GetDigit(seed, place) == 0)
            {
                error = $"{place}번째 자리의 반복 횟수가 0입니다.";
                return false;
            }
        }

        // 최대 구역 수는 직선 9개씩 네 구간과 교차로 세 개를 합친 39개다.
        var pieces = new List<MissionRoadPiece>(39);
        var direction = RoadDirection.North;
        var firstDigit = GetDigit(seed, 1);
        var lanes = firstDigit <= 2 ? 2 : firstDigit <= 5 ? 4 : 6;
        AddStraights(pieces, GetDigit(seed, 2), direction, lanes);

        // 3번째 자리는 Cross 이후 방향, 5번째 자리는 일반 T 이후 방향을 정한다.
        var nextDirection = Turn(direction, GetDigit(seed, 3));
        AddPiece(pieces, Cross(lanes), direction, nextDirection, lanes, lanes);
        direction = nextDirection;
        AddStraights(pieces, GetDigit(seed, 4), direction, lanes);

        nextDirection = Turn(direction, GetDigit(seed, 5));
        AddPiece(pieces, Tee(lanes), direction, nextDirection, lanes, lanes);
        direction = nextDirection;
        AddStraights(pieces, GetDigit(seed, 6), direction, lanes);

        // 7번째 자리로 마지막 교차로와 방향을 함께 정하고, 출구 차선을 다음 직선에 적용한다.
        var finalDigit = GetDigit(seed, 7);
        var junction = Cross(lanes);
        var exitLanes = lanes;
        if (finalDigit >= 4 && finalDigit <= 6)
        {
            junction = lanes == 4 ? RoadIndex.TV4LaneH2Lane : Tee(lanes);
            exitLanes = lanes == 4 ? 2 : lanes;
        }
        else if (finalDigit >= 7)
        {
            junction = lanes == 2 ? RoadIndex.TV2LaneH4Lane
                : lanes == 4 ? RoadIndex.TV4LaneH6Lane : RoadIndex.TV6LaneH4Lane;
            exitLanes = lanes == 4 ? 6 : 4;
        }
        nextDirection = Turn(direction, finalDigit);
        AddPiece(pieces, junction, direction, nextDirection, lanes, exitLanes);
        AddStraights(pieces, GetDigit(seed, 8), nextDirection, exitLanes);

        // 동일한 정사각 구역을 같은 칸에 두 번 배치하는 경로는 생성 후보에서 제외한다.
        var occupied = new HashSet<long>();
        foreach (var piece in pieces)
        {
            var cell = ((long)piece.GridX << 32) | (uint)piece.GridY;
            if (!occupied.Add(cell))
            {
                error = "시드 경로가 이전 구역과 겹칩니다.";
                return false;
            }
        }
        route = pieces.ToArray();
        return true;
    }

    /// <summary>0~3은 직진, 4~6은 좌회전, 7~9는 우회전이다. 방향 enum은 시계방향 순서다.</summary>
    private static RoadDirection Turn(RoadDirection direction, int digit)
    {
        return (RoadDirection)(((int)direction + (digit <= 3 ? 0 : digit <= 6 ? 3 : 1)) % 4);
    }

    /// <summary>차선 수에 대응하는 직선 도로의 고정 식별자를 반환한다.</summary>
    private static RoadIndex Straight(int lanes)
    {
        return lanes == 2 ? RoadIndex.Straight2Lane : lanes == 4 ? RoadIndex.Straight4Lane : RoadIndex.Straight6Lane;
    }

    /// <summary>직전 도로와 같은 차선 수의 Cross 식별자를 반환한다.</summary>
    private static RoadIndex Cross(int lanes)
    {
        return lanes == 2 ? RoadIndex.Cross2Lane : lanes == 4 ? RoadIndex.Cross4Lane : RoadIndex.Cross6Lane;
    }

    /// <summary>직전 도로와 같은 차선 수의 일반 T 식별자를 반환한다.</summary>
    private static RoadIndex Tee(int lanes)
    {
        return lanes == 2 ? RoadIndex.T2Lane : lanes == 4 ? RoadIndex.T4Lane : RoadIndex.T6Lane;
    }

    /// <summary>주어진 개수의 동일 차선 직선을 진행 방향으로 이어 붙인다.</summary>
    private static void AddStraights(List<MissionRoadPiece> pieces, int count, RoadDirection direction, int lanes)
    {
        for (var index = 0; index < count; index++)
        {
            AddPiece(pieces, Straight(lanes), direction, direction, lanes, lanes);
        }
    }

    /// <summary>이전 구역의 출구 방향으로 한 칸 이동한 구역을 추가한다. 첫 구역의 중심은 원점이다.</summary>
    private static void AddPiece(List<MissionRoadPiece> pieces, RoadIndex index, RoadDirection incoming,
        RoadDirection outgoing, int entryLanes, int exitLanes)
    {
        // 겹침 검사와 재현성 확인에 사용하는 정수 격자 좌표다. 실제 배치는 프리팹 연결 위치로 계산한다.
        var x = 0;
        var y = 0;
        if (pieces.Count > 0)
        {
            var previous = pieces[pieces.Count - 1];
            x = previous.GridX;
            y = previous.GridY;
            switch (incoming)
            {
                case RoadDirection.North: y++; break;
                case RoadDirection.East: x++; break;
                case RoadDirection.South: y--; break;
                case RoadDirection.West: x--; break;
            }
        }
        pieces.Add(new MissionRoadPiece(index, incoming, outgoing, entryLanes, exitLanes, x, y));
    }
}

/// <summary>한 구역의 불변 생성 계획이다. Incoming은 진입 이동 방향이며 입구 가장자리는 그 반대다.</summary>
public readonly struct MissionRoadPiece
{
    // 생성할 프리팹 종류, 진입/출구 이동 방향, 해당 연결부의 양방향 총 차선 수다.
    public readonly RoadIndex Index;
    public readonly RoadDirection Incoming;
    public readonly RoadDirection Outgoing;
    public readonly int EntryLanes;
    public readonly int ExitLanes;
    // 정사각 구역 기준 자기 겹침 확인에 사용하는 정수 칸 좌표다.
    public readonly int GridX;
    public readonly int GridY;

    /// <summary>시드로 결정된 한 구역의 종류, 방향, 차선 수, 격자 좌표를 보관한다.</summary>
    public MissionRoadPiece(RoadIndex index, RoadDirection incoming, RoadDirection outgoing,
        int entryLanes, int exitLanes, int gridX, int gridY)
    {
        Index = index;
        Incoming = incoming;
        Outgoing = outgoing;
        EntryLanes = entryLanes;
        ExitLanes = exitLanes;
        GridX = gridX;
        GridY = gridY;
    }
}
