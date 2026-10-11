using System;
using System.Collections.Generic;

// Unity를 실행하지 않는 순수 계획 검증이다. 실제 런타임 MissionSeedRules를 직접 컴파일한다.
internal static class PlannerChecks
{
    // 재현 가능한 표본 10,000개에서 두 단계 사이의 도달 가능성과 생성 상한을 확인한다.
    private const int SampleCount = 10000;

    /// <summary>고정 예제와 무작위 표본을 검사하고 실패하면 비정상 종료한다.</summary>
    private static void Main()
    {
        Require(MissionSeedRules.TryBuildTransportRoute(11111111, 11111111, out var straight, out _), "직진 연결 실패");
        Require(straight.Length == 7 && straight[0].GridX == 0 && straight[0].GridY == 7
            && straight[6].GridX == 0 && straight[6].GridY == 13, "직진 연결 좌표 오류");
        Require(!MissionSeedRules.TryBuildTransportRoute(11111111, 11111114, out var mismatch, out _)
            && mismatch.Length == 0, "차선 불일치를 허용함");
        Require(!MissionSeedRules.TryBuildTransportRoute(10000000, 11111111, out var invalid, out _)
            && invalid.Length == 0, "잘못된 픽업 시드를 허용함");
        Require(!MissionSeedRules.TrySelectTransportSeed(89279899, out var blockedSeed, out _)
            && blockedSeed == 0, "출구가 막힌 픽업 맵을 허용함");

        // 프로젝트에서 사용하는 실제 난수 순서와 시드 파생식을 따라 성공할 때까지 후보를 제한하여 검증한다.
        var random = new Random(20261012);
        var seenDirections = new HashSet<RoadDirection>();
        var seenLanes = new HashSet<int>();
        var largestAttempt = 0;
        var overlapRejections = 0;
        var firstOverlap = "";
        var blockedRejections = 0;
        for (var sample = 0; sample < SampleCount; sample++)
        {
            int pickupSeed;
            MissionRoadPiece[] pickup;
            do
            {
                pickupSeed = MissionSeedRules.CreateSeed(random);
            }
            while (!MissionSeedRules.TryBuildRoute(pickupSeed, out pickup, out _));
            if (!MissionSeedRules.TrySelectTransportSeed(pickupSeed, out var selectedSeed, out _))
            {
                blockedRejections++;
                sample--;
                continue;
            }
            var last = pickup[pickup.Length - 1];
            seenDirections.Add(last.Outgoing);
            seenLanes.Add(last.ExitLanes);
            var transportRandom = new Random(pickupSeed ^ 0x5F3759DF);
            var acceptedSeed = 0;
            for (var attempt = 1; attempt <= 1024; attempt++)
            {
                var candidate = MissionSeedRules.CreateSeed(transportRandom);
                if (!MissionSeedRules.TryBuildTransportRoute(pickupSeed, candidate, out var transport, out var error))
                {
                    if (error.Contains("기존 픽업"))
                    {
                        overlapRejections++;
                        if (firstOverlap.Length == 0)
                        {
                            firstOverlap = $"{pickupSeed}/{candidate}";
                        }
                    }
                    continue;
                }
                Require(pickup.Length <= 39 && transport.Length <= 39, "구역 수 상한 초과");
                var cells = new HashSet<(int, int)>();
                foreach (var piece in pickup)
                {
                    Require(cells.Add((piece.GridX, piece.GridY)), "픽업 도로 겹침");
                }
                var previous = last;
                foreach (var piece in transport)
                {
                    // 연결은 한 칸 이웃이며 진행 방향과 진입/출구 차선 수가 같아야 실제로 이동 가능하다.
                    Require(cells.Add((piece.GridX, piece.GridY)), "후송 도로 겹침");
                    var dx = piece.GridX - previous.GridX;
                    var dy = piece.GridY - previous.GridY;
                    Require(Math.Abs(dx) + Math.Abs(dy) == 1, "도로 연결 끊김");
                    Require(piece.Incoming == previous.Outgoing && piece.EntryLanes == previous.ExitLanes, "연결 방향/차선 불일치");
                    Require((dx == 1 && piece.Incoming == RoadDirection.East)
                        || (dx == -1 && piece.Incoming == RoadDirection.West)
                        || (dy == 1 && piece.Incoming == RoadDirection.North)
                        || (dy == -1 && piece.Incoming == RoadDirection.South), "좌표와 도로 방향 불일치");
                    previous = piece;
                }
                // 같은 시드 쌍을 다시 계산한 결과는 모든 계획 필드가 같아야 한다.
                Require(MissionSeedRules.TryBuildTransportRoute(pickupSeed, candidate, out var replay, out _)
                    && replay.Length == transport.Length, "재현 경로 길이 불일치");
                for (var index = 0; index < replay.Length; index++)
                {
                    var a = replay[index];
                    var b = transport[index];
                    Require(a.Index == b.Index && a.GridX == b.GridX && a.GridY == b.GridY
                        && a.Incoming == b.Incoming && a.Outgoing == b.Outgoing
                        && a.EntryLanes == b.EntryLanes && a.ExitLanes == b.ExitLanes, "시드 재현 실패");
                }
                acceptedSeed = candidate;
                Require(acceptedSeed == selectedSeed, "시드 후보 선택과 실제 첫 유효 경로 불일치");
                largestAttempt = Math.Max(largestAttempt, attempt);
                break;
            }
            Require(acceptedSeed != 0, $"1024회 이내 후송 생성 실패: pickup={pickupSeed}, exit={last.Outgoing}, last=({last.GridX},{last.GridY})");
        }
        Require(seenDirections.Count == 4 && seenLanes.Count == 3 && overlapRejections > 0, "방향/차선/겹침 표본 누락");
        Console.WriteLine($"PASS: 4 fixed checks + {SampleCount} pickup/transport pairs; directions=4; lane widths=3; max attempts={largestAttempt}; overlap rejections={overlapRejections}; blocked pickup rejections={blockedRejections}; first overlap={firstOverlap}");
    }

    /// <summary>기대 동작과 다른 결과는 테스트를 즉시 실패시킨다.</summary>
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
