#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>공사 분기의 차량 경로 차단, 회전 배치와 풀 재사용 및 실제 2D 충돌을 검증한다.</summary>
public sealed class ConstructionBarrierTests
{
    // 펜스 양쪽에서 시민이 차체 여유를 남기고 돌아서며 후송 확장 후에도 같은 경로를 유지한다.
    [TestCase(2, 0)]
    [TestCase(2, 1)]
    [TestCase(2, 2)]
    [TestCase(2, 3)]
    [TestCase(4, 0)]
    [TestCase(4, 1)]
    [TestCase(4, 2)]
    [TestCase(4, 3)]
    [TestCase(6, 0)]
    [TestCase(6, 1)]
    [TestCase(6, 2)]
    [TestCase(6, 3)]
    public void Citizens_ReverseBeforeBarrierFromBothSides(int lanes, int rotation)
    {
        Assert.IsTrue(RoadLaneLayout.TryCreate(new Vector2(12.54f, 12.54f), lanes, 0, lanes, 0,
            out var layout, out var error), error);
        var side = (rotation + 2) % 4;
        var placement = new RoadLanePlacement(layout, Vector2.zero, rotation, true, side);
        Assert.IsTrue(RoadLaneNetwork.TryBuild(new[] { placement }, out var network, out error), error);
        var outward = Outward(side);
        var plane = 6.27f - layout.LaneWidth;
        var clearance = layout.LaneWidth * 0.425f;
        var sidewalks = new RoadSidewalkNetwork(network);
        var tested = 0;
        for (var edgeId = 0; edgeId < sidewalks.Edges.Count; edgeId++)
        {
            var edge = sidewalks.Edges[edgeId];
            var gapA = Mathf.Abs(Vector2.Dot(sidewalks.Points[edge.A], outward) - plane);
            var gapB = Mathf.Abs(Vector2.Dot(sidewalks.Points[edge.B], outward) - plane);
            if (Mathf.Abs(Mathf.Min(gapA, gapB) - clearance) > 0.001f) continue;
            var simulation = new RoadCitizenSimulation(network, new RoadTrafficSignals(network), 91);
            var citizen = simulation.Spawn(edgeId, 0.5f, 2f);
            var sign = Mathf.Sign(Vector2.Dot(citizen.Position, outward) - plane);
            var previousGap = Mathf.Abs(Vector2.Dot(citizen.Position, outward) - plane);
            var reversed = false;
            for (var step = 0; step < 1000; step++)
            {
                if (step == 200)
                {
                    Assert.IsTrue(RoadLaneNetwork.TryBuild(new[] { placement,
                        new RoadLanePlacement(layout, -outward * 12.54f, rotation) }, out var extended, out error), error);
                    var previousPosition = citizen.Position;
                    var previousEdge = citizen.EdgeId;
                    simulation.SetNetwork(extended, new RoadTrafficSignals(extended));
                    Assert.AreEqual(previousPosition, citizen.Position);
                    Assert.AreEqual(previousEdge, citizen.EdgeId);
                }
                simulation.Tick(0.1f, new RoadTrafficVehicle[0], null, _ => true);
                var signedGap = (Vector2.Dot(citizen.Position, outward) - plane) * sign;
                Assert.GreaterOrEqual(signedGap, clearance - 0.001f, "시민이 펜스 또는 반대편으로 진입했다.");
                if (previousGap <= clearance + 0.001f && signedGap > previousGap + 0.001f) reversed = true;
                previousGap = signedGap;
            }
            Assert.IsTrue(reversed, "펜스 앞에서 실제로 돌아서야 한다.");
            tested++;
        }
        Assert.AreEqual(4, tested, "양쪽 인도의 펜스 앞뒤 구간을 모두 확인해야 한다.");
    }

