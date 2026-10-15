#if UNITY_EDITOR
using System.Collections;
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
            Assert.IsTrue(generator.TryAppendTransport(transportSeed, out error), error);
            AssertSceneRoadsAllowed();
            Assert.Greater(generator.ActiveRoads.Count, 0);
            yield return null;
        }
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
