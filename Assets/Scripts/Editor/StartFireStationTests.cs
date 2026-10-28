#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>실제 소방서 프리팹의 크기와 시작 도로 예약, 후송 확장 시 중복 및 겹침 방지를 검사한다.</summary>
public sealed class StartFireStationTests
{
    // 기존 저층 건물보다 가로·세로가 최소 3배이며 정적 충돌 영역이 이미지 전체를 덮는지 확인한다.
    [Test]
    public void Prefab_IsThreeTimesNormalBuildingSize()
    {
        var station = LoadBuilding("FireStation");
        var normal = LoadBuilding("LowRiseVilla");
        var size = MissionBuildingLayout.GetBounds(new MissionBuildingLayout.Placement(station, Vector2.zero, 1f)).size;
        var normalSize = MissionBuildingLayout.GetBounds(new MissionBuildingLayout.Placement(normal, Vector2.zero, 1f)).size;
        Assert.That(size.x / normalSize.x, Is.EqualTo(3f).Within(0.001f));
        Assert.That(size.y / normalSize.y, Is.InRange(3f, 3.01f));
        Assert.That(Quaternion.Angle(station.transform.localRotation, Quaternion.Euler(0f, 0f, -180f)), Is.LessThan(0.001f));
        Assert.IsTrue(MissionBuildingLayout.Validate(new[] { station }, out var error, true), error);
        Assert.IsFalse(MissionBuildingLayout.Validate(new[] { station }, out _));
        Assert.IsNull(station.GetComponent<Rigidbody2D>());
        Assert.IsFalse(station.GetComponent<BoxCollider2D>().isTrigger);
    }

    // 4·6차선의 짧은 시작 구간과 긴 구간에서 예약한 건물 한 채만 유지하고 모든 건물의 겹침을 검사한다.
    [TestCase(4, 1)]
    [TestCase(6, 1)]
    [TestCase(4, 2)]
    [TestCase(6, 2)]
    [TestCase(4, 9)]
    [TestCase(6, 9)]
    public void PlanningAndExtension_KeepSingleStationBelowFirstRoad(int lanes, int count)
    {
        var road = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Road/Road_Straight_{lanes}Lane.prefab").GetComponent<RoadPrefab>();
        var station = LoadBuilding("FireStation");
        var prefabs = new[] { LoadBuilding("LowRiseVilla"), LoadBuilding("Cafe"), LoadBuilding("CommunityCenter") };
        var roads = new List<MissionBuildingLayout.Road>();
        for (var index = 0; index < count; index++)
            roads.Add(new MissionBuildingLayout.Road(road, Vector2.up * road.ImageSize.y * index, 0));
        var fixedStation = MissionBuildingLayout.CreateStartBuilding(station, roads[0]);
        var bounds = MissionBuildingLayout.GetBounds(fixedStation);
        Assert.That(bounds.max.y, Is.EqualTo(-road.ImageSize.y * 0.5f - 0.16f).Within(0.0001f));
        Assert.That(bounds.center.x, Is.EqualTo(0f).Within(0.0001f));

        Assert.IsTrue(MissionBuildingLayout.TryPlan(roads, 0, 12345678, prefabs,
            new Dictionary<Vector2, List<MissionBuildingLayout.Placement>>(), out var plan, out var error, 0.3135f, fixedStation, LoadBuilding("Apartment")), error);
        AssertPlan(plan, fixedStation);
        roads.Add(new MissionBuildingLayout.Road(road, Vector2.up * road.ImageSize.y * count, 0));
        Assert.IsTrue(MissionBuildingLayout.TryPlan(roads, count, 87654321, prefabs, plan,
            out var extended, out error, 0.3135f, fixedStation, LoadBuilding("Apartment")), error);
        AssertPlan(extended, fixedStation);

        // 도로 중앙을 점유하는 최초 예약은 실패하고 이전 계획은 수정하지 않아야 한다.
        var previousCount = extended.Values.Sum(items => items.Count);
        var blocked = new MissionBuildingLayout.Placement(station, Vector2.zero, 1f);
        Assert.IsFalse(MissionBuildingLayout.TryPlan(roads, 0, 87654321, prefabs,
            new Dictionary<Vector2, List<MissionBuildingLayout.Placement>>(),
            out _, out error, 0.3135f, blocked));
        Assert.IsNotEmpty(error);
        Assert.AreEqual(previousCount, extended.Values.Sum(items => items.Count));
    }

