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
    [SerializeField] private Sprite[] sprites = System.Array.Empty<Sprite>();
    [SerializeField] private int[] spriteWeights = { 35, 25, 15, 12, 8, 5 };
    [SerializeField, Range(0, 100)] private int maxVehicles = 16;
    [SerializeField, Min(5f)] private float spawnNear = 18f;
    [SerializeField, Min(10f)] private float spawnFar = 40f;
    [SerializeField, Min(15f)] private float despawnDistance = 60f;
    [SerializeField, Min(0.1f)] private float interval = 0.5f;
    [SerializeField, Range(1, 64)] private int attempts = 24;
    [SerializeField] private bool spawnCitizens = true;
    private readonly List<VehicleAI> active = new List<VehicleAI>();
    private readonly Stack<VehicleAI> pool = new Stack<VehicleAI>();
    private readonly Dictionary<Collider2D, VehicleAI> vehicles = new Dictionary<Collider2D, VehicleAI>();
    private readonly Collider2D[] hits = new Collider2D[32];
    private readonly Collider2D[] leaderHits = new Collider2D[32];
    private readonly HashSet<TrafficLane> nearby = new HashSet<TrafficLane>();
    private readonly HashSet<TrafficLane> preferredLanes = new HashSet<TrafficLane>();
    private readonly List<TrafficLane> forwardLanes = new List<TrafficLane>();
    private readonly List<TrafficLane> reverseLanes = new List<TrafficLane>();
    private readonly List<TrafficLane> otherLanes = new List<TrafficLane>();
    private int leaderCount;
    private Rigidbody2D ambulanceBody;
    private Collider2D ambulanceShape;
    private TrafficLane ambulanceLane;
    private float ambulanceAlong;
    private SirenController siren;
    private CitizenSpawner citizens;
    private ContactFilter2D filter;
    private CityMap subscribedMap;
    private CityMap layout;
    private Coroutine routine;
    private uint random;
    private int lastSprite = -1;
    private int spriteRun;
    public CityMap Map => map;
    public int ActiveCount => active.Count;
    public int PooledCount => pool.Count;
    public int MaxVehicles => maxVehicles;
    internal bool SirenOn => siren && siren.isActiveAndEnabled && siren.IsOn;

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
        ambulanceShape = player ? player.GetComponent<Collider2D>() : null;
        siren = player ? player.GetComponent<SirenController>() : null;
        if (spawnCitizens && Application.isPlaying && map && player)
        {
            if (!citizens)
                citizens = GetComponent<CitizenSpawner>();
            if (!citizens)
                citizens = gameObject.AddComponent<CitizenSpawner>();
            citizens.Configure(map, player, view, siren, Resources.Load<CitizenAI>("Citizen"));
        }
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
        if (!map || !map.IsReady || layout != map)
        {
            ClearVehicles();
            layout = map && map.IsReady ? map : null;
            random = unchecked((uint)System.Guid.NewGuid().GetHashCode());
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

    // 콘텐츠 난수 진행을 다른 시스템과 분리한다
    private int Next(int count)
    {
        random = unchecked(random * 1664525u + 1013904223u);
        return (int)((random >> 8) % (uint)count);
    }

    // 가중치에 따라 생성 시 한 번 선택하며 같은 종류의 세 번 연속 생성을 피한다
    private Sprite NextSprite()
    {
        if (sprites == null || sprites.Length == 0)
            return null;
        int total = 0;
        for (int i = 0; i < sprites.Length; i++)
            if (sprites[i] && (i != lastSprite || spriteRun < 2))
                total += i < spriteWeights.Length ? Mathf.Max(0, spriteWeights[i]) : 1;
        if (total == 0)
            return lastSprite >= 0 && lastSprite < sprites.Length ? sprites[lastSprite] : null;
        int choice = Next(total);
        for (int i = 0; i < sprites.Length; i++)
        {
            if (!sprites[i] || (i == lastSprite && spriteRun >= 2))
                continue;
            choice -= i < spriteWeights.Length ? Mathf.Max(0, spriteWeights[i]) : 1;
            if (choice >= 0)
                continue;
            spriteRun = i == lastSprite ? spriteRun + 1 : 1;
            lastSprite = i;
            return sprites[i];
        }
        return null;
    }

    // 생성 지점의 실제 차체 겹침과 같은 방향 차량의 앞뒤 간격을 함께 확인한다
    private bool SpawnSpaceFree(Vector3 point, Vector3 direction)
    {
        int count = Physics2D.OverlapCircle(point, 4.5f, filter, hits);
        if (count == hits.Length)
            return false;
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = hits[i];
            if (!hit)
                continue;
            Rigidbody2D body = hit.attachedRigidbody;
            if (ambulanceBody && body == ambulanceBody)
                return false;
            if (!vehicles.TryGetValue(hit, out VehicleAI other) || !other || !other.isActiveAndEnabled)
                continue;
            Vector3 gap = other.transform.position - point;
            if (gap.sqrMagnitude < 1f)
                return false;
            float ahead = Vector3.Dot(gap, direction);
            float lateral = Mathf.Abs(gap.x * direction.y - gap.y * direction.x);
            if (lateral < 0.9f && Mathf.Abs(ahead) < (ahead >= 0f ? 4f : 3f))
                return false;
        }
        return true;
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
        ambulanceLane = null;
        if (ambulanceShape && ambulanceShape.enabled && ambulanceBody && ambulanceBody.gameObject.activeInHierarchy && map)
            map.TryLocate(ambulanceBody.position, 0.7f, out ambulanceLane, out ambulanceAlong);
    }

    // 기존 콜라이더 캐시에서 정지선 밖 대기 차량만 상수 시간에 구분한다
    internal bool IsWaitingOutsideSignal(Collider2D shape, Bounds bounds)
    {
        if (!vehicles.TryGetValue(shape, out VehicleAI vehicle) || !vehicle)
            return false;
        VehicleStopZone zone = vehicle.WaitingZone;
        if (!zone || !zone.Lane)
            return false;
        Vector3 direction = zone.Lane.StartDirection;
        Vector3 gap = bounds.center - zone.Lane.StartPoint;
        if (Mathf.Abs(gap.x * direction.y - gap.y * direction.x) > 0.7f)
            return false;
        float extent = Mathf.Abs(direction.x) * bounds.extents.x + Mathf.Abs(direction.y) * bounds.extents.y;
        return Vector3.Dot(gap, direction) + extent <= zone.StopDistance;
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
            // 같은 실제 경로의 구급차도 사이렌 여부와 무관하게 기존 앞차 간격을 적용한다
            if (hit && hit == ambulanceShape && ambulanceLane == sampledLane && (sampledLane != self.Lane || ambulanceAlong > self.Distance) && ((Vector2)position - hit.ClosestPoint(position)).sqrMagnitude <= 2.25f)
                return true;
            if (!hit || !vehicles.TryGetValue(hit, out VehicleAI other) || !other || other == self || !other.isActiveAndEnabled || (other.Lane != sampledLane && other.TargetLane != sampledLane) || ((Vector2)position - hit.ClosestPoint(position)).sqrMagnitude > 2.25f)
                continue;
            if (other.Lane != self.Lane || other.Distance > self.Distance)
                return true;
        }
        return false;
    }

    // 차선 변경 판단의 확률을 콘텐츠 난수 진행에서 얻는다
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

    // 현재 차선 또는 짧은 정방향 연결에서 빠르게 접근하는 사이렌을 확인한다
    internal bool IsAmbulanceApproaching(TrafficLane target, float distance, float vehicleSpeed)
    {
        if (!SirenOn || !map || !map.ContainsLane(target) || !TryAmbulanceLane(out TrafficLane lane, out float along, out float speed) || speed < 1f || speed <= vehicleSpeed + 0.3f)
            return false;
        return ApproachAlong(lane, target, -along, distance, 0);
    }

    // 거리와 연결 횟수를 제한하여 분기 차선도 확인한다
    private bool ApproachAlong(TrafficLane lane, TrafficLane target, float offset, float distance, int hops)
    {
        if (!map.ContainsLane(lane) || hops > 3 || offset > 30f)
            return false;
        if (lane == target)
        {
            float gap = offset + distance;
            return gap > 2f && gap <= 25f;
        }
        for (int i = 0; i < lane.NextCount; i++)
        {
            TrafficLane next = lane.GetNext(i);
            if (next && ApproachAlong(next, target, offset + lane.Length, distance, hops + 1))
                return true;
        }
        return false;
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
        return OutsideView(point);
    }

    // 화면 여유 영역 밖인지 생성과 먼 교통 정리에 같은 기준을 사용한다
    private bool OutsideView(Vector3 point)
    {
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
        if (!isActiveAndEnabled || !map || !prefab || active.Count >= maxVehicles || !map.ContainsLane(lane) || !float.IsFinite(distance) || distance < 2f || distance > lane.Length - 2f || !VehicleAI.Following(map, lane, routeChoice) || !lane.TrySample(distance, out Vector3 point, out Vector3 direction) || !CanSpawnAt(point) || !SpawnSpaceFree(point, direction))
            return false;
        while (pool.Count > 0 && !vehicle)
            vehicle = pool.Pop();
        if (!vehicle)
        {
            vehicle = Instantiate(prefab, transform);
            vehicle.gameObject.SetActive(false);
        }
        vehicle.SetSprite(NextSprite());
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

    // 차선의 실제 방향으로 주변 생성 후보를 분류한다
    private Vector3 CollectSpawnLanes()
    {
        Vector3 heading = player.up;
        if (map.TryLocate(player.position, 2f, out TrafficLane current, out float along))
            current.TrySample(along, out _, out heading);
        forwardLanes.Clear();
        reverseLanes.Clear();
        otherLanes.Clear();
        preferredLanes.Clear();
        map.CollectLanes(player.position, spawnFar, nearby);
        foreach (TrafficLane lane in nearby)
        {
            if (lane.Length < 4f || !lane.TryProject(player.position, out float distance, out float offset) || offset > spawnFar * spawnFar || !lane.TrySample(distance, out Vector3 point, out Vector3 direction) || !TrafficSignalController.CanSpawn(lane, lane.Length * 0.5f))
                continue;
            float dot = Vector3.Dot(heading, direction);
            Vector3 gap = point - player.position;
            float lateral = Mathf.Abs(gap.x * heading.y - gap.y * heading.x);
            if (lateral < 3f && dot > 0.7f)
                forwardLanes.Add(lane);
            else if (lateral < 3f && dot < -0.7f)
                reverseLanes.Add(lane);
            else
                otherLanes.Add(lane);
        }
        preferredLanes.UnionWith(forwardLanes);
        preferredLanes.UnionWith(reverseLanes);
        return heading;
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
            Vector3 heading = CollectSpawnLanes();
            int forward = 0;
            int reverse = 0;
            int other = 0;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                VehicleAI vehicle = active[i];
                if (!vehicle || !vehicle.isActiveAndEnabled || !map.ContainsLane(vehicle.Lane) || (vehicle.transform.position - player.position).sqrMagnitude > radius * radius || active.Count > maxVehicles || (preferredLanes.Count > 0 && active.Count >= Mathf.Max(1, maxVehicles - 3) && !preferredLanes.Contains(vehicle.Lane) && view && view.isActiveAndEnabled && OutsideView(vehicle.transform.position)))
                    Release(vehicle);
                else if (vehicle.Lane.TrySample(vehicle.Distance, out Vector3 point, out Vector3 direction))
                {
                    if (!preferredLanes.Contains(vehicle.Lane)) other++;
                    Vector3 gap = point - player.position;
                    if (gap.sqrMagnitude <= spawnFar * spawnFar && Mathf.Abs(gap.x * heading.y - gap.y * heading.x) < 3f)
                    {
                        float dot = Vector3.Dot(heading, direction);
                        if (dot > 0.7f) forward++;
                        else if (dot < -0.7f) reverse++;
                    }
                }
            }
            int added = 0;
            for (int attempt = 0; attempt < attempts && active.Count < maxVehicles && added < 3; attempt++)
            {
                List<TrafficLane> candidates = forward < reverse || (forward == reverse && Next(2) == 0) ? forwardLanes : reverseLanes;
                if ((attempt % 6 == 5 && other < maxVehicles / 4) || candidates.Count == 0)
                    candidates = otherLanes.Count > 0 ? otherLanes : (forwardLanes.Count > 0 ? forwardLanes : reverseLanes);
                if (candidates.Count == 0)
                    break;
                TrafficLane lane = candidates[Next(candidates.Count)];
                if (TrySpawn(lane, lane.Length * (0.05f + Next(900) / 1000f), out VehicleAI vehicle))
                {
                    added++;
                    if (candidates == forwardLanes) forward++;
                    else if (candidates == reverseLanes) reverse++;
                    else other++;
                }
            }
        }
    }
}
