#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

/// <summary>교차로 신호 UI의 진입 경계, 회전 중 방향 유지와 완전 이탈 조건을 검증한다.</summary>
public sealed class TrafficSignalTests
{
    // 네 방향에서 앞 범퍼가 교차로 시작 경계에 닿는 순간부터 진입 신호를 선택한다.
    [TestCase(0f, -1f, RoadDirection.South)]
    [TestCase(0f, 1f, RoadDirection.North)]
    [TestCase(-1f, 0f, RoadDirection.West)]
    [TestCase(1f, 0f, RoadDirection.East)]
    public void BoundaryContact_SelectsIncomingDirection(float x, float y, RoadDirection expected)
    {
        var network = CreateNetwork();
        var tracker = new RoadTrafficSignalTracker();
        var outward = new Vector2(x, y);
        Assert.IsFalse(tracker.TryTrack(network, Vehicle(outward * 6.01f), -outward, out _, out _));
        Assert.IsTrue(tracker.TryTrack(network, Vehicle(outward * 6f), -outward, out var road, out var entry));
        Assert.AreEqual(0, road);
        Assert.AreEqual(expected, entry);
    }

    // 우회전 후 차체 뒤쪽이 남아 있으면 진입 신호를 유지하고 완전히 나가면 해제한다.
    [Test]
    public void Turning_KeepsEntryUntilEntireVehicleLeaves()
    {
        var network = CreateNetwork();
        var tracker = new RoadTrafficSignalTracker();
        Assert.IsTrue(tracker.TryTrack(network, Vehicle(new Vector2(0f, -6f)), Vector2.up, out _, out _));
        Assert.IsTrue(tracker.TryTrack(network, Vehicle(Vector2.zero), Vector2.right, out _, out var entry));
        Assert.AreEqual(RoadDirection.South, entry);
        Assert.IsTrue(tracker.TryTrack(network, Vehicle(new Vector2(6f, 0f)), Vector2.right, out _, out entry));
        Assert.AreEqual(RoadDirection.South, entry);
        Assert.IsFalse(tracker.TryTrack(network, Vehicle(new Vector2(6.01f, 0f)), Vector2.right, out _, out _));
        Assert.IsFalse(tracker.TryTrack(network, Vehicle(new Vector2(5.9f, 0f)), Vector2.right, out _, out _));
    }

    // 새 맵에서는 같은 도로 번호라도 이전 진입 방향을 이어받지 않는다.
    [Test]
    public void MapReplacement_DropsPreviousEntry()
    {
        var tracker = new RoadTrafficSignalTracker();
        Assert.IsTrue(tracker.TryTrack(CreateNetwork(), Vehicle(new Vector2(0f, -5f)), Vector2.up, out _, out _));
        Assert.IsTrue(tracker.TryTrack(CreateNetwork(), Vehicle(new Vector2(5f, 0f)), Vector2.left, out _, out var entry));
        Assert.AreEqual(RoadDirection.East, entry);
        Assert.IsFalse(tracker.TryTrack(null, Vehicle(Vector2.zero), Vector2.up, out _, out _));
    }

    // 회전한 T자 교차로의 닫힌 방향과 일반 직선 도로에는 신호를 표시하지 않는다.
    [Test]
    public void ClosedSideAndStraightRoad_DoNotShowSignal()
    {
        Assert.IsTrue(RoadLaneLayout.TryCreate(new Vector2(10f, 10f), 2, 2, 2, 0, out var junction, out var error), error);
        Assert.IsTrue(RoadLaneNetwork.TryBuild(new[] { new RoadLanePlacement(junction, Vector2.zero, 1) }, out var network, out error), error);
        var tracker = new RoadTrafficSignalTracker();
        Assert.IsFalse(tracker.TryTrack(network, Vehicle(new Vector2(0f, 5f)), Vector2.down, out _, out _));
        Assert.IsTrue(RoadLaneLayout.TryCreate(new Vector2(10f, 10f), 2, 0, 2, 0, out var straight, out error), error);
        Assert.IsTrue(RoadLaneNetwork.TryBuild(new[] { new RoadLanePlacement(straight, Vector2.zero, 0) }, out network, out error), error);
        Assert.IsFalse(tracker.TryTrack(network, Vehicle(Vector2.zero), Vector2.up, out _, out _));
    }

