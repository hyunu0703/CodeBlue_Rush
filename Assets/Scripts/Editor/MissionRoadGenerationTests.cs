#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>시드 분기 유지와 2차선 생성 금지를 원본 프리팹 및 실제 GamePlay 생성기로 확인한다.</summary>
public sealed class MissionRoadGenerationTests
{
    [Test]
    public void AllDigitCombinationsKeepRouteStructureWithoutTwoLaneRoads()
    {
        var validCount = 0;
        for (var first = 0; first < 10; first++)
        {
            for (var junctions = 0; junctions < 1000; junctions++)
            {
                var seed = 50505050 + first + junctions % 10 * 100
                    + junctions / 10 % 10 * 10000 + junctions / 100 * 1000000;
                if (!MissionSeedRules.TryBuildRoute(seed, out var route, out _))
                {
                    continue;
                }
                validCount++;
                Assert.AreEqual(first <= 4 ? 4 : 6, route[0].EntryLanes);
                Assert.AreEqual(23, route.Length);
                foreach (var piece in route)
                {
                    Assert.IsTrue(RoadPrefabCatalog.IsPlayableRoad(piece.Index), seed.ToString());
                    Assert.That(piece.EntryLanes, Is.EqualTo(4).Or.EqualTo(6));
                    Assert.That(piece.ExitLanes, Is.EqualTo(4).Or.EqualTo(6));
                }
                for (var stage = 0; stage < 3; stage++)
                {
                    var digit = MissionSeedRules.GetDigit(seed, 3 + stage * 2);
                    var piece = route[5 + stage * 6];
                    var turn = digit <= 3 ? 0 : digit <= 6 ? 3 : 1;
                    Assert.AreEqual((RoadDirection)(((int)piece.Incoming + turn) % 4), piece.Outgoing);
                    if (digit >= 4 && digit <= 6)
                    {
                        Assert.AreEqual(piece.EntryLanes, piece.ExitLanes);
                    }
                }
                Assert.IsTrue(MissionSeedRules.TryBuildRoute(seed, out var repeated, out var error), error);
                CollectionAssert.AreEqual(route, repeated);
            }
        }
        Assert.Greater(validCount, 0);
    }

