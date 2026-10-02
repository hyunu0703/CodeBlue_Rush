using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>플레이어 주변 도로에서 차량 수를 제한하고 작은 풀과 물리 조회를 제공한다</summary>
[DisallowMultipleComponent]
public sealed class TrafficSpawner : MonoBehaviour
{
    [SerializeField] private CityMap map;
    [SerializeField] private Transform player;
    [SerializeField] private Camera view;
    [SerializeField] private VehicleAI prefab;
    [SerializeField, Range(0, 100)] private int maxVehicles = 16;
    [SerializeField, Min(5f)] private float spawnNear = 18f;
    [SerializeField, Min(10f)] private float spawnFar = 40f;
    [SerializeField, Min(15f)] private float despawnDistance = 60f;
    [SerializeField, Min(0.1f)] private float interval = 0.5f;
    [SerializeField, Range(1, 64)] private int attempts = 24;
    private readonly List<VehicleAI> active = new List<VehicleAI>();
    private readonly Stack<VehicleAI> pool = new Stack<VehicleAI>();
    private readonly Dictionary<Collider2D, VehicleAI> vehicles = new Dictionary<Collider2D, VehicleAI>();
    private readonly Collider2D[] hits = new Collider2D[32];
    private readonly Collider2D[] leaderHits = new Collider2D[32];
    private int leaderCount;
    private Rigidbody2D ambulanceBody;
    private ContactFilter2D filter;
    private CityMap subscribedMap;
    private CityLayout layout;
    private Coroutine routine;
    private uint random;
    public CityMap Map => map;
    public int ActiveCount => active.Count;
    public int PooledCount => pool.Count;
    public int MaxVehicles => maxVehicles;

    // 트리거 차량 감지를 위한 조회 필터를 준비한다
    private void Awake()
    {
        filter = new ContactFilter2D { useTriggers = true };
    }

    // 명시적 참조로 기존 도시와 플레이어 및 Prefab을 연결한다
    public void Configure(CityMap city, Transform source, Camera camera, VehicleAI vehiclePrefab)
    {
        Unbind();
        ClearVehicles();
        map = city;
        player = source;
        view = camera;
        prefab = vehiclePrefab;
        layout = null;
        if (isActiveAndEnabled)
            Bind();
    }

    // 활성화 시 하나의 유지 루틴만 실행한다
    private void OnEnable()
    {
        Bind();
    }

    // 비활성화 시 차량과 이벤트를 정리한다
    private void OnDisable()
    {
        Unbind();
        ClearVehicles();
    }

    // 실제 구독 대상과 루틴을 중복 없이 연결한다
    private void Bind()
    {
        Unbind();
        ambulanceBody = player ? player.GetComponent<Rigidbody2D>() : null;
        subscribedMap = map;
        if (subscribedMap)
            subscribedMap.StateChanged += HandleMap;
        HandleMap();
        if (Application.isPlaying)
            routine = StartCoroutine(Maintain());
    }

    // 기존 구독과 유지 루틴을 해제한다
    private void Unbind()
    {
        if (subscribedMap)
            subscribedMap.StateChanged -= HandleMap;
        subscribedMap = null;
        if (routine != null)
            StopCoroutine(routine);
        routine = null;
    }

    // 도시가 교체되면 이전 차량을 즉시 풀로 돌려보낸다
    private void HandleMap()
    {
        if (!map || !map.IsReady || layout != map.Layout)
        {
            ClearVehicles();
            layout = map && map.IsReady ? map.Layout : null;
            random = map ? unchecked((uint)map.Seed) ^ 0xA37F81u : 1u;
        }
    }

    // 활성 차량 전체를 선형 시간에 정리한다
    private void ClearVehicles()
    {
        while (active.Count > 0)
            Release(active[active.Count - 1]);
    }

    // 조회 전용으로 활성 차량을 반환한다
    public VehicleAI GetVehicle(int index)
    {
        return index >= 0 && index < active.Count ? active[index] : null;
    }

    // Seed 기반 난수 진행을 다른 시스템과 분리한다
    private int Next(int count)
    {
        random = unchecked(random * 1664525u + 1013904223u);
        return (int)((random >> 8) % (uint)count);
    }

