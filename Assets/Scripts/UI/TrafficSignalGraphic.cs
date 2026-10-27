using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>미션 안내 아래에 진입 방향의 직진 신호를 빨강·주황·초록 원 세 개로 표시한다.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TrafficSignalGraphic : MaskableGraphic
{
    private const int Segments = 48;                               // 작은 UI에서도 원의 외곽이 매끄럽게 보이는 분할 수다.
    private readonly RoadTrafficSignalTracker tracker = new RoadTrafficSignalTracker(); // 교차로 통과 중 진입 방향을 보존한다.
    private MissionController mission;                            // 미션 종료와 비활성화 시 표시를 중단할 원본이다.
    private MissionMapGenerator map;                              // 생성된 교차로 배치와 맵 좌표계다.
    private RoadTrafficController traffic;                        // AI와 같은 신호 시계를 사용하는 교통 제어기다.
    private BoxCollider2D vehicle;                                // 진입과 완전 이탈을 판단할 차체다.
    private bool visible;                                         // 교차로 안에서만 원형 메시를 만든다.
    private RoadTrafficLight currentLight;                        // 마지막으로 그린 신호 색상이다.
    private readonly List<Vector2> localRoute = new List<Vector2>(42); // 내비게이션과 같은 경로를 맵 좌표로 보관한다.
    private int routeVersion = -1;                                // 픽업·후송 맵이 바뀔 때만 경로 좌표를 갱신한다.

    // 미션 표시 초기화 시 필수 참조를 한 번 연결한다.
    public void Configure(MissionController owner, MissionMapGenerator generator,
        RoadTrafficController controller, BoxCollider2D body)
    {
        mission = owner;
        map = generator;
        traffic = controller;
        vehicle = body;
        raycastTarget = false;
        tracker.Reset();
        routeVersion = -1;
        SetVerticesDirty();
    }

    private void LateUpdate()
    {
        var show = false;
        var next = currentLight;
        if (mission != null && mission.isActiveAndEnabled && mission.IsMissionActive
            && map != null && map.LaneMapRoot != null && vehicle != null && vehicle.enabled
            && traffic != null && traffic.Simulation != null)
        {
            var root = map.LaneMapRoot;
            if (routeVersion != map.LaneNetworkVersion)
            {
                localRoute.Clear();
                foreach (var point in map.NavigationRoute) localRoute.Add(root.InverseTransformPoint(point));
                tracker.Reset();
                routeVersion = map.LaneNetworkVersion;
            }
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            // 월드 AABB 대신 실제 BoxCollider 모서리를 변환해 맵 회전과 차체 회전을 함께 반영한다.
            for (var index = 0; index < 4; index++)
            {
                var corner = vehicle.offset + Vector2.Scale(vehicle.size * 0.5f,
                    new Vector2((index & 1) == 0 ? -1f : 1f, (index & 2) == 0 ? -1f : 1f));
                var point = (Vector2)root.InverseTransformPoint(vehicle.transform.TransformPoint(corner));
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            var forward = (Vector2)root.InverseTransformVector(vehicle.transform.up);
            var body = vehicle.attachedRigidbody;
            if (body != null && body.linearVelocity.sqrMagnitude > 0.01f)
                forward = root.InverseTransformVector(body.linearVelocity);
            var road = -1;
            var entry = default(RoadDirection);
            show = ReferenceEquals(traffic.SignalNetwork, map.LaneNetwork)
                && tracker.TryTrack(traffic.SignalNetwork, Rect.MinMaxRect(min.x, min.y, max.x, max.y),
                    forward, out road, out entry, localRoute);
            // 원형 신호는 직진 현시를 따른다. 진입 후 조향해도 다른 축의 신호로 바꾸지 않는다.
            if (show) next = traffic.Simulation.Signals.GetLight(road, entry, RoadLaneManeuver.Straight);
        }
        else tracker.Reset();
        if (show == visible && next == currentLight) return;
        visible = show;
        currentLight = next;
        SetVerticesDirty();
    }

    protected override void OnDisable()
    {
        tracker.Reset();
        visible = false;
        base.OnDisable();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (!visible) return;
        var rect = rectTransform.rect;
        var radius = Mathf.Min(rect.height, rect.width / 3.5f) * 0.5f;
        var spacing = radius * 2.5f;
        for (var index = 0; index < 3; index++)
        {
            var center = rect.center + Vector2.right * ((index - 1) * spacing);
            AddDisc(mesh, center, radius, new Color32(20, 25, 31, 245));
            // 색과 위치는 고정하고 현재 신호 하나만 켜서 운전자가 전환 상태를 구분하게 한다.
            var fill = new Color32(48, 48, 48, 255);
            if (index == 0 && currentLight == RoadTrafficLight.Red) fill = new Color32(255, 60, 55, 255);
            else if (index == 1 && currentLight == RoadTrafficLight.Yellow) fill = new Color32(255, 160, 35, 255);
            else if (index == 2 && currentLight == RoadTrafficLight.Green) fill = new Color32(45, 235, 90, 255);
            AddDisc(mesh, center, radius * 0.8f, fill);
        }
    }

    // 배경 테두리와 신호 색상을 같은 원형 UI 메시 안에 겹쳐 그린다.
    private static void AddDisc(VertexHelper mesh, Vector2 center, float radius, Color32 tint)
    {
        var start = mesh.currentVertCount;
        mesh.AddVert(center, tint, Vector2.zero);
        for (var index = 0; index <= Segments; index++)
        {
            var angle = index * Mathf.PI * 2f / Segments;
            mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint, Vector2.zero);
            if (index > 0) mesh.AddTriangle(start, start + index + 1, start + index);
        }
    }
}
