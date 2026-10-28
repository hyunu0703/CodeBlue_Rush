#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>차량 회수 조건과 실제 카메라 시야를 조합하고 경로 끝에 남은 차량의 뒤차 정지를 검증한다.</summary>
public sealed class TrafficVisibilityTests
{
    private GameObject root;                    // 테스트가 소유하는 카메라·차량·맵의 정리 루트다.
    private RoadTrafficController controller;   // 실제 런타임의 회수 판정을 호출할 대상이다.
    private RoadLaneNetwork network;            // 연결이 끝나는 단일 직선 도로다.
    private Camera camera;                      // 이동·회전 후에도 현재 시야의 차체를 보호하는지 검사한다.
    private GameObject road;                    // 도로 스트리밍으로 비활성화되는 조건을 재현한다.
    private GameObject car;                     // AI 좌표와 다른 실제 차체 위치를 재현한다.
    private RoadTrafficVehicle vehicle;         // 회수 후보의 경로·좌표·종료 상태다.
    private object instance;                    // 외부 API를 추가하지 않고 풀 인스턴스를 구성한다.
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic; // 내부 관측 범위다.

    // 프리팹 및 저장 데이터와 독립된 실제 Unity 컴포넌트로 시야와 회수 조건을 준비한다.
    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Traffic visibility test");
        var owner = CreateChild("Controller");
        owner.SetActive(false);
        controller = owner.AddComponent<RoadTrafficController>();
        var generator = CreateChild("Map").AddComponent<MissionMapGenerator>();
        road = CreateChild("Road");
        var roads = (List<RoadPrefab>)typeof(MissionMapGenerator).GetField("activeRoads", Fields).GetValue(generator);
        roads.Add(road.AddComponent<RoadPrefab>());
        network = CreateNetwork();
        camera = CreateChild("Camera").AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.aspect = 1f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.enabled = false;
        var player = CreateChild("Player").AddComponent<Rigidbody2D>();
        player.simulated = false;
        Set(controller, "mapGenerator", generator);
        Set(controller, "mapRoot", root.transform);
        Set(controller, "mapScale", 1f);
        Set(controller, "network", network);
        Set(controller, "trafficCamera", camera);
        Set(controller, "player", player);
        RefreshView();
        car = CreateChild("Car");
        var body = car.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        var collider = car.AddComponent<BoxCollider2D>();
        vehicle = new RoadTrafficVehicle();
        SetVehicle("Position", Vector2.zero);
        instance = Activator.CreateInstance(typeof(RoadTrafficController).GetNestedType("VehicleInstance", BindingFlags.NonPublic), true);
        Set(instance, "Root", car);
        Set(instance, "Body", body);
        Set(instance, "Collider", collider);
        Set(instance, "Vehicle", vehicle);
        Set(instance, "VisualRadius", 0.75f);
    }

    // 테스트 생성물만 해제하고 실제 씬과 영구 에셋은 건드리지 않는다.
    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
    }

    // 경로 종료·도로 비활성·거리 초과 각각에서 보이는 차체는 보존하고 시야 밖에서는 반환한다.
    [TestCase(0, 0f)]
    [TestCase(0, 90f)]
    [TestCase(1, 0f)]
    [TestCase(1, 90f)]
    [TestCase(2, 0f)]
    [TestCase(2, 90f)]
    public void ReturnConditions_KeepVisibleCarsAndReleaseOffscreenCars(int reason, float rotation)
    {
        camera.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
        RefreshView();
        if (reason == 0) SetVehicle("IsFinished", true);
        if (reason == 1) road.SetActive(false);
        if (reason == 2)
        {
            Set(controller, "searchCenter", new Vector2(-100f, -100f));
            Set(controller, "despawnRadius", 1f);
        }
        Assert.That(ShouldReturn(), Is.False, "화면 안 차량은 회수 조건에 걸려도 유지해야 한다.");
        SetVehicle("Position", new Vector2(100f, 100f));
        Assert.That(ShouldReturn(), Is.False, "물리 보간 중 실제 차체가 아직 보이는 경우도 보호해야 한다.");
        car.transform.position = new Vector3(100f, 100f, 0f);
        Physics2D.SyncTransforms();
        Assert.That(ShouldReturn(), Is.True, "AI와 실제 차체 모두 시야 밖이면 정상 반환해야 한다.");
    }

    // 생성 주기 사이에 카메라가 이동하거나 회전해도 다음 물리 스텝부터 현재 시야를 사용한다.
    [Test]
    public void MovingCamera_RefreshesVisibilityBeforeReturn()
    {
        SetVehicle("IsFinished", true);
        SetVehicle("Position", new Vector2(30f, 0f));
        car.transform.position = new Vector3(30f, 0f, 0f);
        Physics2D.SyncTransforms();
        Assert.That(ShouldReturn(), Is.True);
        camera.transform.SetPositionAndRotation(new Vector3(30f, 0f, -10f), Quaternion.Euler(0f, 0f, 90f));
        RefreshView();
        Assert.That(ShouldReturn(), Is.False);
        Set(controller, "trafficCamera", null);
        Assert.That(ShouldReturn(), Is.False, "카메라를 잃으면 안전하게 보존한다.");
    }

    // 경로 끝에서 보존되는 앞차가 관측에서 빠져 뒤차가 같은 위치까지 겹쳐 전진하면 안 된다.
    [Test]
    public void FinishedLeader_RemainsAnObstacleUntilRemoved()
    {
        var terminalNetwork = CreateNetwork();
        var simulation = new RoadTrafficSimulation(terminalNetwork, 7);
        var path = terminalNetwork.Paths[0];
        Assert.That(simulation.TryAddVehicle(path.Id, path.Length - 0.5f, 1f, path.Width * 0.5f,
            path.Width, null, out var leader), Is.True);
        simulation.Tick(0.05f, null);
        Assert.That(leader.IsFinished, Is.True);
        Assert.That(simulation.TryAddVehicle(path.Id, path.Length - 5f, 1f, path.Width * 0.5f,
            path.Width, null, out var follower), Is.True);
        for (var step = 0; step < 600; step++) simulation.Tick(0.05f, null);
        Assert.That(leader.Distance - follower.Distance, Is.GreaterThanOrEqualTo(1f));
        Assert.That(follower.Speed, Is.LessThan(0.01f));
        simulation.RemoveVehicle(leader);
        for (var step = 0; step < 600; step++) simulation.Tick(0.05f, null);
        Assert.That(follower.IsFinished, Is.True, "앞차가 실제 회수되면 뒤차도 경로 끝까지 전진한다.");
    }

    // 같은 배치의 네트워크 생성기를 사용해 막다른 경로와 차로 폭을 준비한다.
    private static RoadLaneNetwork CreateNetwork()
    {
        Assert.That(RoadLaneLayout.TryCreate(new Vector2(20f, 20f), 2, 0, 2, 0, out var layout, out var error), Is.True, error);
        Assert.That(RoadLaneNetwork.TryBuild(new[] { new RoadLanePlacement(layout, Vector2.zero, 0) }, out var value, out error), Is.True, error);
        return value;
    }

    // 모든 임시 오브젝트가 테스트 루트 정리와 함께 제거되도록 소유 관계를 만든다.
    private GameObject CreateChild(string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(root.transform, false);
        return child;
    }

    // 실제 물리 스텝의 카메라 시야 갱신 경로를 사용한다.
    private void RefreshView()
    {
        typeof(RoadTrafficController).GetMethod("UpdateSearchArea", Fields).Invoke(controller, null);
    }

    // 실제 런타임 회수 판정의 결과를 관측한다.
    private bool ShouldReturn()
    {
        return (bool)typeof(RoadTrafficController).GetMethod("ShouldReturnVehicle", Fields).Invoke(controller, new[] { instance });
    }

    // 테스트에서만 경로 진행 중 발생하는 상태를 재현한다.
    private void SetVehicle(string name, object value)
    {
        typeof(RoadTrafficVehicle).GetProperty(name).SetValue(vehicle, value);
    }

    // 직렬화 설정과 풀 내부 상태를 테스트 범위 안에서 주입한다.
    private static void Set(object target, string name, object value)
    {
        target.GetType().GetField(name, Fields).SetValue(target, value);
    }
}
#endif
