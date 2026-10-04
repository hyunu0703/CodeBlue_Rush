using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

/// <summary>실제 도로 연결을 양방향으로 탐색하여 거리 기준 최단 경로를 제공한다</summary>
[DisallowMultipleComponent]
public sealed class NavigationRoute : MonoBehaviour
{
    public enum RouteStatus { NoDestination, MapUnavailable, InvalidStart, InvalidDestination, NoPath, Ready, Invalidated }

    [SerializeField] private CityMap map;
    [SerializeField] private Transform player;
    [SerializeField, Min(0f)] private float snapDistance = 2f;
    [SerializeField] private bool followPlayer;
    private Coroutine tracking;

    private readonly List<TrafficLane> lanes = new List<TrafficLane>();
    private readonly List<Vector3> points = new List<Vector3>();
    private readonly HashSet<TrafficLane> watched = new HashSet<TrafficLane>();
    private readonly List<(TrafficLane lane, float from, float to)> sections = new List<(TrafficLane, float, float)>();
    private ReadOnlyCollection<TrafficLane> laneView;
    private ReadOnlyCollection<Vector3> pointView;
    private TrafficLane destinationLane;
    private float destinationDistance;
    private bool laneDestination;
    private Coroutine pending;

    public CityMap Map => map;
    public Transform Player => player;
    public bool HasDestination { get; private set; }
    public TrafficLane DestinationLane => laneDestination ? destinationLane : null;
    public float DestinationDistance => laneDestination ? destinationDistance : 0f;
    public Vector3 Destination { get; private set; }
    public Vector3 Origin { get; private set; }
    public float StartDistance { get; private set; }
    public float EndDistance { get; private set; }
    public RouteStatus Status { get; private set; }
    public IReadOnlyList<TrafficLane> Lanes => laneView ?? (laneView = lanes.AsReadOnly());
    public IReadOnlyList<Vector3> Points => pointView ?? (pointView = points.AsReadOnly());
    public event Action RouteChanged;

    // 명시적 참조를 연결하고 기존 목적지와 구독을 정리한다
    public void Configure(CityMap city, Transform source)
    {
        Unsubscribe();
        ClearDestination();
        map = city;
        player = source;
        Subscribe();
    }

    // Inspector 참조의 도시 수명 이벤트를 구독한다
    private void OnEnable()
    {
        Subscribe();
        if (HasDestination)
            Recalculate();
        if (Application.isPlaying && followPlayer)
            tracking = StartCoroutine(TrackPlayer());
    }

    // 비활성화 시 결과와 예약된 재탐색 및 구독을 정리한다
    private void OnDisable()
    {
        if (tracking != null)
            StopCoroutine(tracking);
        tracking = null;
        Unsubscribe();
        CancelPending();
        ResetPath();
        Publish(HasDestination ? RouteStatus.Invalidated : RouteStatus.NoDestination);
    }

    // 이동한 경우에만 경로를 갱신하고, 도로를 잠시 벗어난 동안에는 현장 보고를 유지한다.
    private IEnumerator TrackPlayer()
    {
        var interval = new WaitForSeconds(0.5f);
        while (true)
        {
            yield return interval;
            if (HasDestination && player && map && (player.position - Origin).sqrMagnitude >= 4f && map.TryLocate(player.position, snapDistance, out _, out _))
                Recalculate();
        }
    }

    // 중복 없이 도시 이벤트를 구독한다
    private void Subscribe()
    {
        if (!map || !isActiveAndEnabled)
            return;
        map.StateChanged -= HandleMapChanged;
        map.StateChanged += HandleMapChanged;
    }

    // 기존 도시 이벤트를 해제한다
    private void Unsubscribe()
    {
        if (map)
            map.StateChanged -= HandleMapChanged;
    }

    // 월드 목적지를 저장하고 현재 구급차 위치부터 새 경로를 계산한다
    public bool SetDestination(Vector3 position)
    {
        Destination = position;
        destinationLane = null;
        laneDestination = false;
        HasDestination = true;
        return Recalculate();
    }

    // 교차로의 겹친 경로나 도로변 목적지를 정확한 차선과 거리로 지정한다
    public bool SetDestination(TrafficLane lane, float distance)
    {
        destinationLane = lane;
        destinationDistance = distance;
        laneDestination = true;
        HasDestination = true;
        Destination = Vector3.zero;
        return Recalculate();
    }

    // 목적지와 이전 경로를 즉시 제거한다
    public void ClearDestination()
    {
        CancelPending();
        HasDestination = false;
        destinationLane = null;
        Destination = Vector3.zero;
        ResetPath();
        Publish(RouteStatus.NoDestination);
    }