    // 차로 수와 회전에 관계없이 공사 분기로 향하는 직진/회전 및 차량 생성을 차단한다.
    [TestCase(2, 0)]
    [TestCase(2, 1)]
    [TestCase(2, 2)]
    [TestCase(2, 3)]
    [TestCase(4, 0)]
    [TestCase(4, 1)]
    [TestCase(4, 2)]
    [TestCase(4, 3)]
    [TestCase(6, 0)]
    [TestCase(6, 1)]
    [TestCase(6, 2)]
    [TestCase(6, 3)]
    public void ClosedBranches_RejectSpawnsAndAllIncomingMovements(int lanes, int rotation)
    {
        var network = CreateNetwork(lanes, rotation, false);
        var simulation = new RoadTrafficSimulation(network, 41);
        var route = new List<int>();
        var allowedMovements = 0;
        foreach (var path in network.Paths)
        {
            foreach (var successor in path.Successors)
                Assert.IsTrue(network.Paths[successor].IsTrafficAllowed);
            if (!path.IsTrafficAllowed)
            {
                Assert.AreEqual(0, path.Successors.Count);
                Assert.IsFalse(simulation.TryAddVehicle(path.Id, path.Length * 0.5f,
                    0.3f, 0.2f, 0.5f, null, out _));
                Assert.IsFalse(network.TryFindRoute(path.Id, path.Id, route));
            }
            if (path.RoadOrdinal == 0 && path.Kind == RoadLanePathKind.Intersection && path.IsTrafficAllowed)
            {
                var north = (RoadDirection)rotation;
                var south = (RoadDirection)((rotation + 2) % 4);
                Assert.That(path.Entry == north || path.Entry == south);
                Assert.That(path.Exit == north || path.Exit == south);
                allowedMovements++;
            }
            if (path.RoadOrdinal >= 3) Assert.IsFalse(path.IsTrafficAllowed);
        }
        Assert.Greater(allowedMovements, 0, "미션 경로의 왕복 통행은 유지되어야 한다.");
        // 실제 선택과 경로 전환을 여러 번 진행해 폐쇄 경로를 선택하지 않는지 확인한다.
        var crossedJunction = false;
        foreach (var path in network.Paths)
        {
            if (path.RoadOrdinal != 1 || !path.IsTrafficAllowed) continue;
            if (!simulation.TryAddVehicle(path.Id, path.Length * 0.5f, 0.3f, 0.2f, 0.5f, null, out var vehicle)) continue;
            for (var step = 0; step < 1500 && !vehicle.IsFinished; step++)
            {
                simulation.Tick(0.1f, null);
                Assert.IsTrue(network.Paths[vehicle.PathId].IsTrafficAllowed);
                Assert.Less(network.Paths[vehicle.PathId].RoadOrdinal, 3);
                if (network.Paths[vehicle.PathId].Kind == RoadLanePathKind.Intersection) crossedJunction = true;
            }
            simulation.RemoveVehicle(vehicle);
        }
        Assert.IsTrue(crossedJunction, "일반 경로의 차량은 교차로를 계속 통과해야 한다.");
    }

    // 후송 확장으로 그래프를 다시 만들어도 기존 경로 번호와 폐쇄 상태를 유지한다.
    [Test]
    public void Extension_PreservesPathIdsAndClosures()
    {
        var previous = CreateNetwork(4, 1, false);
        var extended = CreateNetwork(4, 1, true);
        for (var index = 0; index < previous.Paths.Count; index++)
        {
            Assert.AreEqual(previous.Paths[index].RoadOrdinal, extended.Paths[index].RoadOrdinal);
            Assert.AreEqual(previous.Paths[index].Points[0], extended.Paths[index].Points[0]);
            Assert.AreEqual(previous.Paths[index].IsTrafficAllowed, extended.Paths[index].IsTrafficAllowed);
        }
    }

    // 혼합 차로 T자에서도 미션의 회전은 남기고 반대 분기 진입만 제거한다.
    [TestCase(2, 4)]
    [TestCase(4, 2)]
    [TestCase(4, 6)]
    [TestCase(6, 4)]
    [TestCase(6, 6)]
    public void MixedJunction_KeepsMissionTurn(int vertical, int horizontal)
    {
        for (var rotation = 0; rotation < 4; rotation++)
        {
            Assert.IsTrue(RoadLaneLayout.TryCreate(new Vector2(12.54f, 12.54f), 0, horizontal, vertical, horizontal,
                out var junction, out var error), error);
            Assert.IsTrue(RoadLaneLayout.TryCreate(new Vector2(12.54f, 12.54f), horizontal, 0, horizontal, 0,
                out var branch, out error), error);
            var closedSide = (RoadDirection)((3 + rotation) % 4);
            Assert.IsTrue(RoadLaneNetwork.TryBuild(new[] {
                new RoadLanePlacement(junction, Vector2.zero, rotation),
                new RoadLanePlacement(branch, Outward((int)closedSide) * 12.54f, (1 + rotation) % 4, true)
            }, out var network, out error), error);
            var turnCount = 0;
            foreach (var path in network.Paths)
            {
                if (!path.IsTrafficAllowed) continue;
                Assert.AreNotEqual(closedSide, path.Entry);
                Assert.AreNotEqual(closedSide, path.Exit);
                if (path.Kind == RoadLanePathKind.Intersection) turnCount++;
            }
            Assert.Greater(turnCount, 0);
        }
    }