    // 추적한 진입 방향에서도 실제 신호 시계의 녹색·황색·적색 전환을 따른다.
    [Test]
    public void SignalClock_ChangesGreenYellowRedForTrackedEntry()
    {
        var network = CreateNetwork();
        var tracker = new RoadTrafficSignalTracker();
        Assert.IsTrue(tracker.TryTrack(network, Vehicle(new Vector2(0f, -5f)), Vector2.up, out var road, out var entry));
        var signals = new RoadTrafficSignals(network);
        Assert.AreEqual(RoadTrafficLight.Green, signals.GetLight(road, entry, RoadLaneManeuver.Straight));
        signals.Tick(RoadTrafficSignals.GreenSeconds);
        Assert.AreEqual(RoadTrafficLight.Yellow, signals.GetLight(road, entry, RoadLaneManeuver.Straight));
        signals.Tick(RoadTrafficSignals.YellowSeconds);
        Assert.AreEqual(RoadTrafficLight.Red, signals.GetLight(road, entry, RoadLaneManeuver.Straight));
        signals.Tick(11.999f);
        Assert.AreEqual(RoadTrafficLight.Red, signals.GetLight(road, entry, RoadLaneManeuver.Straight));
        signals.Tick(0.001f);
        Assert.AreEqual(RoadTrafficLight.Green, signals.GetLight(road, entry, RoadLaneManeuver.Straight));
    }

