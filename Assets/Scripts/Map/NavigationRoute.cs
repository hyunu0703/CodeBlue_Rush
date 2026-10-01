using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

/// <summary>현재 도시의 방향 차선 그래프에서 경로를 계산하고 읽기 전용 결과를 제공한다</summary>
[DisallowMultipleComponent]
public sealed class NavigationRoute : MonoBehaviour
{
    public enum RouteStatus { NoDestination, MapUnavailable, InvalidStart, InvalidDestination, NoPath, Ready, Invalidated }

    [SerializeField] private CityMap map;
    [SerializeField] private Transform player;
    [SerializeField, Min(0f)] private float snapDistance = 2f;

    private readonly List<TrafficLane> lanes = new List<TrafficLane>();
    private readonly List<Vector3> points = new List<Vector3>();
    private readonly HashSet<TrafficLane> watched = new HashSet<TrafficLane>();
    private readonly Dictionary<TrafficLane, TrafficLane> parents = new Dictionary<TrafficLane, TrafficLane>();
    private readonly Queue<TrafficLane> queue = new Queue<TrafficLane>();
    private ReadOnlyCollection<TrafficLane> laneView;
    private ReadOnlyCollection<Vector3> pointView;
    private TrafficLane destinationLane;
    private float destinationDistance;
    private bool laneDestination;
    private Coroutine pending;

    public CityMap Map => map;
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
    }

    // 비활성화 시 결과와 예약된 재탐색 및 구독을 정리한다
    private void OnDisable()
    {
        Unsubscribe();
        CancelPending();
        ResetPath();
        Publish(HasDestination ? RouteStatus.Invalidated : RouteStatus.NoDestination);
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

    // 목적지 설정 또는 외부의 명시적 요청에서만 BFS를 실행한다
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
        BuildPoints();
        foreach (TrafficLane lane in lanes)
        {
            if (watched.Add(lane))
                lane.Changed += HandleInvalidation;
        }
        return Publish(RouteStatus.Ready);
    }

    // 실제 GetNext 간선만 사용하여 최소 차선 전환 횟수의 경로를 찾는다
    private bool Search(TrafficLane start, float from, TrafficLane end, float to)
    {
        parents.Clear();
        queue.Clear();
        parents.Add(start, null);
        queue.Enqueue(start);
        if (start == end && to >= from)
        {
            lanes.Add(start);
            return true;
        }
        while (queue.Count > 0)
        {
            TrafficLane current = queue.Dequeue();
            for (int i = 0; i < current.NextCount; i++)
            {
                TrafficLane next = current.GetNext(i);
                if (!CanTraverse(current, next))
                    continue;
                // 동일 차선 뒤쪽 목적지는 정방향 순환으로 재진입해야 한다
                if (next == end)
                {
                    lanes.Add(end);
                    for (TrafficLane step = current; step; step = parents[step])
                        lanes.Add(step);
                    lanes.Reverse();
                    return true;
                }
                if (parents.ContainsKey(next))
                    continue;
                parents.Add(next, current);
                queue.Enqueue(next);
            }
        }
        return false;
    }

    // 끊어진 연결과 비활성 및 외부 차선을 제외한다
    private bool CanTraverse(TrafficLane from, TrafficLane to)
    {
        return map.ContainsLane(from) && map.ContainsLane(to) && to.Length > 0f && Vector3.Distance(from.EndPoint, to.StartPoint) <= RoadConnection.PositionTolerance && Vector3.Dot(from.EndDirection, to.StartDirection) >= 0.98f;
    }

    // 실제 차선 점을 시작 및 목적지 거리로 잘라 UI용 월드 좌표로 제공한다
    private void BuildPoints()
    {
        for (int i = 0; i < lanes.Count; i++)
        {
            TrafficLane lane = lanes[i];
            float from = i == 0 ? StartDistance : 0f;
            float to = i == lanes.Count - 1 ? EndDistance : lane.Length;
            lane.TrySample(from, out Vector3 first, out _);
            AddPoint(first);
            float distance = 0f;
            for (int p = 1; p < lane.PointCount; p++)
            {
                distance += Vector3.Distance(lane.GetWorldPoint(p - 1), lane.GetWorldPoint(p));
                if (distance > from && distance < to)
                    AddPoint(lane.GetWorldPoint(p));
            }
            lane.TrySample(to, out Vector3 last, out _);
            AddPoint(last);
        }
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
            for (int n = 0; n < lane.NextCount; n++)
                connected |= lane.GetNext(n) == lanes[i + 1];
            if (!connected || !CanTraverse(lane, lanes[i + 1]))
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
        parents.Clear();
        queue.Clear();
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
