using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>차량 표식을 고정하고 실제 도로와 경로를 주행 방향 기준으로 표시한다.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class NavigationUI : MaskableGraphic
{
    [SerializeField] private NavigationRoute navigation;
    [SerializeField, Min(1f)] private float lineWidth = 3f;
    [SerializeField] private Color startColor = new Color(0.15f, 1f, 0.3f);
    [SerializeField] private Color destinationColor = new Color(1f, 0.22f, 0.25f);
    [SerializeField] private Color roadColor = Color.white;
    [SerializeField, Min(10f)] private float viewDistance = 50f;
    [SerializeField, Range(0.15f, 0.45f)] private float playerHeight = 0.25f;
    private readonly List<Vector2> roadSegments = new List<Vector2>();
    private readonly HashSet<RoadChunk> roads = new HashSet<RoadChunk>();
    private CityMap cachedMap;
    private Vector3 lastPosition;
    private Quaternion lastRotation;

    public void Bind(NavigationRoute route)
    {
        if (navigation) navigation.RouteChanged -= Refresh;
        if (cachedMap) cachedMap.StateChanged -= CacheRoads;
        navigation = route;
        cachedMap = navigation ? navigation.Map : null;
        if (isActiveAndEnabled)
        {
            if (navigation) navigation.RouteChanged += Refresh;
            if (cachedMap) cachedMap.StateChanged += CacheRoads;
        }
        raycastTarget = false;
        CacheRoads();
    }
    protected override void OnEnable() { base.OnEnable(); Bind(navigation); }
    protected override void OnDisable()
    {
        if (navigation) navigation.RouteChanged -= Refresh;
        if (cachedMap) cachedMap.StateChanged -= CacheRoads;
        base.OnDisable();
    }
    // 맵 수명이 바뀔 때만 좌표를 확보한다. 매 프레임 Scene을 검색하지 않는다.
    private void CacheRoads()
    {
        roadSegments.Clear(); roads.Clear();
        if (cachedMap && cachedMap.IsReady)
        {
            for (int i = 0; i < cachedMap.LaneCount; i++)
            {
                TrafficLane lane = cachedMap.GetLane(i);
                if (!lane) continue;
                RoadChunk road = lane.GetComponentInParent<RoadChunk>();
                if (!road || !roads.Add(road)) continue;
                if (road.LaneCount == 2)
                {
                    TrafficLane forward = road.GetLane(0), reverse = road.GetLane(1);
                    if (!forward || !reverse) continue;
                    var samples = new SortedSet<float> { 0f, 1f };
                    for (int j = 0; j < 2; j++)
                    {
                        TrafficLane path = road.GetLane(j);
                        float distance = 0f;
                        for (int p = 1; p < path.PointCount; p++)
                        {
                            distance += Vector3.Distance(path.GetWorldPoint(p - 1), path.GetWorldPoint(p));
                            float progress = Mathf.Clamp01(distance / Mathf.Max(path.Length, 0.0001f));
                            samples.Add(j == 0 ? progress : 1f - progress);
                        }
                    }
                    // 반대 방향의 두 차선을 도로 중심선 하나로 합친다.
                    Vector2 previous = Vector2.zero;
                    bool first = true;
                    foreach (float progress in samples)
                    {
                        if (!forward.TrySample(progress * forward.Length, out Vector3 a, out _) || !reverse.TrySample((1f - progress) * reverse.Length, out Vector3 b, out _)) continue;
                        Vector2 center = (a + b) * 0.5f;
                        if (!first) CacheSegment(previous, center);
                        previous = center;
                        first = false;
                    }
                }
                else
                {
                    // 교차로의 모든 AI 회전 경로 대신 실제 입구들을 연결한다.
                    Vector2 center = Vector2.zero;
                    for (int j = 0; j < road.ConnectionCount; j++) center += (Vector2)road.GetConnection(j).Position;
                    if (road.ConnectionCount > 0) center /= road.ConnectionCount;
                    for (int j = 0; j < road.ConnectionCount; j++) CacheSegment(center, road.GetConnection(j).Position);
                }
            }
        }
        Refresh();
    }
    private void CacheSegment(Vector2 a, Vector2 b)
    {
        if ((a - b).sqrMagnitude < 0.0001f) return;
        roadSegments.Add(a); roadSegments.Add(b);
    }
    private void Refresh() => SetVerticesDirty();
    private void LateUpdate()
    {
        if (!navigation) return;
        if (cachedMap != navigation.Map) { Bind(navigation); return; }
        Transform player = navigation.Player;
        if (!player || (player.position == lastPosition && player.rotation == lastRotation)) return;
        lastPosition = player.position; lastRotation = player.rotation;
        Refresh();
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (!navigation || !navigation.Player || !cachedMap || !cachedMap.IsReady) return;
        Rect rect = GetPixelAdjustedRect();
        Vector2 anchor = new Vector2(rect.center.x, rect.yMin + rect.height * playerHeight);
        float scale = rect.height / Mathf.Max(10f, viewDistance);
        Vector2 position = navigation.Player.position;
        float angle = navigation.Player.eulerAngles.z * Mathf.Deg2Rad;
        Vector2 right = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        Vector2 forward = new Vector2(-right.y, right.x);
        Rect clip = Rect.MinMaxRect(rect.xMin + 6, rect.yMin + 6, rect.xMax - 6, rect.yMax - 6);
        for (int i = 0; i < roadSegments.Count; i += 2)
            Line(mesh, Project(roadSegments[i], position, right, forward, scale, anchor), Project(roadSegments[i + 1], position, right, forward, scale, anchor), 2f, roadColor, clip);
        var points = navigation.Points;
        if (navigation.Status == NavigationRoute.RouteStatus.Ready && points.Count > 1)
        {
            // 재탐색 사이에도 이미 지나온 구간은 표시하지 않는다.
            int closest = 1; float best = float.PositiveInfinity;
            Vector2 projected = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 a = points[i - 1], delta = (Vector2)points[i] - a;
                float t = delta.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(position - a, delta) / delta.sqrMagnitude) : 0;
                Vector2 point = a + delta * t;
                float distance = (point - position).sqrMagnitude;
                if (distance < best) { best = distance; closest = i; projected = point; }
            }
            for (int pass = 0; pass < 2; pass++)
            {
                Vector2 previous = anchor;
                Vector2 next = Project(projected, position, right, forward, scale, anchor);
                float width = pass == 0 ? lineWidth + 3 : lineWidth;
                Color tint = pass == 0 ? new Color(0.03f, 0.2f, 0.38f) : color;
                Line(mesh, previous, next, width, tint, clip); previous = next;
                for (int i = closest; i < points.Count; i++)
                {
                    next = Project(points[i], position, right, forward, scale, anchor);
                    Line(mesh, previous, next, width, tint, clip); previous = next;
                }
            }
        }
        if (navigation.HasDestination)
        {
            Vector2 destination = Project(navigation.Destination, position, right, forward, scale, anchor);
            Vector2 delta = destination - anchor; float t = 1;
            // 화면 밖 목적지는 목적지 방향의 테두리에 표시한다.
            if (delta.x > 0) t = Mathf.Min(t, (clip.xMax - anchor.x) / delta.x);
            if (delta.x < 0) t = Mathf.Min(t, (clip.xMin - anchor.x) / delta.x);
            if (delta.y > 0) t = Mathf.Min(t, (clip.yMax - anchor.y) / delta.y);
            if (delta.y < 0) t = Mathf.Min(t, (clip.yMin - anchor.y) / delta.y);
            destination = anchor + delta * t;
            Circle(mesh, destination, Color.white, 5); Circle(mesh, destination, destinationColor, 4);
        }
        Circle(mesh, anchor + Vector2.down, new Color(0, 0, 0, 0.5f), 10);
        Circle(mesh, anchor, startColor, 9);
        Quad(mesh, anchor + new Vector2(-5, -3), anchor + new Vector2(-3, -4), anchor + new Vector2(0, 2), anchor + new Vector2(0, 6), Color.white);
        Quad(mesh, anchor + new Vector2(0, 6), anchor + new Vector2(0, 2), anchor + new Vector2(3, -4), anchor + new Vector2(5, -3), Color.white);
    }
    private static Vector2 Project(Vector2 point, Vector2 position, Vector2 right, Vector2 forward, float scale, Vector2 anchor)
    {
        Vector2 delta = point - position;
        return anchor + new Vector2(Vector2.Dot(delta, right), Vector2.Dot(delta, forward)) * scale;
    }
    // 선을 UI 경계에서 잘라 다른 HUD로 넘치지 않게 한다.
    private static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint, Rect clip)
    {
        Vector2 delta = b - a; float from = 0, to = 1;
        if (!Clip(-delta.x, a.x - clip.xMin, ref from, ref to) || !Clip(delta.x, clip.xMax - a.x, ref from, ref to) || !Clip(-delta.y, a.y - clip.yMin, ref from, ref to) || !Clip(delta.y, clip.yMax - a.y, ref from, ref to)) return;
        b = a + delta * to; a += delta * from;
        if ((b - a).sqrMagnitude < 0.001f) return;
        Vector2 side = new Vector2(-delta.y, delta.x).normalized * width * 0.5f;
        Quad(mesh, a - side, a + side, b + side, b - side, tint);
    }
    private static bool Clip(float p, float q, ref float from, ref float to)
    {
        if (Mathf.Abs(p) < 0.00001f) return q >= 0;
        float t = q / p;
        if (p < 0) { if (t > to) return false; from = Mathf.Max(from, t); }
        else { if (t < from) return false; to = Mathf.Min(to, t); }
        return true;
    }
    private static void Circle(VertexHelper mesh, Vector2 center, Color tint, float radius)
    {
        int index = mesh.currentVertCount; mesh.AddVert(center, tint, Vector2.zero);
        const int count = 24;
        for (int i = 0; i < count; i++)
        {
            float angle = i * Mathf.PI * 2 / count;
            mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint, Vector2.zero);
        }
        for (int i = 0; i < count; i++) mesh.AddTriangle(index, index + 1 + i, index + 1 + (i + 1) % count);
    }
    private static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
    {
        int index = mesh.currentVertCount;
        mesh.AddVert(a, tint, Vector2.zero); mesh.AddVert(b, tint, Vector2.zero);
        mesh.AddVert(c, tint, Vector2.zero); mesh.AddVert(d, tint, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2); mesh.AddTriangle(index, index + 2, index + 3);
    }
}
