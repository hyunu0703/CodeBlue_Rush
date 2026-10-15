#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>실제 차로 그래프를 고정 스텝으로 실행해 픽업 구역 회피와 안전 대기를 확인한다.</summary>
public sealed class TrafficPickupTests
{
    /// <summary>모든 도로 회전에서 첫 스텝부터 합류하고 픽업 구역 전에 변경을 완료하는지 확인한다.</summary>
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
        // 접근 구간까지 직진 대기하지 않고 최초 판단에서 왼쪽 차로를 예약해야 한다.
        simulation.Tick(0.02f, null);
        Assert.IsTrue(vehicle.IsChangingLane);
        Assert.AreEqual(path.LeftNeighborId, vehicle.TargetLaneId);
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
        // 장애물이 사라진 첫 스텝에 다시 합류를 시작해야 한다.
        simulation.Tick(0.02f, null);
        Assert.IsTrue(vehicle.IsChangingLane);
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

    /// <summary>차로 변경 중 후송을 확장해도 상태를 보존하고 픽업 도로 너머로 계속 직진하는지 확인한다.</summary>
    [TestCase(4, 0)]
    [TestCase(4, 1)]
    [TestCase(4, 2)]
    [TestCase(4, 3)]
    [TestCase(6, 0)]
    [TestCase(6, 1)]
    [TestCase(6, 2)]
    [TestCase(6, 3)]
    public void Tick_ExtendedDuringLaneChange_PreservesVehicleAndDrivesPastPickup(int lanes, int rotation)
    {
        // 실제 차로 배치와 고정 시드로 변경 중인 차량 상태를 만든다.
        var simulation = Create(lanes, rotation, out var path);
        Assert.IsTrue(simulation.TryAddVehicle(path.Id, 0.5f, 1f, 0.4f, 1.6f, null, out var vehicle));
        for (var step = 0; step < 50; step++)
        {
            simulation.Tick(0.02f, null);
        }
        Assert.IsTrue(vehicle.IsChangingLane);
        // 기존 도로의 출구 방향으로 같은 규격의 직선을 정확히 한 칸 연결한다.
        var previous = GetNetwork(simulation);
        var placement = previous.Roads[0];
        path.TrySample(path.Length, out _, out var forward);
        Assert.IsTrue(RoadLaneNetwork.TryBuild(new[] { placement,
            new RoadLanePlacement(placement.Layout, forward * placement.Layout.ImageSize.x, rotation) },
            out var extended, out var error), error);
        // 맵 생성기가 이전 그래프를 무효화한 실제 교체 순서를 재현한다.
        var position = vehicle.Position;
        var speed = vehicle.Speed;
        var distance = vehicle.Distance;
        var targetLane = vehicle.TargetLaneId;
        var id = vehicle.Id;
        typeof(RoadLaneNetwork).GetMethod("Invalidate", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(previous, null);
        Assert.IsTrue((bool)typeof(RoadTrafficSimulation).GetMethod("TryExtendNetwork", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(simulation, new object[] { extended }));
        Assert.AreSame(vehicle, simulation.Vehicles[0]);
        Assert.AreEqual(id, vehicle.Id);
        Assert.AreEqual(position, vehicle.Position);
        Assert.AreEqual(speed, vehicle.Speed);
        Assert.AreEqual(distance, vehicle.Distance);
        Assert.AreEqual(targetLane, vehicle.TargetLaneId);
        // 픽업 구역 전에 합류한 뒤 추가 직선까지 스스로 전진해야 한다.
        for (var step = 0; step < 650; step++)
        {
            simulation.Tick(0.02f, null);
            if (vehicle.PathId == path.Id)
            {
                Assert.LessOrEqual(vehicle.Distance + vehicle.Length * 0.5f, 8f + 0.001f);
            }
        }
        Assert.AreEqual(1, extended.Paths[vehicle.PathId].RoadOrdinal);
        Assert.Greater(vehicle.Speed, 0f);
        Assert.IsFalse(vehicle.IsFinished);
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
