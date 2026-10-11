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

        // 3·5·7번째 자리는 같은 선택 규칙을 적용한다. 혼합 T의 출구 차선은 다음 구간과 교차로에 이어진다.
        for (var place = 3; place <= 7; place += 2)
        {
            // 현재 자리의 선택 숫자, 교차로 종류, 출구 차선 수 및 다음 이동 방향이다.
            var digit = GetDigit(seed, place);
            var junction = SelectJunction(lanes, digit, out var exitLanes);
            var nextDirection = Turn(direction, digit);
            AddPiece(pieces, junction, direction, nextDirection, lanes, exitLanes);
            direction = nextDirection;
            lanes = exitLanes;
            AddStraights(pieces, GetDigit(seed, place + 1), direction, lanes);
        }

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

    /// <summary>기존 픽업 경로의 마지막 출구에 후송 계획을 연결한다. 차선과 두 경로의 겹침을 생성 전에 확인한다.</summary>
    public static bool TryBuildTransportRoute(int pickupSeed, int transportSeed, out MissionRoadPiece[] route, out string error)
    {
        route = Array.Empty<MissionRoadPiece>();
        if (!TryBuildRoute(pickupSeed, out var pickup, out error)
            || !TryBuildRoute(transportSeed, out var transport, out error))
        {
            return false;
        }
        // 픽업 마지막 구역은 직선이다. 북쪽 기준 후송 계획을 마지막 출구 방향으로 회전한다.
        var last = pickup[pickup.Length - 1];
        var turns = (int)last.Outgoing;
        if (transport[0].EntryLanes != last.ExitLanes)
        {
            error = "후송 시드의 시작 차선이 픽업 도로 출구와 맞지 않습니다.";
            return false;
        }
        // 첫 후송 칸은 픽업 마지막 칸의 출구 쪽 이웃이다. 이후 좌표도 동일한 90도 회전을 적용한다.
        var originX = last.GridX + (last.Outgoing == RoadDirection.East ? 1 : last.Outgoing == RoadDirection.West ? -1 : 0);
        var originY = last.GridY + (last.Outgoing == RoadDirection.North ? 1 : last.Outgoing == RoadDirection.South ? -1 : 0);
        var occupied = new HashSet<long>();
        foreach (var piece in pickup)
        {
            occupied.Add(((long)piece.GridX << 32) | (uint)piece.GridY);
        }
        var connected = new MissionRoadPiece[transport.Length];
        for (var index = 0; index < transport.Length; index++)
        {
            // XY의 시계방향 90도 회전은 (x,y) -> (y,-x)이다. 부동소수점 오차 없이 겹침을 검사한다.
            var piece = transport[index];
            var x = piece.GridX;
            var y = piece.GridY;
            for (var turn = 0; turn < turns; turn++)
            {
                var previousX = x;
                x = y;
                y = -previousX;
            }
            x += originX;
            y += originY;
            if (!occupied.Add(((long)x << 32) | (uint)y))
            {
                error = "후송 경로가 기존 픽업 도로나 자기 경로와 겹칩니다.";
                return false;
            }
            connected[index] = new MissionRoadPiece(piece.Index,
                (RoadDirection)(((int)piece.Incoming + turns) % 4),
                (RoadDirection)(((int)piece.Outgoing + turns) % 4), piece.EntryLanes, piece.ExitLanes, x, y);
        }
        route = connected;
        error = null;
        return true;
    }

    /// <summary>픽업 시드에서 재현 가능한 후송 후보를 선택한다. 사전 유효성 검사로 호출해도 게임/난수 상태는 변하지 않는다.</summary>
    public static bool TrySelectTransportSeed(int pickupSeed, out int transportSeed, out string error)
    {
        transportSeed = 0;
        if (!TryBuildRoute(pickupSeed, out _, out error))
        {
            return false;
        }
        // 고정된 지역 난수 소스는 수락 전 검증과 컷신 뒤 시드 확정에서 동일한 후보 순서를 제공한다.
        var random = new Random(pickupSeed ^ 0x5F3759DF);
        for (var attempt = 0; attempt < 1024; attempt++)
        {
            var candidate = CreateSeed(random);
            if (TryBuildTransportRoute(pickupSeed, candidate, out _, out error))
            {
                transportSeed = candidate;
                return true;
            }
        }
        error = "픽업 맵에서 차선과 겹침 조건을 만족하는 후송 도로를 연결할 수 없습니다. 다른 픽업 시드를 사용해주세요.";
        return false;
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

    /// <summary>현재 차선과 0~9의 선택 숫자로 교차로와 출구 차선을 결정한다. 3·5·7번째 자리에서 공통으로 사용한다.</summary>
    private static RoadIndex SelectJunction(int lanes, int digit, out int exitLanes)
    {
        exitLanes = lanes;
        if (digit <= 3)
        {
            return Cross(lanes);
        }
        if (digit <= 6)
        {
            exitLanes = lanes == 4 ? 2 : lanes;
            return lanes == 4 ? RoadIndex.TV4LaneH2Lane : Tee(lanes);
        }

        exitLanes = lanes == 4 ? 6 : 4;
        return lanes == 2 ? RoadIndex.TV2LaneH4Lane
            : lanes == 4 ? RoadIndex.TV4LaneH6Lane : RoadIndex.TV6LaneH4Lane;
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