    // 펜스가 도로의 회전된 입구 안에 배치되고 도로 비활성화 및 일반 도로 재사용에 따라 함께 숨는다.
    [TestCase(2, 0)]
    [TestCase(2, 1)]
    [TestCase(2, 2)]
    [TestCase(2, 3)]
    [TestCase(4, 0)]
    [TestCase(4, 1)]
    [TestCase(4, 2)]
    [TestCase(4, 3)]
    [TestCase(6, 0)]
    [TestCase(6, 1)]
    [TestCase(6, 2)]
    [TestCase(6, 3)]
    public void Barrier_RotatesAndResetsWithRoadPool(int lanes, int direction)
    {
        var road = Object.Instantiate(AssetDatabase.LoadAssetAtPath<RoadPrefab>($"Assets/Prefabs/Road/Road_Straight_{lanes}Lane.prefab"));
        try
        {
            road.transform.rotation = Quaternion.Euler(0f, 0f, -90f * ((direction + 2) % 4));
            var barrier = road.gameObject.AddComponent<RoadConstructionBarrier>();
            barrier.Configure(road, RoadDirection.South);
            var body = road.GetComponentInChildren<BoxCollider2D>();
            Assert.IsNotNull(body);
            Assert.IsFalse(body.isTrigger);
            var expectedWidth = road.ImageSize.x * (lanes * 84.6f + 128f) / 1254f;
            Assert.AreEqual(expectedWidth, body.size.x, 0.001f);
            Assert.Less(body.size.x, road.ImageSize.x);
            Assert.Greater(Vector2.Dot(body.transform.position, Outward(direction)), 0f);
            var rail = body.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(rail.sprite);
            Assert.AreEqual(rail.size, body.size);
            foreach (var foot in body.GetComponentsInChildren<SpriteRenderer>())
                if (foot != rail)
                    Assert.LessOrEqual(Mathf.Abs(foot.transform.localPosition.x) + foot.transform.localScale.x * 0.5f,
                        expectedWidth * 0.5f);
            road.gameObject.SetActive(false);
            Assert.IsFalse(body.gameObject.activeInHierarchy);
            road.gameObject.SetActive(true);
            barrier.Hide();
            Assert.IsNull(road.GetComponentInChildren<BoxCollider2D>());
            barrier.Configure(road, RoadDirection.North);
            Assert.IsTrue(body.gameObject.activeInHierarchy);
            Assert.Less(Vector2.Dot(body.transform.position, Outward(direction)), 0f);
        }
        finally { Object.DestroyImmediate(road.gameObject); }
    }

    // 차도·인도는 막고 건물 영역에는 보이지 않는 충돌이 남지 않는지 별도 물리 씬에서 확인한다.
    [TestCase(0f, true)]
    [TestCase(2f, true)]
    [TestCase(3f, false)]
    public void Barrier_PhysicallyStopsVehicle(float across, bool shouldStop)
    {
        var scene = SceneManager.CreateScene("ConstructionCollisionTest", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var road = Object.Instantiate(AssetDatabase.LoadAssetAtPath<RoadPrefab>("Assets/Prefabs/Road/Road_Straight_4Lane.prefab"));
        var car = new GameObject("TestVehicle");
        try
        {
            SceneManager.MoveGameObjectToScene(road.gameObject, scene);
            SceneManager.MoveGameObjectToScene(car, scene);
            road.gameObject.AddComponent<RoadConstructionBarrier>().Configure(road, RoadDirection.South);
            var fence = road.GetComponentInChildren<BoxCollider2D>();
            var rigidbody = car.AddComponent<Rigidbody2D>();
            rigidbody.gravityScale = 0f;
            rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;
            rigidbody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            car.AddComponent<BoxCollider2D>().size = new Vector2(0.3f, 0.5f);
            rigidbody.position = new Vector2(across, fence.transform.position.y - 1f);
            for (var step = 0; step < 100; step++)
            {
                rigidbody.linearVelocity = Vector2.up * 5f;
                scene.GetPhysicsScene2D().Simulate(0.02f);
            }
            if (shouldStop) Assert.Less(rigidbody.position.y, fence.transform.position.y);
            else Assert.Greater(rigidbody.position.y, fence.transform.position.y);
        }
        finally
        {
            Object.DestroyImmediate(car);
            Object.DestroyImmediate(road.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
        }
    }

    // 왕복 미션 축과 양옆 공사 분기를 같은 폭으로 구성하며 확장 시 끝 직선만 추가한다.
    private static RoadLaneNetwork CreateNetwork(int lanes, int rotation, bool extend)
    {
        Assert.IsTrue(RoadLaneLayout.TryCreate(new Vector2(12.54f, 12.54f), lanes, lanes, lanes, lanes, out var cross, out var error), error);
        Assert.IsTrue(RoadLaneLayout.TryCreate(new Vector2(12.54f, 12.54f), lanes, 0, lanes, 0, out var straight, out error), error);
        var roads = new List<RoadLanePlacement> { new RoadLanePlacement(cross, Vector2.zero, rotation) };
        for (var index = 0; index < 4; index++)
        {
            var side = new[] { 0, 2, 1, 3 }[index];
            roads.Add(new RoadLanePlacement(straight, Outward((side + rotation) % 4) * 12.54f,
                (side + rotation) % 4, index >= 2));
        }
        if (extend) roads.Add(new RoadLanePlacement(straight, Outward(rotation) * 25.08f, rotation));
        Assert.IsTrue(RoadLaneNetwork.TryBuild(roads, out var network, out error), error);
        return network;
    }

    // 테스트 배치에 사용하는 시계방향 네 방향 벡터다.
    private static Vector2 Outward(int direction)
    {
        return new[] { Vector2.up, Vector2.right, Vector2.down, Vector2.left }[direction];
    }
}
#endif