    [Test]
    public void CatalogRejectsAllForbiddenPrefabsAndTwoLaneConnections()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<RoadPrefabCatalog>("Assets/Data/Road/RoadPrefabCatalog.asset");
        Assert.IsNotNull(catalog);
        var forbidden = new[] { RoadIndex.Straight2Lane, RoadIndex.Cross2Lane, RoadIndex.T2Lane,
            RoadIndex.TV2LaneH4Lane, RoadIndex.TV4LaneH2Lane };
        foreach (var index in forbidden)
        {
            Assert.IsFalse(catalog.TryGetPrefab(index, out var prefab));
            Assert.IsNull(prefab);
        }
        for (var index = 1; index <= 13; index++)
        {
            if (!RoadPrefabCatalog.IsPlayableRoad((RoadIndex)index))
            {
                continue;
            }
            Assert.IsTrue(catalog.TryGetPrefab((RoadIndex)index, out var prefab));
            AssertRoadAllowed(prefab);
        }
        // 허용 식별자의 연결부가 잘못 설정되어도 카탈로그가 생성기로 넘기지 않아야 한다.
        var root = new GameObject("TwoLaneCatalogGuardTest");
        var temporaryCatalog = ScriptableObject.CreateInstance<RoadPrefabCatalog>();
        try
        {
            var road = root.AddComponent<RoadPrefab>();
            var roadData = new SerializedObject(road);
            roadData.FindProperty("index").intValue = (int)RoadIndex.Straight4Lane;
            roadData.FindProperty("northLaneCount").intValue = 2;
            roadData.ApplyModifiedPropertiesWithoutUndo();
            var catalogData = new SerializedObject(temporaryCatalog);
            var prefabs = catalogData.FindProperty("prefabs");
            prefabs.arraySize = 1;
            prefabs.GetArrayElementAtIndex(0).objectReferenceValue = road;
            catalogData.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsFalse(temporaryCatalog.TryGetPrefab(RoadIndex.Straight4Lane, out _));
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(temporaryCatalog);
        }
    }

    [UnityTest]
    public IEnumerator GamePlayPickupTransportAndPoolReuseNeverSpawnTwoLaneRoads()
    {
        yield return new EnterPlayMode();
        SceneManager.LoadScene("GamePlay");
        yield return null;
        var generator = Object.FindFirstObjectByType<MissionMapGenerator>();
        Assert.IsNotNull(generator);
        Assert.IsTrue(generator.ValidateConfiguration(out var error), error);
        // 시작 차선 양쪽과 교차로 추가 분기, 후송 연결 및 다음 미션의 풀 재사용을 실행한다.
        foreach (var seed in new[] { 50505050, 50505055, 50505050 })
        {
            Assert.IsTrue(generator.TrySelectTransportSeed(seed, out var transportSeed, out error), error);
            Assert.IsTrue(generator.TryGenerate(seed, out error), error);
            AssertSceneRoadsAllowed();
            AssertPickupContinuation(generator, seed);
            // 픽업 주변에서 실제로 보이는 차량이 유지되는지 검사하도록 플레이어 관측 중심을 옮긴다.
            var ambulance = Object.FindFirstObjectByType<AmbulanceDriveController>().GetComponent<Rigidbody2D>();
            ambulance.position = generator.DestinationPosition;
            ambulance.linearVelocity = Vector2.zero;
            ambulance.angularVelocity = 0f;
            // 실제 GamePlay 컨트롤러가 생성한 활성 차량과 물리 차체를 확장 직전에 보관한다.
            var traffic = Object.FindFirstObjectByType<RoadTrafficController>();
            Assert.IsNotNull(traffic);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            var simulation = traffic.Simulation;
            Assert.IsNotNull(simulation);
            var vehicles = new List<RoadTrafficVehicle>(simulation.Vehicles);
            Assert.Greater(vehicles.Count, 0);
            var positions = new List<Vector2>();
            var speeds = new List<float>();
            foreach (var vehicle in vehicles)
            {
                positions.Add(vehicle.Position);
                speeds.Add(vehicle.Speed);
            }
            var vehicleRoot = (GameObject)typeof(RoadTrafficController)
                .GetField("vehicleRoot", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(traffic);
            var bodies = vehicleRoot.GetComponentsInChildren<Rigidbody2D>();
            Assert.IsTrue(generator.TryAppendTransport(transportSeed, out error), error);
            // 다음 물리 이동 전의 교체 경계를 직접 실행하여 위치/속도와 동일 오브젝트 보존을 검사한다.
            typeof(RoadTrafficController).GetMethod("ResetNetwork", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(traffic, new object[] { generator.LaneNetwork });
            Assert.AreSame(simulation, traffic.Simulation);
            for (var index = 0; index < vehicles.Count; index++)
            {
                CollectionAssert.Contains(simulation.Vehicles, vehicles[index]);
                Assert.AreEqual(positions[index], vehicles[index].Position);
                Assert.AreEqual(speeds[index], vehicles[index].Speed);
            }
            foreach (var body in bodies)
            {
                Assert.IsTrue(body != null && body.gameObject.activeInHierarchy && body.simulated);
            }
            AssertSceneRoadsAllowed();
            Assert.Greater(generator.ActiveRoads.Count, 0);
            yield return null;
        }
    }

    /// <summary>픽업 위치가 원래 끝 도로에 남고 같은 방향의 직선 한 칸으로 모든 차선이 이어지는지 검사한다.</summary>
    private static void AssertPickupContinuation(MissionMapGenerator generator, int seed)
    {
        Assert.IsTrue(MissionSeedRules.TryBuildRoute(seed, out var route, out var error), error);
        // 추가 직선은 활성 목록의 마지막, 픽업 도로는 그 바로 앞이다.
        var pickupOrdinal = generator.ActiveRoads.Count - 2;
        var continuation = generator.ActiveRoads[generator.ActiveRoads.Count - 1];
        var pickup = generator.ActiveRoads[pickupOrdinal];
        Assert.AreEqual(pickup.Index, continuation.Index);
        Assert.AreEqual(pickup.transform.rotation, continuation.transform.rotation);
        Assert.That(Vector2.Distance(pickup.transform.localPosition, continuation.transform.localPosition),
            Is.EqualTo(pickup.ImageSize.x).Within(0.001f));
        // 픽업 구역 크기는 실제 맵 스케일과 차로 폭에서 환산한다.
        var laneWidth = generator.LaneNetwork.Paths[0].Width;
        var worldWidth = generator.LaneMapRoot.TransformVector(Vector3.right * laneWidth).magnitude;
        Assert.IsTrue(generator.TryConfigurePickup(new Vector2(worldWidth * 0.6f, worldWidth * 1.2f),
            out _, out _, out error), error);
        foreach (var path in generator.LaneNetwork.Paths)
        {
            if (path.RoadOrdinal != pickupOrdinal || path.Exit != route[route.Length - 1].Outgoing)
            {
                continue;
            }
            Assert.Greater(path.Successors.Count, 0);
            Assert.AreEqual(generator.ActiveRoads.Count - 1,
                generator.LaneNetwork.Paths[path.Successors[0]].RoadOrdinal);
        }
        // 실제 픽업 구역 등록도 추가 도로가 아닌 원래 도로를 가리켜야 한다.
        var zone = new object[] { 0, 0f, 0f };
        Assert.IsTrue((bool)typeof(MissionMapGenerator)
            .GetMethod("TryGetPickupTrafficZone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(generator, zone));
        Assert.AreEqual(pickupOrdinal, generator.LaneNetwork.Paths[(int)zone[0]].RoadOrdinal);
    }

    [UnityTearDown]
    public IEnumerator ExitRuntimeTest()
    {
        if (Application.isPlaying)
        {
            yield return new ExitPlayMode();
        }
    }

    private static void AssertSceneRoadsAllowed()
    {
        var roads = Object.FindObjectsByType<RoadPrefab>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Assert.Greater(roads.Length, 0);
        foreach (var road in roads)
        {
            AssertRoadAllowed(road);
        }
    }

    private static void AssertRoadAllowed(RoadPrefab road)
    {
        Assert.IsTrue(RoadPrefabCatalog.IsPlayableRoad(road.Index), road.name);
        for (var direction = 0; direction < 4; direction++)
        {
            Assert.That(road.GetLaneCount((RoadDirection)direction), Is.EqualTo(0).Or.EqualTo(4).Or.EqualTo(6), road.name);
        }
    }
}
#endif