    // 목적지 설정 또는 이동 갱신 시 거리 가중치로 최단 경로를 계산한다
    public bool Recalculate()
    {
        CancelPending();
        ResetPath();
        if (!HasDestination)
            return Publish(RouteStatus.NoDestination);
        if (!isActiveAndEnabled || !map || !map.IsReady)
            return Publish(RouteStatus.MapUnavailable);
        if (!player || !map.TryLocate(player.position, snapDistance, out TrafficLane start, out float from))
            return Publish(RouteStatus.InvalidStart);

        TrafficLane end = destinationLane;
        float to = destinationDistance;
        if (laneDestination)
        {
            if (!map.ContainsLane(end) || !float.IsFinite(to) || to < 0f || to > end.Length || !end.TrySample(to, out Vector3 target, out _))
                return Publish(RouteStatus.InvalidDestination);
            Destination = target;
        }
        else if (!map.TryLocate(Destination, snapDistance, out end, out to))
            return Publish(RouteStatus.InvalidDestination);

        if (!Search(start, from, end, to))
            return Publish(RouteStatus.NoPath);
        Origin = player.position;
        StartDistance = from;
        EndDistance = to;
        start.TrySample(from, out Vector3 snappedOrigin, out _);
        AddPoint(snappedOrigin);
        BuildPoints();
        foreach (TrafficLane lane in lanes)
        {
            if (watched.Add(lane))
                lane.Changed += HandleInvalidation;
        }
        return Publish(RouteStatus.Ready);
    }

    // AI의 방향 차선은 유지하고 내비게이션에서만 역방향 탐색을 허용한다.
    private bool Search(TrafficLane start, float from, TrafficLane end, float to)
    {
        var index = new Dictionary<TrafficLane, int>();
        var available = new List<TrafficLane>();
        for (int i = 0; i < map.LaneCount; i++)
        {
            TrafficLane lane = map.GetLane(i);
            if (map.ContainsLane(lane) && lane.Length > 0f) { index.Add(lane, available.Count); available.Add(lane); }
        }
        int source = available.Count * 2, target = source + 1, count = target + 1;
        var graph = new List<(int node, float cost, TrafficLane lane, float from, float to)>[count];
        for (int i = 0; i < count; i++) graph[i] = new List<(int, float, TrafficLane, float, float)>();
        for (int i = 0; i < available.Count; i++)
        {
            TrafficLane lane = available[i];
            graph[i * 2].Add((i * 2 + 1, lane.Length, lane, 0f, lane.Length));
            graph[i * 2 + 1].Add((i * 2, lane.Length, lane, lane.Length, 0f));
            for (int n = 0; n < lane.NextCount; n++)
            {
                TrafficLane next = lane.GetNext(n);
                if (!next || !index.TryGetValue(next, out int j) || !CanTraverse(lane, next)) continue;
                float gap = Vector3.Distance(lane.EndPoint, next.StartPoint);
                graph[i * 2 + 1].Add((j * 2, gap, null, 0f, 0f));
                graph[j * 2].Add((i * 2 + 1, gap, null, 0f, 0f));
            }
        }
        start.TrySample(from, out Vector3 origin, out _);
        end.TrySample(to, out Vector3 destination, out _);
        RoadChunk startRoad = start.GetComponentInParent<RoadChunk>();
        RoadChunk endRoad = end.GetComponentInParent<RoadChunk>();
        for (int i = 0; i < available.Count; i++)
        {
            TrafficLane lane = available[i];
            RoadChunk road = lane.GetComponentInParent<RoadChunk>();
            bool starts = (lane == start || (startRoad && road == startRoad)) && lane.TryProject(origin, out _, out float startOffset) && startOffset <= 4f;
            bool ends = (lane == end || (endRoad && road == endRoad)) && lane.TryProject(destination, out _, out float endOffset) && endOffset <= 4f;
            float entry = 0, exit = 0, entryGap = 0, exitGap = 0;
            if (starts)
            {
                lane.TryProject(origin, out entry, out float offset); entryGap = Mathf.Sqrt(offset);
                graph[source].Add((i * 2, entry + entryGap, lane, entry, 0f));
                graph[source].Add((i * 2 + 1, lane.Length - entry + entryGap, lane, entry, lane.Length));
            }
            if (ends)
            {
                lane.TryProject(destination, out exit, out float offset); exitGap = Mathf.Sqrt(offset);
                graph[i * 2].Add((target, exit + exitGap, lane, 0f, exit));
                graph[i * 2 + 1].Add((target, lane.Length - exit + exitGap, lane, lane.Length, exit));
            }
            if (starts && ends) graph[source].Add((target, Mathf.Abs(exit - entry) + entryGap + exitGap, lane, entry, exit));
        }
        var costs = new float[count];
        var previous = new int[count];
        var steps = new (TrafficLane lane, float from, float to)[count];
        for (int i = 0; i < count; i++) { costs[i] = float.PositiveInfinity; previous[i] = -1; }
        var pendingNodes = new SortedSet<(float cost, int node)>();
        costs[source] = 0f; pendingNodes.Add((0f, source));
        while (pendingNodes.Count > 0)
        {
            var current = pendingNodes.Min; pendingNodes.Remove(current);
            if (current.cost > costs[current.node]) continue;
            if (current.node == target) break;
            foreach (var edge in graph[current.node])
            {
                float cost = current.cost + edge.cost;
                if (cost >= costs[edge.node]) continue;
                costs[edge.node] = cost; previous[edge.node] = current.node;
                steps[edge.node] = (edge.lane, edge.from, edge.to);
                pendingNodes.Add((cost, edge.node));
            }
        }
        if (previous[target] < 0) return false;
        for (int node = target; node != source; node = previous[node])
            if (steps[node].lane) sections.Add(steps[node]);
        sections.Reverse();
        for (int i = sections.Count - 1; i > 0; i--)
        {
            var before = sections[i - 1];
            var after = sections[i];
            if (before.lane != after.lane || Mathf.Abs(before.to - after.from) > 0.0001f) continue;
            sections[i - 1] = (before.lane, before.from, after.to);
            sections.RemoveAt(i);
        }
        foreach (var section in sections) lanes.Add(section.lane);
        return true;
    }

