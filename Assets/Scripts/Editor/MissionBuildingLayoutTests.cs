#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>실제 프리팹 규격으로 시드 재현, 좌우 분류, 연속 제한과 구역 경계를 검사한다.</summary>
public sealed class MissionBuildingLayoutTests
{
    /// <summary>64개 시드, 도로 3종, 회전 4방향에서 연결된 세 구간의 실제 박스를 검증한다.</summary>
    [Test]
    public void SeededLayoutsRespectSidesBoundsAndRuns()
    {
        // 저장된 건물 원본은 테스트에서 변경하지 않는다.
        var prefabs = LoadBuildings();
        for (var lanes = 2; lanes <= 6; lanes += 2)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Road/Road_Straight_" + lanes + "Lane.prefab").GetComponent<RoadPrefab>();
            for (var rotation = 0; rotation < 4; rotation++)
            {
                var up = (Vector2)(Quaternion.Euler(0f, 0f, -90f * rotation) * Vector3.up);
                var roads = Enumerable.Range(0, 3).Select(index => new MissionBuildingLayout.Road(prefab, up * prefab.ImageSize.y * index, rotation)).ToList();
                for (var seed = 0; seed < 64; seed++)
                {
                    var empty = new Dictionary<Vector2, List<MissionBuildingLayout.Placement>>();
                    Assert.IsTrue(MissionBuildingLayout.TryPlan(roads, 0, seed, prefabs, empty, out var result, out var error), error);
                    Assert.IsTrue(MissionBuildingLayout.TryPlan(roads, 0, seed, prefabs, empty, out var repeated, out error), error);
                    Assert.AreEqual(Signature(result), Signature(repeated), "동일 시드는 동일 배치여야 합니다.");
                    for (var side = 1; side <= 2; side++)
                    {
                        // 도로 경계를 포함한 한쪽 전체 건물 순서를 검사한다.
                        var sequence = new List<BuildingPrefab>();
                        foreach (var road in roads)
                        {
                            var row = result[road.Position].Where(item => item.Prefab.Index == side).ToList();
                            Assert.That(row.Count, Is.InRange(4, 5));
                            var lastY = float.NegativeInfinity;
                            foreach (var item in row)
                            {
                                var box = item.Prefab.GetComponent<BoxCollider2D>();
                                var scale = (Vector2)item.Prefab.transform.localScale * item.Scale;
                                var size = Vector2.Scale(box.size, scale);
                                var center = item.Position + Vector2.Scale(box.offset, scale);
                                var edge = prefab.ImageSize.x * (lanes * 0.5f * 84.6f + 64f) / 1254f;
                                Assert.LessOrEqual(Mathf.Abs(center.x) + size.x * 0.5f, prefab.ImageSize.x * 0.5f + 0.001f);
                                Assert.GreaterOrEqual(Mathf.Abs(center.x) - size.x * 0.5f, edge + 0.079f);
                                Assert.LessOrEqual(Mathf.Abs(center.y) + size.y * 0.5f, prefab.ImageSize.y * 0.5f);
                                Assert.AreEqual(side == 1, center.x < 0f);
                                Assert.GreaterOrEqual(center.y - size.y * 0.5f, lastY + 0.119f);
                                lastY = center.y + size.y * 0.5f;
                                sequence.Add(item.Prefab);
                            }
                        }
                        for (var index = 2; index < sequence.Count; index++)
                        {
                            Assert.IsFalse(sequence[index] == sequence[index - 1] && sequence[index] == sequence[index - 2], "도로 경계에서 동일 건물이 3연속입니다.");
                        }
                    }
                }
            }
        }
    }

    /// <summary>후송 추가가 기존 계획을 바꾸지 않으며 새 구간과의 경계에서도 연속 제한을 유지한다.</summary>
    [Test]
    public void AppendedLayoutsPreserveExistingRows()
    {
        var prefabs = LoadBuildings();
        var road = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Road/Road_Straight_6Lane.prefab").GetComponent<RoadPrefab>();
        var roads = new List<MissionBuildingLayout.Road>
        {
            new MissionBuildingLayout.Road(road, Vector2.zero, 0),
            new MissionBuildingLayout.Road(road, Vector2.up * road.ImageSize.y, 0)
        };
        for (var seed = 0; seed < 64; seed++)
        {
            var empty = new Dictionary<Vector2, List<MissionBuildingLayout.Placement>>();
            Assert.IsTrue(MissionBuildingLayout.TryPlan(roads.GetRange(0, 1), 0, seed, prefabs, empty, out var existing, out var error), error);
            var before = Signature(existing);
            Assert.IsTrue(MissionBuildingLayout.TryPlan(roads, 1, seed + 1234, prefabs, existing, out var added, out error), error);
            Assert.AreEqual(before, Signature(existing));
            for (var side = 1; side <= 2; side++)
            {
                var row = existing[Vector2.zero].Concat(added[roads[1].Position]).Where(item => item.Prefab.Index == side).ToArray();
                for (var index = 2; index < row.Length; index++)
                {
                    Assert.IsFalse(row[index].Prefab == row[index - 1].Prefab && row[index].Prefab == row[index - 2].Prefab);
                }
            }
        }
    }

    /// <summary>후송 방향이 역방향이어도 기존 구간 앞에 추가되는 건물 연속 제한을 검사한다.</summary>
    [Test]
    public void ReverseAppendRespectsUpperBoundary()
    {
        var prefabs = LoadBuildings();
        var road = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Road/Road_Straight_2Lane.prefab").GetComponent<RoadPrefab>();
        var roads = new List<MissionBuildingLayout.Road>
        {
            new MissionBuildingLayout.Road(road, Vector2.zero, 0),
            new MissionBuildingLayout.Road(road, -Vector2.up * road.ImageSize.y, 0)
        };
        for (var seed = 0; seed < 64; seed++)
        {
            Assert.IsTrue(MissionBuildingLayout.TryPlan(roads.GetRange(0, 1), 0, seed, prefabs, new Dictionary<Vector2, List<MissionBuildingLayout.Placement>>(), out var existing, out var error), error);
            Assert.IsTrue(MissionBuildingLayout.TryPlan(roads, 1, seed + 5678, prefabs, existing, out var added, out error), error);
            for (var side = 1; side <= 2; side++)
            {
                var row = added[roads[1].Position].Concat(existing[Vector2.zero]).Where(item => item.Prefab.Index == side).ToArray();
                for (var index = 2; index < row.Length; index++)
                {
                    Assert.IsFalse(row[index].Prefab == row[index - 1].Prefab && row[index].Prefab == row[index - 2].Prefab);
                }
            }
        }
    }

    /// <summary>이미 연결된 원본 16종을 읽고 분류를 검증한다.</summary>
    private static BuildingPrefab[] LoadBuildings()
    {
        var result = Enumerable.Range(1, 16).Select(index => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Building/Building_" + index.ToString("00") + ".prefab").GetComponent<BuildingPrefab>()).ToArray();
        Assert.IsTrue(MissionBuildingLayout.Validate(result, out var error), error);
        return result;
    }

    /// <summary>종류, 도로 로컬 위치와 보정 스케일을 비교하는 불변 스냅숏이다.</summary>
    private static string Signature(Dictionary<Vector2, List<MissionBuildingLayout.Placement>> result)
    {
        return string.Join(";", result.SelectMany(pair => pair.Value.Select(item => pair.Key.ToString("R") + item.Prefab.name + item.Position.ToString("R") + item.Scale.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
    }
}
#endif