    // 시작 직선 한 칸 뒤에 T자가 있더라도 도로 밖 소방서의 고정 위치를 후송 확장에서 보존한다.
    [TestCase(4)]
    [TestCase(6)]
    public void ShortStartBeforeJunction_PreservesStationOnExtension(int lanes)
    {
        var straight = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Road/Road_Straight_{lanes}Lane.prefab").GetComponent<RoadPrefab>();
        var junction = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Road/Road_T_{lanes}Lane.prefab").GetComponent<RoadPrefab>();
        var station = LoadBuilding("FireStation");
        var prefabs = new[] { LoadBuilding("LowRiseVilla"), LoadBuilding("Cafe"), LoadBuilding("CommunityCenter") };
        var roads = new List<MissionBuildingLayout.Road>
        {
            new MissionBuildingLayout.Road(straight, Vector2.zero, 0),
            new MissionBuildingLayout.Road(junction, Vector2.up * 12.54f, 0)
        };
        for (var side = -1; side <= 1; side += 2)
            for (var index = 1; index <= 2; index++)
                roads.Add(new MissionBuildingLayout.Road(straight, new Vector2(side * index * 12.54f, 12.54f), 1));
        var candidate = MissionBuildingLayout.CreateStartBuilding(station, roads[0]);
        Assert.IsTrue(MissionBuildingLayout.TryPlan(roads, 0, 75221433, prefabs,
            new Dictionary<Vector2, List<MissionBuildingLayout.Placement>>(), out var plan, out var error, 0.3135f, candidate, LoadBuilding("Apartment")), error);
        var actual = plan.Values.SelectMany(items => items).Single(item => item.Prefab == station);
        Assert.AreEqual(candidate.Position, actual.Position);
        AssertPlan(plan, actual);
        var firstNewRoad = roads.Count;
        roads.Add(new MissionBuildingLayout.Road(straight, new Vector2(37.62f, 12.54f), 1));
        Assert.IsTrue(MissionBuildingLayout.TryPlan(roads, firstNewRoad, 87654321, prefabs, plan,
            out var extended, out error, 0.3135f, candidate, LoadBuilding("Apartment")), error);
        AssertPlan(extended, actual);
    }

    // 소방서 위치·배율과 중복 여부, 스트리밍 소유 도로 및 다른 건물과의 외곽을 함께 확인한다.
    private static void AssertPlan(Dictionary<Vector2, List<MissionBuildingLayout.Placement>> plan,
        MissionBuildingLayout.Placement expected)
    {
        var buildings = plan.Values.SelectMany(items => items).ToList();
        var stations = buildings.FindAll(item => item.Prefab == expected.Prefab);
        Assert.AreEqual(1, stations.Count);
        Assert.AreEqual(expected.Position, stations[0].Position);
        Assert.AreEqual(expected.FlipX, stations[0].FlipX);
        Assert.AreEqual(expected.Rotation, stations[0].Rotation);
        Assert.AreEqual(1f, stations[0].Scale);
        Assert.Contains(Vector2.zero, stations[0].Roads);
        var sides = buildings.FindAll(item => item.Barrier.HasValue);
        Assert.AreEqual(2, sides.Count);
        var stationBody = MissionBuildingLayout.GetCollisionBounds(stations[0]);
        foreach (var side in sides)
        {
            Assert.AreEqual("Apartment", side.Prefab.name);
            Assert.Contains(Vector2.zero, side.Roads);
            Assert.AreEqual(1f, side.Scale);
            var body = MissionBuildingLayout.GetCollisionBounds(side);
            var seam = body.center.x < stationBody.center.x ? stationBody.min.x - body.max.x : body.min.x - stationBody.max.x;
            Assert.That(seam, Is.InRange(0f, 0.3135f * 0.5f));
            var barrier = side.Barrier.Value;
            Assert.IsTrue(MissionBuildingLayout.Overlaps(body, barrier));
            Assert.IsTrue(buildings.Any(other => !other.Barrier.HasValue && other.Prefab != expected.Prefab
                && MissionBuildingLayout.Overlaps(MissionBuildingLayout.GetCollisionBounds(other), barrier)));
        }
        for (var first = 0; first < buildings.Count; first++)
            for (var second = first + 1; second < buildings.Count; second++)
                Assert.IsFalse(MissionBuildingLayout.Overlaps(MissionBuildingLayout.GetBounds(buildings[first]),
                    MissionBuildingLayout.GetBounds(buildings[second])), $"건물 겹침: {first}, {second}");
    }

    // 풀에서 일반 건물로 재사용할 때 시작 구역 전용 충돌이 남지 않는지 확인한다.
    [Test]
    public void StartBarrier_IsDisabledWhenReusedAsNormalBuilding()
    {
        var map = new GameObject("StartBoundaryTestMap");
        var building = Object.Instantiate(LoadBuilding("Apartment"), map.transform);
        try
        {
            var bounds = new Bounds(new Vector3(4f, -6f), new Vector3(1f, 2f));
            building.ConfigureStartBarrier(bounds, map.transform);
            var barrier = building.GetComponentsInChildren<BoxCollider2D>().Single(box => box.gameObject.name == "StartBoundary");
            Assert.IsTrue(barrier.enabled);
            Assert.IsFalse(barrier.isTrigger);
            Physics2D.SyncTransforms();
            Assert.That(Vector2.Distance(barrier.bounds.center, bounds.center), Is.LessThan(0.001f));
            Assert.That(Vector2.Distance(barrier.bounds.size, bounds.size), Is.LessThan(0.001f));
            building.ConfigureStartBarrier(null, map.transform);
            Assert.IsFalse(barrier.enabled);
        }
        finally
        {
            Object.DestroyImmediate(map);
        }
    }

    // 테스트는 씬의 임시 대역 대신 저장된 건물 원본을 사용한다.
    private static BuildingPrefab LoadBuilding(string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Building/{name}.prefab");
        Assert.IsNotNull(prefab);
        return prefab.GetComponent<BuildingPrefab>();
    }
}
#endif
