#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>병원 끝 배치의 방향, 주변 충돌 연결과 오른쪽 차고 접근 판정을 검증한다.</summary>
public sealed class HospitalBuildingTests
{
    // 완전 암전에서도 같은 Canvas의 결과창이 덮개보다 앞에 있고 확인 버튼이 최우선 입력을 받아야 한다.
    [Test]
    public void ArrivalCover_KeepsResultVisibleAndClickable()
    {
        var missionRoot = new GameObject("ArrivalMissionTest");
        missionRoot.SetActive(false);
        var ui = new GameObject("ArrivalCanvasTest", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        var events = new GameObject("ArrivalEventsTest", typeof(EventSystem));
        try
        {
            var canvas = ui.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var safeArea = new GameObject("SafeArea", typeof(RectTransform));
            safeArea.transform.SetParent(ui.transform, false);
            var safeRect = (RectTransform)safeArea.transform;
            safeRect.anchorMin = Vector2.zero;
            safeRect.anchorMax = Vector2.one;
            safeRect.offsetMin = safeRect.offsetMax = Vector2.zero;
            var panel = new GameObject("Result", typeof(RectTransform));
            panel.transform.SetParent(safeArea.transform, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
            var buttonRoot = new GameObject("Confirm", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonRoot.transform.SetParent(panel.transform, false);
            ((RectTransform)buttonRoot.transform).sizeDelta = new Vector2(200f, 100f);
            panel.SetActive(false);
            var controller = missionRoot.AddComponent<MissionController>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(MissionController).GetField("safeAreaRoot", flags).SetValue(controller, safeRect);
            typeof(MissionController).GetField("resultPanel", flags).SetValue(controller, panel);
            typeof(MissionController).GetMethod("EnsureArrivalCover", flags).Invoke(controller, null);
            var cover = (CanvasGroup)typeof(MissionController).GetField("arrivalCover", flags).GetValue(controller);
            cover.alpha = 1f;
            cover.blocksRaycasts = true;
            panel.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Assert.That(cover.GetComponentInParent<Canvas>(), Is.SameAs(canvas));
            Assert.That(panel.GetComponentInParent<Canvas>(), Is.SameAs(canvas));
            Assert.That(cover.transform.GetSiblingIndex(), Is.LessThan(safeArea.transform.GetSiblingIndex()));
            var hits = new List<RaycastResult>();
            var pointer = new PointerEventData(events.GetComponent<EventSystem>())
            {
                position = RectTransformUtility.WorldToScreenPoint(null, buttonRoot.transform.position)
            };
            ui.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
            Assert.IsNotEmpty(hits);
            Assert.That(hits[0].gameObject, Is.SameAs(buttonRoot));
            var clicked = false;
            buttonRoot.GetComponent<Button>().onClick.AddListener(() => clicked = true);
            ExecuteEvents.Execute(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Assert.IsTrue(clicked);
            typeof(MissionController).GetMethod("StopArrival", flags).Invoke(controller, null);
            Assert.That(cover.alpha, Is.Zero);
            Assert.IsFalse(cover.blocksRaycasts);
        }
        finally
        {
            Object.DestroyImmediate(missionRoot);
            Object.DestroyImmediate(ui);
            Object.DestroyImmediate(events);
        }
    }

    // 네 방향의 4·6차선 끝에서 병원 한 채와 양옆 연결부 두 개가 서로 겹치지 않아야 한다.
    [TestCase(0, 4)]
    [TestCase(1, 4)]
    [TestCase(2, 4)]
    [TestCase(3, 4)]
    [TestCase(0, 6)]
    [TestCase(1, 6)]
    [TestCase(2, 6)]
    [TestCase(3, 6)]
    public void TerminalBuilding_ClosesBothSides(int direction, int lanes)
    {
        var hospital = Load("Hospital");
        var apartment = Load("Apartment");
        var normal = new[] { apartment, Load("LowRiseVilla"), Load("Cafe"), Load("CommunityCenter") };
        var road = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Road/Road_Straight_{lanes}Lane.prefab").GetComponent<RoadPrefab>();
        var forward = (Vector2)(Quaternion.Euler(0f, 0f, -90f * direction) * Vector3.up);
        var roads = new List<MissionBuildingLayout.Road>();
        for (var index = 0; index < 3; index++)
            roads.Add(new MissionBuildingLayout.Road(road, forward * (index * road.ImageSize.y), direction % 2));
        var terminal = MissionBuildingLayout.CreateEndBuilding(hospital, roads[2], (RoadDirection)direction);
        Assert.IsTrue(MissionBuildingLayout.TryPlan(roads, 0, 12345678, normal,
            new Dictionary<Vector2, List<MissionBuildingLayout.Placement>>(), out var plan, out var error,
            0.3135f, startSideBuilding: apartment, endBuilding: terminal), error);
        var items = plan.Values.SelectMany(value => value).ToArray();
        Assert.That(items.Count(item => item.Prefab == hospital), Is.EqualTo(1));
        Assert.That(items.Count(item => item.Barrier.HasValue), Is.EqualTo(2));
        foreach (var side in items.Where(item => item.Barrier.HasValue))
        {
            Assert.IsTrue(MissionBuildingLayout.Overlaps(side.Barrier.Value, MissionBuildingLayout.GetCollisionBounds(side)));
            Assert.IsTrue(items.Any(item => !item.Barrier.HasValue && item.Prefab != hospital
                && MissionBuildingLayout.Overlaps(side.Barrier.Value, MissionBuildingLayout.GetCollisionBounds(item))));
        }
        for (var first = 0; first < items.Length; first++)
            for (var second = 0; second < first; second++)
                Assert.IsFalse(MissionBuildingLayout.Overlaps(MissionBuildingLayout.GetBounds(items[first]), MissionBuildingLayout.GetBounds(items[second])));
    }

    // 오른쪽 차고만 판정하고 왼쪽 문 앞과 병원 미활성 상태에서는 완료하지 않는다.
    [Test]
    public void Arrival_UsesRightGarageAndSweptMovement()
    {
        var hospital = Object.Instantiate(Load("Hospital"));
        var root = new GameObject("HospitalTest");
        var vehicle = new GameObject("VehicleTest");
        try
        {
            var generator = root.AddComponent<MissionMapGenerator>();
            var arrival = hospital.GetComponentsInChildren<BoxCollider2D>().Single(box => box.isTrigger);
            var box = vehicle.AddComponent<BoxCollider2D>();
            box.size = new Vector2(0.3135f, 0.6f);
            var fields = new SerializedObject(generator);
            fields.FindProperty("endHospital").objectReferenceValue = Load("Hospital");
            fields.ApplyModifiedPropertiesWithoutUndo();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(MissionMapGenerator).GetField("hospitalArrival", flags).SetValue(generator, arrival);
            typeof(MissionMapGenerator).GetField("hasTransport", flags).SetValue(generator, true);
            Physics2D.SyncTransforms();
            var center = (Vector2)arrival.bounds.center;
            Assert.IsTrue(generator.HasReachedHospital(box, center + Vector2.down * 3f, center + Vector2.up * 3f, 0.85f));
            Assert.IsFalse(generator.HasReachedHospital(box, center + Vector2.left * 4f, center + Vector2.left * 4f, 0.85f));
            hospital.gameObject.SetActive(false);
            Assert.IsFalse(generator.HasReachedHospital(box, center, center, 0.85f));
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(vehicle);
            Object.DestroyImmediate(hospital.gameObject);
        }
    }

    // 저장된 프리팹으로 테스트해 이미지와 직렬화 연결의 누락도 확인한다.
    private static BuildingPrefab Load(string name)
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Building/{name}.prefab").GetComponent<BuildingPrefab>();
    }
}
#endif