    // 차량 전체 탐색 없이 근처 콜라이더만 확인한다
    public bool IsSpaceFree(Vector3 position, VehicleAI ignore = null)
    {
        int count = Physics2D.OverlapCircle(position, 1.5f, filter, hits);
        if (count == hits.Length)
            return false;
        for (int i = 0; i < count; i++)
            if (vehicles.TryGetValue(hits[i], out VehicleAI vehicle) && vehicle && vehicle != ignore && vehicle.isActiveAndEnabled)
                return false;
        return true;
    }

    // 한 차량의 짧은 경로 전체를 포함하는 후보를 한 번 조회한다
    internal void ScanLeaders(Vector3 origin, float range)
    {
        leaderCount = Physics2D.OverlapCircle(origin, range + 1.5f, filter, leaderHits);
    }

    // 기존 원형 Probe와 같은 거리 조건을 캐싱한 콜라이더에서 판별한다
    internal bool HasLeaderAt(Vector3 position, VehicleAI self, TrafficLane sampledLane)
    {
        Collider2D[] candidates = leaderHits;
        int count = leaderCount;
        // 넓은 조회가 가득 찬 경우 기존의 짧은 조회로 되돌려 불필요한 정차를 막는다
        if (count == leaderHits.Length)
        {
            candidates = hits;
            count = Physics2D.OverlapCircle(position, 1.5f, filter, hits);
            if (count == hits.Length)
                return true;
        }
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = candidates[i];
            if (!hit || !vehicles.TryGetValue(hit, out VehicleAI other) || !other || other == self || !other.isActiveAndEnabled || (other.Lane != sampledLane && other.TargetLane != sampledLane) || ((Vector2)position - hit.ClosestPoint(position)).sqrMagnitude > 2.25f)
                continue;
            if (other.Lane != self.Lane || other.Distance > self.Distance)
                return true;
        }
        return false;
    }

    // 차선 변경 판단의 확률을 도시 Seed 난수 진행에서 얻는다
    internal bool Roll(float chance)
    {
        return chance > 0f && (chance >= 1f || Next(100000) < chance * 100000f);
    }

    // 기존 지역 도로 조회와 캐싱한 실제 물리 속도로 구급차를 확인한다
    internal bool TryAmbulanceLane(out TrafficLane lane, out float distance, out float speed)
    {
        lane = null;
        distance = 0f;
        speed = 0f;
        if (!ambulanceBody || !ambulanceBody.gameObject.activeInHierarchy || !map || !map.TryLocate(ambulanceBody.position, 0.7f, out lane, out distance) || !lane.TrySample(distance, out _, out Vector3 direction))
            return false;
        speed = Vector2.Dot(ambulanceBody.linearVelocity, direction);
        return true;
    }

    // 목표 차선의 앞뒤 차량과 변경 중 예약 차량을 물리 조회로 확인한다
    internal bool LaneSpaceSafe(VehicleAI self, TrafficLane target, float along, float front, float rear, float seconds)
    {
        if (!map.ContainsLane(target) || !target.TrySample(along, out Vector3 point, out Vector3 direction))
            return false;
        float radius = Mathf.Max(front, rear) + 12f * seconds + 2f;
        int count = Physics2D.OverlapCircle(point, radius, filter, hits);
        if (count == hits.Length)
            return false;
        for (int i = 0; i < count; i++)
        {
            if (!vehicles.TryGetValue(hits[i], out VehicleAI other) || !other || other == self || !other.isActiveAndEnabled || (other.Lane != target && other.TargetLane != target))
                continue;
            float gap = Vector3.Dot(other.transform.position - point, direction);
            float required = gap >= 0f ? front + Mathf.Max(0f, self.Speed - other.Speed) * seconds : rear + Mathf.Max(0f, other.Speed - self.Speed) * seconds;
            if (Mathf.Abs(gap) < required)
                return false;
        }
        if (TryAmbulanceLane(out TrafficLane playerLane, out float playerAlong, out float playerSpeed) && playerLane == target)
        {
            float gap = playerAlong - along;
            float required = gap >= 0f ? front + Mathf.Max(0f, self.Speed - playerSpeed) * seconds : rear + Mathf.Max(0f, playerSpeed - self.Speed) * seconds;
            if (Mathf.Abs(gap) < required)
                return false;
        }
        return true;
    }
    // 실제 카메라의 여유 영역 밖이며 플레이어와 충분히 떨어진 위치만 허용한다
    public bool CanSpawnAt(Vector3 point)
    {
        if (!player || !view || !view.isActiveAndEnabled)
            return false;
        float distance = Vector2.Distance(player.position, point);
        if (distance < spawnNear || distance > spawnFar)
            return false;
        Vector3 viewport = view.WorldToViewportPoint(point);
        return viewport.z > 0f && (viewport.x < -0.2f || viewport.x > 1.2f || viewport.y < -0.2f || viewport.y > 1.2f);
    }

    // 동일한 안전 조건으로 생성과 명시적 생성 요청을 처리한다
    public bool TrySpawn(TrafficLane lane, float distance, out VehicleAI vehicle)
    {
        vehicle = null;
        int routeChoice = Next(int.MaxValue);
        if (!TrafficSignalController.CanSpawn(lane, distance))
            return false;
        if (!isActiveAndEnabled || !map || !prefab || active.Count >= maxVehicles || !map.ContainsLane(lane) || !float.IsFinite(distance) || distance < 0f || distance > lane.Length || !VehicleAI.Following(map, lane, routeChoice) || !lane.TrySample(distance, out Vector3 point, out _) || !CanSpawnAt(point) || !IsSpaceFree(point))
            return false;
        while (pool.Count > 0 && !vehicle)
            vehicle = pool.Pop();
        if (!vehicle)
        {
            vehicle = Instantiate(prefab, transform);
            vehicle.gameObject.SetActive(false);
        }
        vehicle.gameObject.SetActive(true);
        vehicle.Place(this, lane, distance, routeChoice);
        vehicle.Slot = active.Count;
        active.Add(vehicle);
        vehicles.Add(vehicle.Shape, vehicle);
        Physics2D.SyncTransforms();
        return true;
    }

    // 마지막 슬롯 교환으로 상수 시간에 등록을 지우고 차선 참조를 비운다
    public void Release(VehicleAI vehicle)
    {
        if (ReferenceEquals(vehicle, null))
            return;
        int slot = vehicle.Slot;
        if (slot < 0 || slot >= active.Count || active[slot] != vehicle)
            return;
        VehicleAI last = active[active.Count - 1];
        active[slot] = last;
        last.Slot = slot;
        active.RemoveAt(active.Count - 1);
        if (!ReferenceEquals(vehicle.Shape, null))
            vehicles.Remove(vehicle.Shape);
        vehicle.Clear();
        if (vehicle)
        {
            vehicle.gameObject.SetActive(false);
            if (pool.Count < maxVehicles)
                pool.Push(vehicle);
            else
                Destroy(vehicle.gameObject);
        }
    }

    // 간헐적인 정리와 고정 횟수의 주변 격자 샘플링으로 차량 수를 유지한다
    private IEnumerator Maintain()
    {
        while (true)
        {
            yield return new WaitForSeconds(Mathf.Max(0.1f, interval));
            if (!map || !map.IsReady || !player || !prefab)
                continue;
            float radius = Mathf.Max(despawnDistance, spawnFar + 5f);
            for (int i = active.Count - 1; i >= 0; i--)
            {
                VehicleAI vehicle = active[i];
                if (!vehicle || !vehicle.isActiveAndEnabled || !map.ContainsLane(vehicle.Lane) || (vehicle.transform.position - player.position).sqrMagnitude > radius * radius || active.Count > maxVehicles)
                    Release(vehicle);
            }
            Vector3 local = map.transform.InverseTransformPoint(player.position);
            int cx = Mathf.RoundToInt(local.x / map.CellSize);
            int cy = Mathf.RoundToInt(local.y / map.CellSize);
            int cells = Mathf.CeilToInt(spawnFar / map.CellSize);
            int added = 0;
            for (int attempt = 0; attempt < attempts && active.Count < maxVehicles && added < 3; attempt++)
            {
                RoadChunk road = map.GetRoad(cx + Next(cells * 2 + 1) - cells, cy + Next(cells * 2 + 1) - cells);
                if (!road || road.LaneCount == 0)
                    continue;
                TrafficLane lane = road.GetLane(Next(road.LaneCount));
                if (lane && TrySpawn(lane, lane.Length * (0.15f + Next(700) / 1000f), out _))
                    added++;
            }
        }
    }
}