    // 모든 축과 보호 좌회전에서도 녹색 3초·주황 1초·빨강 12초가 반복된다.
    [TestCase(RoadDirection.North, RoadLaneManeuver.Straight, 0f)]
    [TestCase(RoadDirection.South, RoadLaneManeuver.Left, 4f)]
    [TestCase(RoadDirection.East, RoadLaneManeuver.Straight, 8f)]
    [TestCase(RoadDirection.West, RoadLaneManeuver.Left, 12f)]
    public void SignalPhases_KeepRequestedDurations(RoadDirection entry, RoadLaneManeuver maneuver, float offset)
    {
        var signals = new RoadTrafficSignals(CreateNetwork());
        signals.Tick(offset);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            Assert.AreEqual(RoadTrafficLight.Green, signals.GetLight(0, entry, maneuver));
            signals.Tick(2.999f);
            Assert.AreEqual(RoadTrafficLight.Green, signals.GetLight(0, entry, maneuver));
            signals.Tick(0.001f);
            Assert.AreEqual(RoadTrafficLight.Yellow, signals.GetLight(0, entry, maneuver));
            signals.Tick(0.999f);
            Assert.AreEqual(RoadTrafficLight.Yellow, signals.GetLight(0, entry, maneuver));
            signals.Tick(0.001f);
            for (var second = 0; second < 12; second++)
            {
                Assert.AreEqual(RoadTrafficLight.Red, signals.GetLight(0, entry, maneuver));
                signals.Tick(1f);
            }
        }
    }

    // 한 주기 전체에서 서로 교차하는 직진·좌회전 신호가 동시에 켜지지 않게 한다.
    [Test]
    public void SignalPhases_DoNotOverlapConflictingMovements()
    {
        var signals = new RoadTrafficSignals(CreateNetwork());
        for (var step = 0; step < 160; step++)
        {
            var active = 0;
            if (signals.GetLight(0, RoadDirection.North, RoadLaneManeuver.Straight) != RoadTrafficLight.Red) active++;
            if (signals.GetLight(0, RoadDirection.North, RoadLaneManeuver.Left) != RoadTrafficLight.Red) active++;
            if (signals.GetLight(0, RoadDirection.East, RoadLaneManeuver.Straight) != RoadTrafficLight.Red) active++;
            if (signals.GetLight(0, RoadDirection.East, RoadLaneManeuver.Left) != RoadTrafficLight.Red) active++;
            Assert.AreEqual(1, active);
            signals.Tick(0.1f);
        }
    }

    // 경계 조건을 일정하게 검사할 2×2 크기 차체 영역을 만든다.
    private static Rect Vehicle(Vector2 center) => new Rect(center - Vector2.one, Vector2.one * 2f);

    // 조향이나 속도 방향 대신 안내 경로의 진입 면에 앞 범퍼가 닿을 때 표시한다.
    [TestCase(0f, -1f, RoadDirection.South)]
    [TestCase(0f, 1f, RoadDirection.North)]
    [TestCase(-1f, 0f, RoadDirection.West)]
    [TestCase(1f, 0f, RoadDirection.East)]
    public void NavigationEntry_StartsAtRoadSeam(float x, float y, RoadDirection expected)
    {
        var tracker = new RoadTrafficSignalTracker();
        var network = CreateNetwork();
        var outward = new Vector2(x, y);
        var route = new[] { outward * 10f, Vector2.zero, -outward * 10f };
        Assert.IsFalse(tracker.TryTrack(network, Vehicle(outward * 6.01f), Vector2.right, out _, out _, route));
        Assert.IsTrue(tracker.TryTrack(network, Vehicle(outward * 6f), Vector2.right, out _, out var entry, route));
        Assert.AreEqual(expected, entry);
        Assert.IsTrue(tracker.TryTrack(network, Vehicle(Vector2.zero), Vector2.down, out _, out entry, route));
        Assert.AreEqual(expected, entry);
        Assert.IsFalse(tracker.TryTrack(network, Vehicle(-outward * 6.01f), -outward, out _, out _, route));
    }

    // 경로에 없는 교차로나 경로상 교차로의 옆 진입로에 닿아도 신호를 띄우지 않는다.
    [Test]
    public void NavigationEntry_IgnoresOtherApproachesAndOffRouteJunctions()
    {
        var tracker = new RoadTrafficSignalTracker();
        var network = CreateNetwork();
        var route = new[] { new Vector2(0f, -10f), Vector2.zero, new Vector2(10f, 0f) };
        Assert.IsFalse(tracker.TryTrack(network, Vehicle(new Vector2(-6f, 0f)), Vector2.right, out _, out _, route));
        Assert.IsFalse(tracker.TryTrack(network, Vehicle(new Vector2(0f, 6f)), Vector2.down, out _, out _, route));
        var otherRoute = new[] { new Vector2(20f, -10f), new Vector2(20f, 0f) };
        Assert.IsFalse(tracker.TryTrack(network, Vehicle(new Vector2(0f, -6f)), Vector2.up, out _, out _, otherRoute));
    }

    // 네 방향 모두 교차로 바로 앞 직선 도로 시작부터 표시하고 교차로 완전 이탈 때 해제한다.
    [TestCase(0f, -1f, RoadDirection.South)]
    [TestCase(0f, 1f, RoadDirection.North)]
    [TestCase(-1f, 0f, RoadDirection.West)]
    [TestCase(1f, 0f, RoadDirection.East)]
    public void NavigationApproach_StartsAtStraightRoadBeginning(float x, float y, RoadDirection expected)
    {
        var outward = new Vector2(x, y);
        Assert.IsTrue(RoadLaneLayout.TryCreate(new Vector2(10f, 10f), 2, 2, 2, 2, out var junction, out var error), error);
        Assert.IsTrue(RoadLaneLayout.TryCreate(new Vector2(10f, 10f), 2, 0, 2, 0, out var straight, out error), error);
        var placements = new[]
        {
            new RoadLanePlacement(junction, Vector2.zero, 0),
            new RoadLanePlacement(straight, outward * 10f, x == 0f ? 0 : 1)
        };
        Assert.IsTrue(RoadLaneNetwork.TryBuild(placements, out var network, out error), error);
        var tracker = new RoadTrafficSignalTracker();
        var route = new[] { outward * 20f, outward * 10f, Vector2.zero, -outward * 10f };
        Assert.IsFalse(tracker.TryTrack(network, Vehicle(outward * 16.01f), -outward, out _, out _, route));
        Assert.IsTrue(tracker.TryTrack(network, Vehicle(outward * 16f), -outward, out var road, out var entry, route));
        Assert.AreEqual(0, road);
        Assert.AreEqual(expected, entry);
        Assert.IsTrue(tracker.TryTrack(network, Vehicle(outward * 10f), -outward, out _, out _, route));
        Assert.IsTrue(tracker.TryTrack(network, Vehicle(outward * 6f), -outward, out _, out _, route));
        Assert.IsTrue(tracker.TryTrack(network, Vehicle(Vector2.zero), Vector2.right, out _, out entry, route));
        Assert.AreEqual(expected, entry);
        Assert.IsTrue(tracker.TryTrack(network, Vehicle(-outward * 6f), -outward, out _, out _, route));
        Assert.IsFalse(tracker.TryTrack(network, Vehicle(-outward * 6.01f), -outward, out _, out _, route));
        Assert.IsTrue(tracker.TryTrack(network, Vehicle(outward * 10f), -outward, out _, out _, route));
        Assert.IsFalse(tracker.TryTrack(network, Vehicle(outward * 16.01f), outward, out _, out _, route));
    }

    // 실제 도로 레이아웃 생성기를 사용해 독립된 사거리 맵을 만든다.
    private static RoadLaneNetwork CreateNetwork()
    {
        Assert.IsTrue(RoadLaneLayout.TryCreate(new Vector2(10f, 10f), 2, 2, 2, 2, out var layout, out var error), error);
        Assert.IsTrue(RoadLaneNetwork.TryBuild(new[] { new RoadLanePlacement(layout, Vector2.zero, 0) }, out var network, out error), error);
        return network;
    }
}
#endif
