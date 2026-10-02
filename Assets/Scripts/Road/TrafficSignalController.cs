using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>기존 교차로 하나의 보호 신호와 실제 경로 간 점유를 관리한다</summary>
[DisallowMultipleComponent]
public sealed class TrafficSignalController : MonoBehaviour
{
    [SerializeField] private RoadChunk road;
    [SerializeField] private VehicleStopZone[] zones;
    [SerializeField, Min(0.1f)] private float greenDuration = 8f;
    [SerializeField, Min(0.1f)] private float changeDelay = 1f;
    private static readonly Dictionary<TrafficLane, TrafficSignalController> signals = new Dictionary<TrafficLane, TrafficSignalController>();
    private readonly Dictionary<TrafficLane, VehicleStopZone> entries = new Dictionary<TrafficLane, VehicleStopZone>();
    private readonly Dictionary<TrafficLane, int> routes = new Dictionary<TrafficLane, int>();
    private readonly Dictionary<Collider2D, int> occupants = new Dictionary<Collider2D, int>();
    private readonly Collider2D[] hits = new Collider2D[32];
    private bool[,] conflicts;
    private bool[,] pedestrianConflicts;
    private int[] counts;
    private Coroutine routine;
    private TrafficLane[] registered;
    private int phase = -1;
    public int ZoneCount => zones == null ? 0 : zones.Length;
    public int OccupantCount => occupants.Count;

    // 명시적으로 연결된 횡단 및 정지 위치만 읽기 전용으로 제공한다
    public VehicleStopZone GetZone(int index)
    {
        return index >= 0 && index < ZoneCount ? zones[index] : null;
    }

    // 현재 차선에 등록된 교차로를 상수 시간에 조회한다
    public static TrafficSignalController ForLane(TrafficLane lane)
    {
        return lane && signals.TryGetValue(lane, out TrafficSignalController signal) && signal ? signal : null;
    }

    // 이미 존재하는 진입 Lane에 대응하는 정지선을 반환한다
    public VehicleStopZone Entry(TrafficLane lane)
    {
        return lane && entries.TryGetValue(lane, out VehicleStopZone zone) ? zone : null;
    }

    // 비활성 신호와 전환 시간은 모두 적색으로 취급한다
    public bool IsGreen(VehicleStopZone zone)
    {
        return isActiveAndEnabled && phase >= 0 && phase < ZoneCount && zones[phase] == zone;
    }

    // 차량 신호, 실제 회전 경로, 교차로 점유를 모두 반영한다
    public bool CanPedestrianCross(VehicleStopZone zone)
    {
        if (!isActiveAndEnabled || !zone || !zone.isActiveAndEnabled || zone.Signal != this || pedestrianConflicts == null || zone.IsGreen)
            return false;
        int crossing = -1;
        for (int i = 0; i < ZoneCount; i++)
            if (zones[i] == zone)
                crossing = i;
        if (crossing < 0)
            return false;
        for (int route = 0; route < counts.Length; route++)
            if (pedestrianConflicts[crossing, route] && (counts[route] > 0 || (phase >= 0 && phase < ZoneCount && routesFrom[route] == zones[phase])))
                return false;
        return true;
    }

    // 교차로 내부에 갑자기 생성되어 신호를 우회하는 차량을 막는다
    public static bool CanSpawn(TrafficLane lane, float distance)
    {
        TrafficSignalController signal = ForLane(lane);
        if (!signal)
            return true;
        VehicleStopZone zone = signal.Entry(lane);
        if (zone)
            return distance <= zone.StopDistance;
        return !signal.routes.ContainsKey(lane) && lane.TrySample(distance, out Vector3 point, out _) && Vector2.Distance(point, signal.transform.position) >= 7f;
    }

    // 실제 내부 경로를 한 번 등록하고 교차로당 하나의 신호 루틴만 시작한다
    private VehicleStopZone[] routesFrom;

    private void OnEnable()
    {
        if (!Application.isPlaying || !road || ZoneCount == 0)
            return;
        if (counts != null)
        {
            routine = StartCoroutine(Cycle());
            return;
        }
        entries.Clear();
        routes.Clear();
        var paths = new List<TrafficLane>();
        var origins = new List<VehicleStopZone>();
        foreach (VehicleStopZone zone in zones)
        {
            if (!zone || !zone.Lane || zone.Signal != this)
                continue;
            entries[zone.Lane] = zone;
            for (int i = 0; i < zone.Lane.NextCount; i++)
            {
                TrafficLane route = zone.Lane.GetNext(i);
                if (!route || routes.ContainsKey(route))
                    continue;
                routes.Add(route, paths.Count);
                paths.Add(route);
                origins.Add(zone);
            }
        }
        counts = new int[paths.Count];
        routesFrom = origins.ToArray();
        conflicts = new bool[paths.Count, paths.Count];
        pedestrianConflicts = new bool[ZoneCount, paths.Count];
        for (int a = 0; a < paths.Count; a++)
            for (int b = a + 1; b < paths.Count; b++)
                conflicts[a, b] = conflicts[b, a] = origins[a] != origins[b] && Crosses(paths[a], paths[b]);
        for (int z = 0; z < ZoneCount; z++)
        {
            VehicleStopZone zone = zones[z];
            if (!zone || !zone.CrosswalkStart || !zone.CrosswalkEnd)
                continue;
            for (int r = 0; r < paths.Count; r++)
                pedestrianConflicts[z, r] = CrossesCrosswalk(paths[r], zone.CrosswalkStart.position, zone.CrosswalkEnd.position) || CrossesCrosswalk(origins[r].Lane, zone.CrosswalkStart.position, zone.CrosswalkEnd.position);
        }
        registered = new TrafficLane[road.LaneCount];
        for (int i = 0; i < registered.Length; i++)
        {
            registered[i] = road.GetLane(i);
            signals[registered[i]] = this;
        }
        routine = StartCoroutine(Cycle());
    }