    private bool CanTraverse(TrafficLane from, TrafficLane to)
    {
        return map.ContainsLane(from) && map.ContainsLane(to) && to.Length > 0f && Vector3.Distance(from.EndPoint, to.StartPoint) <= RoadConnection.PositionTolerance && Vector3.Dot(from.EndDirection, to.StartDirection) >= 0.98f;
    }

    // 각 구간의 진행 방향에 따라 실제 차선 점을 정순 또는 역순으로 표시한다.
    private void BuildPoints()
    {
        foreach (var section in sections)
        {
            TrafficLane lane = section.lane;
            lane.TrySample(section.from, out Vector3 first, out _); AddPoint(first);
            var interior = new List<Vector3>();
            float distance = 0f;
            for (int p = 1; p < lane.PointCount; p++)
            {
                distance += Vector3.Distance(lane.GetWorldPoint(p - 1), lane.GetWorldPoint(p));
                if (distance > Mathf.Min(section.from, section.to) && distance < Mathf.Max(section.from, section.to)) interior.Add(lane.GetWorldPoint(p));
            }
            if (section.to < section.from) interior.Reverse();
            foreach (Vector3 point in interior) AddPoint(point);
            lane.TrySample(section.to, out Vector3 last, out _); AddPoint(last);
        }
        AddPoint(Destination);
    }

    // 연결 경계에서 중복된 좌표를 제거한다
    private void AddPoint(Vector3 point)
    {
        if (points.Count == 0 || (points[points.Count - 1] - point).sqrMagnitude > 0.000001f)
            points.Add(point);
    }

    // 외부 도로 이동이나 편집 후 현재 경로만 검사하고 필요할 때 재탐색한다
    public bool ValidateRoute()
    {
        if (Status != RouteStatus.Ready)
            return false;
        for (int i = 0; i < lanes.Count; i++)
        {
            TrafficLane lane = lanes[i];
            if (!map || !map.ContainsLane(lane))
                return Recalculate();
            if (i == lanes.Count - 1)
                continue;
            bool connected = false;
            TrafficLane next = lanes[i + 1];
            for (int n = 0; n < lane.NextCount; n++)
                connected |= lane.GetNext(n) == next && CanTraverse(lane, next);
            for (int n = 0; n < next.NextCount; n++)
                connected |= next.GetNext(n) == lane && CanTraverse(next, lane);
            if (!connected)
                return Recalculate();
        }
        return true;
    }

    // 도시 교체와 준비 상태 변경을 반영한다
    private void HandleMapChanged()
    {
        Recalculate();
    }

    // 변경된 경로는 즉시 버리고 여러 연결 해제 콜백을 한 번의 재탐색으로 모은다
    private void HandleInvalidation()
    {
        ResetPath();
        Publish(RouteStatus.Invalidated);
        if (HasDestination && Application.isPlaying && isActiveAndEnabled && gameObject.activeInHierarchy && map && map.IsReady && pending == null)
            pending = StartCoroutine(RecalculateAfterChanges());
    }

    // 도로 연결 변경이 완료된 다음 프레임에 한 번만 재탐색한다
    private IEnumerator RecalculateAfterChanges()
    {
        yield return null;
        pending = null;
        Recalculate();
    }

    // 예약된 재탐색과 새 요청의 중복 실행을 방지한다
    private void CancelPending()
    {
        if (pending == null)
            return;
        StopCoroutine(pending);
        pending = null;
    }

    // 결과와 이전 차선에 대한 이벤트 참조를 비운다
    private void ResetPath()
    {
        foreach (TrafficLane lane in watched)
        {
            if (lane)
                lane.Changed -= HandleInvalidation;
        }
        watched.Clear();
        lanes.Clear();
        points.Clear();
        sections.Clear();
        Origin = Vector3.zero;
        StartDistance = 0f;
        EndDistance = 0f;
    }

    // 성공과 실패 상태를 UI에 함께 전달한다
    private bool Publish(RouteStatus status)
    {
        Status = status;
        RouteChanged?.Invoke();
        return status == RouteStatus.Ready;
    }
}
