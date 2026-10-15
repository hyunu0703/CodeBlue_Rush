#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>실제 차로 그래프를 고정 스텝으로 실행해 픽업 구역 회피와 안전 대기를 확인한다.</summary>
public sealed class TrafficPickupTests
{
    [TestCase(4, 0)]
    [TestCase(4, 1)]
    [TestCase(4, 2)]
    [TestCase(4, 3)]
    [TestCase(6, 0)]
    [TestCase(6, 1)]
    [TestCase(6, 2)]
    [TestCase(6, 3)]
    public void Tick_ClearAdjacentLane_AvoidsPickup(int lanes, int rotation)
    {
        var simulation = Create(lanes, rotation, out var path);
        Assert.IsTrue(simulation.TryAddVehicle(path.Id, 0.5f, 1f, 0.4f, 1.6f, null, out var vehicle));
        var changed = false;
        for (var step = 0; step < 600; step++)
        {
            simulation.Tick(0.02f, null);
            changed |= vehicle.IsChangingLane;
            if (vehicle.PathId == path.Id)
            {
                Assert.LessOrEqual(vehicle.Distance + vehicle.Length * 0.5f, 8f + 0.001f);
            }
        }
        Assert.IsTrue(changed);
        Assert.AreEqual(path.LeftNeighborId, vehicle.PathId);
        Assert.Greater(vehicle.Distance, 9f);
    }

    [Test]
    public void Tick_BlockedAdjacentLane_WaitsThenResumes()
    {
        var simulation = Create(4, 0, out var path);
        Assert.IsTrue(simulation.TryAddVehicle(path.Id, 0.5f, 1f, 0.4f, 1.6f, null, out var vehicle));
        // 긴 외부 차체로 목표 차로를 막고, 제거 후 동일 차량의 재출발을 확인한다.
        var network = GetNetwork(simulation);
        var adjacent = network.Paths[path.LeftNeighborId];
        adjacent.TrySample(6f, out var position, out var forward);
        var obstacles = new[] { new RoadTrafficObstacle(-1, position, Vector2.zero, forward, 12f, 0.4f) };
        for (var step = 0; step < 700; step++)
        {
            simulation.Tick(0.02f, obstacles);
            Assert.IsFalse(vehicle.IsChangingLane);
            Assert.Less(vehicle.Distance + vehicle.Length * 0.5f, 8f);
        }
        Assert.Less(vehicle.Speed, 0.01f);
        for (var step = 0; step < 600; step++)
        {
            simulation.Tick(0.02f, null);
        }
        Assert.AreEqual(path.LeftNeighborId, vehicle.PathId);
        Assert.Greater(vehicle.Distance, 9f);
    }

    [Test]
    public void Tick_SingleLane_StopsBeforePickup()
    {
        var simulation = Create(2, 0, out var path);
        Assert.IsTrue(simulation.TryAddVehicle(path.Id, 0.5f, 1f, 0.4f, 1.6f, null, out var vehicle));
        for (var step = 0; step < 700; step++)
        {
            simulation.Tick(0.02f, null);
        }
        Assert.Less(vehicle.Distance + vehicle.Length * 0.5f, 8f);
        Assert.Less(vehicle.Speed, 0.01f);
    }

    [Test]
    public void TryAddVehicle_PickupAndApproach_RejectsSpawn()
    {
        var simulation = Create(4, 0, out var path);
        Assert.IsFalse(simulation.TryAddVehicle(path.Id, 4.5f, 1f, 0.4f, 1.6f, null, out _));
        Assert.IsFalse(simulation.TryAddVehicle(path.Id, 6f, 1f, 0.4f, 1.6f, null, out _));
        Assert.IsFalse(simulation.TryAddVehicle(path.Id, 8.5f, 1f, 0.4f, 1.6f, null, out _));
    }

    private static RoadTrafficSimulation Create(int lanes, int rotation, out RoadLanePath path)
    {
        Assert.IsTrue(RoadLaneLayout.TryCreate(new Vector2(12.54f, 12.54f), lanes, 0, lanes, 0,
            out var layout, out var error), error);
        Assert.IsTrue(RoadLaneNetwork.TryBuild(new[] { new RoadLanePlacement(layout, Vector2.zero, rotation) },
            out var network, out error), error);
        path = null;
        foreach (var candidate in network.Paths)
        {
            if (candidate.RightNeighborId < 0)
            {
                path = candidate;
                break;
            }
        }
        Assert.IsNotNull(path);
        var simulation = new RoadTrafficSimulation(network, 73);
        typeof(RoadTrafficSimulation).GetMethod("ConfigurePickupZone", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(simulation, new object[] { path.Id, 8f, 9f, 3f });
        return simulation;
    }

    private static RoadLaneNetwork GetNetwork(RoadTrafficSimulation simulation)
    {
        return (RoadLaneNetwork)typeof(RoadTrafficSimulation).GetField("network", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(simulation);
    }
}
#endif