    // 차량 폭을 포함한 짧은 경로 샘플로 충돌 관계를 초기화 때만 캐싱한다
    private static bool Crosses(TrafficLane a, TrafficLane b)
    {
        for (float x = 0f; x <= a.Length + 0.5f; x += 0.5f)
        {
            a.TrySample(x, out Vector3 point, out _);
            if (b.TryProject(point, out _, out float offset) && offset < 4.84f)
                return true;
        }
        return false;
    }

    // 초기화 때만 실제 차량 경로와 보행 선분의 접촉을 계산한다
    private static bool CrossesCrosswalk(TrafficLane route, Vector3 start, Vector3 end)
    {
        Vector2 segment = end - start;
        float length = segment.sqrMagnitude;
        if (length < 0.01f)
            return true;
        for (float distance = 0f; distance <= route.Length + 0.5f; distance += 0.5f)
        {
            if (!route.TrySample(distance, out Vector3 point, out _))
                return true;
            float t = Mathf.Clamp01(Vector2.Dot((Vector2)(point - start), segment) / length);
            if (((Vector2)(point - start) - segment * t).sqrMagnitude < 2.25f)
                return true;
        }
        return false;
    }

    // 회전 분기의 충돌을 피하도록 연결된 진입 방향을 하나씩 보호한다
    private IEnumerator Cycle()
    {
        int next = 0;
        while (true)
        {
            SetPhase(next);
            yield return new WaitForSeconds(Mathf.Max(0.1f, greenDuration));
            SetPhase(-1);
            yield return new WaitForSeconds(Mathf.Max(0.1f, changeDelay));
            next = (next + 1) % ZoneCount;
        }
    }

    // 신호 표시도 같은 상태값에서 갱신한다
    private void SetPhase(int value)
    {
        phase = value;
        for (int i = 0; i < ZoneCount; i++)
            if (zones[i])
                zones[i].Show(i == phase);
    }

    // 신호 및 점유와 실제 근처 차량을 확인한 뒤 정지선 통과를 예약한다
    internal bool TryEnter(VehicleAI vehicle, VehicleStopZone zone, TrafficLane route, bool violate, bool commit)
    {
        if (!isActiveAndEnabled || !zone || !zone.isActiveAndEnabled || zone.Signal != this || !route || (!zone.IsGreen && !violate) || !routes.TryGetValue(route, out int index))
            return false;
        for (int i = 0; i < counts.Length; i++)
            if (counts[i] > 0 && conflicts[index, i])
                return false;
        int found = Physics2D.OverlapCircle(transform.position, 7f, new ContactFilter2D { useTriggers = true }, hits);
        if (found == hits.Length)
            return false;
        for (int i = 0; i < found; i++)
        {
            Collider2D other = hits[i];
            if (other == vehicle.Shape || !other.attachedRigidbody)
                continue;
            bool inside = Vector2.Distance(other.attachedRigidbody.position, transform.position) < 5.5f;
            Vector3 gap = other.bounds.center - vehicle.transform.position;
            bool ahead = Vector3.Dot(gap, zone.Lane.StartDirection) > 0f;
            if ((!occupants.ContainsKey(other) && inside) || (ahead && gap.sqrMagnitude < 6.25f))
                return false;
        }
        if (commit && !occupants.ContainsKey(vehicle.Shape))
        {
            occupants.Add(vehicle.Shape, index);
            counts[index]++;
        }
        return true;
    }

    // 반환과 교차로 이탈 시 예약을 즉시 해제한다
    internal void Leave(Collider2D vehicle)
    {
        if (!ReferenceEquals(vehicle, null) && occupants.TryGetValue(vehicle, out int index))
        {
            occupants.Remove(vehicle);
            counts[index]--;
        }
    }

    // 비활성화 시 적색으로 정리하고 차량이 떠날 때까지 새 진입을 금지한다
    private void OnDisable()
    {
        if (routine != null)
            StopCoroutine(routine);
        routine = null;
        SetPhase(-1);
    }

    // 도시 파괴 시 이전 Lane 등록과 점유 참조를 제거한다
    private void OnDestroy()
    {
        if (registered != null)
            for (int i = 0; i < registered.Length; i++)
            {
                TrafficLane lane = registered[i];
                if (!ReferenceEquals(lane, null) && signals.TryGetValue(lane, out TrafficSignalController signal) && signal == this)
                    signals.Remove(lane);
            }
        occupants.Clear();
    }
}
