using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>생성된 미션 맵의 신호, AI 차량 인스턴스와 고정 스텝 이동을 소유한다. 비활성화/맵 교체 시 소유 차량만 정리한다.</summary>
[DisallowMultipleComponent]
public sealed class RoadTrafficController : MonoBehaviour
{
    // 설정 메뉴 또는 Inspector에서 지정하는 씬 참조와 기존 차량 외형 프리팹이다.
    [SerializeField] private MissionMapGenerator mapGenerator;
    [SerializeField] private Rigidbody2D player;
    [SerializeField] private GameObject[] vehiclePrefabs;
    // 기존 차량 수/생성 간격을 기준으로 주변 밀도와 보충 속도를 3배로 높인다.
    [SerializeField, Range(1, 64)] private int maximumVehicles = 32;
    [SerializeField, Min(0.2f)] private float spawnInterval = 1f;
    // 반경은 맵 로컬 차로 폭 단위다. 회수 반경을 더 크게 두어 경계에서 반복 생성/회수를 막는다.
    [SerializeField, Range(1, 3)] private int trafficDensity = 3;
    [SerializeField, Min(8f)] private float spawnRadiusInLaneWidths = 28f;
    [SerializeField, Min(9f)] private float despawnRadiusInLaneWidths = 36f;
    private const int MaximumPerPrefab = 3;
    private const int SpawnAttempts = 24;
    // 외부 차량 바디를 추가로 관측할 레이어다. 플레이어와 소유 AI 감지는 이 마스크와 별도로 유지한다.
    [SerializeField, Tooltip("외부 Dynamic/Kinematic Rigidbody2D 감지 레이어. 정적 도로/장식 콜라이더는 제외하며 플레이어와 AI 차량은 별도 감지합니다.")]
    private LayerMask obstacleLayers = ~0;
    // 저장된 씬에 PCX 참조가 남아 있어도 생성하지 않는 명시적 제외 정책이다.
    private const string ExcludedPrefabName = "PCX";
    // 네트워크와 인스턴스는 이 컴포넌트의 활성 수명에만 존재한다. 검색 버퍼는 잘림 없는 List다.
    private RoadTrafficSimulation simulation;
    private RoadLaneNetwork network;
    private Transform mapRoot;
    private GameObject vehicleRoot;
    private readonly List<VehicleInstance> instances = new List<VehicleInstance>();
    private readonly List<RoadTrafficObstacle> obstacles = new List<RoadTrafficObstacle>();
    private readonly List<Collider2D> overlapResults = new List<Collider2D>();
    private readonly HashSet<Rigidbody2D> ownedBodies = new HashSet<Rigidbody2D>();
    // 바디 연결/활성화 상태와 무관하게 소유 콜라이더를 외부 장애물 관측에서 제외한다.
    private readonly HashSet<Collider2D> ownedColliders = new HashSet<Collider2D>();
    private readonly List<GameObject> spawnPrefabs = new List<GameObject>();
    private readonly List<int> spawnPaths = new List<int>();
    // 프리팹별 최대 3개의 차체와 managed 주행 상태를 소유한다. 활성 목록과 풀은 중복 소유하지 않는다.
    private readonly List<Stack<VehicleInstance>> pools = new List<Stack<VehicleInstance>>();
    private int[] activePerPrefab;
    private Camera trafficCamera;
    private float spawnRadius;
    private float despawnRadius;
    private int TargetVehicles => Math.Min(maximumVehicles * trafficDensity, spawnPrefabs.Count * MaximumPerPrefab);
    private System.Random random;
    private int version = -1;
    private float spawnClock;
    private float searchRadius;
    private Vector2 searchCenter;
    private float mapScale;
    // 플레이어 차체는 활성화 시 캐시하고 파괴되면 런타임 검증에서 중단한다.
    private Collider2D playerCollider;
    public RoadTrafficSimulation Simulation => isActiveAndEnabled ? simulation : null;

    /// <summary>실제 차체와 순수 AI 상태의 소유 관계다. Finished 또는 파괴된 차체는 다음 스텝에 제거한다.</summary>
    private sealed class VehicleInstance
    {
        // 오브젝트와 Rigidbody는 컨트롤러가 생성/제거하며 프리팹 원본은 수정하지 않는다.
        internal GameObject Root;
        internal Rigidbody2D Body;
        internal BoxCollider2D Collider;
        internal RoadTrafficVehicle Vehicle;
        // 반환 대상 풀 번호와 그래프 단위의 차체 크기는 생성 시 한 번 계산한다.
        internal int PrefabIndex;
        internal float Length;
        internal float Width;
    }

    /// <summary>활성화마다 의존성을 다시 검증한다. 설정은 Play 이전에 완료되어 있어야 한다.</summary>
    private void OnEnable()
    {
        if (!ValidateConfiguration(out var error))
        {
            Debug.LogError(error, this);
            enabled = false;
            return;
        }
        playerCollider = player.GetComponent<Collider2D>();
        spawnPrefabs.Clear();
        foreach (var prefab in vehiclePrefabs)
        {
            if (prefab != null && prefab.name != ExcludedPrefabName && !spawnPrefabs.Contains(prefab))
            {
                spawnPrefabs.Add(prefab);
            }
        }
        activePerPrefab = new int[spawnPrefabs.Count];
        trafficCamera = Camera.main;
        version = -1;
    }

    /// <summary>소유한 런타임 차량만 제거한다. 원본 프리팹과 플레이어에는 쓰지 않는다.</summary>
    private void OnDisable()
    {
        ClearVehicles();
    }

    /// <summary>네트워크 공개 버전 변경을 감지한 다음 고정 스텝 관측/AI/물리 이동을 처리한다.</summary>
    private void FixedUpdate()
    {
        if (Time.timeScale <= 0f)
        {
            return;
        }
        if (mapGenerator == null || player == null || playerCollider == null)
        {
            Debug.LogError("교통 AI의 Map Generator 또는 Player가 사라졌습니다. 씬 참조를 다시 연결해주세요.", this);
            enabled = false;
            return;
        }
        var current = mapGenerator.LaneNetwork;
        if (current == null || !current.IsValid)
        {
            if (simulation != null)
            {
                ReturnAllInstances();
                simulation = null;
                network = null;
                version = -1;
            }
            return;
        }
        if (version != mapGenerator.LaneNetworkVersion || !ReferenceEquals(current, network))
        {
            ResetNetwork(current);
            if (!isActiveAndEnabled)
            {
                return;
            }
        }
        if (mapRoot == null || !HasSupportedMapTransform())
        {
            Debug.LogError("교통 AI Map Root는 고정된 양수 균일 XY 스케일과 Z축 회전이어야 합니다.", this);
            enabled = false;
            return;
        }
        // 물리 스텝 변경에도 순수 시뮬레이터의 0.1초 상한을 지킨다. 반복 중 관측은 동일 경계의 값을 사용한다.
        CollectObstacles();
        var remaining = Time.fixedDeltaTime;
        while (remaining > 0f)
        {
            var step = Mathf.Min(0.1f, remaining);
            simulation.Tick(step, obstacles);
            remaining -= step;
        }
        for (var index = instances.Count - 1; index >= 0; index--)
        {
            var instance = instances[index];
            if (instance.Root == null || instance.Body == null || instance.Vehicle.IsFinished
                || (instance.Vehicle.Position - searchCenter).sqrMagnitude > despawnRadius * despawnRadius)
            {
                RemoveInstance(index);
                continue;
            }
            // Kinematic 2D 바디의 지속 이동은 FixedUpdate의 MovePosition/MoveRotation으로만 적용한다.
            var position = mapRoot.TransformPoint(instance.Vehicle.Position);
            var forward = mapRoot.TransformVector(instance.Vehicle.Forward);
            instance.Body.MovePosition(position);
            instance.Body.MoveRotation(Mathf.Atan2(forward.y, forward.x) * Mathf.Rad2Deg - 90f);
        }
        spawnClock += Time.fixedDeltaTime;
        if (spawnClock >= spawnInterval / trafficDensity)
        {
            spawnClock = 0f;
            RefreshSpawnPaths();
            if (instances.Count < TargetVehicles)
            {
                TrySpawn(false);
            }
        }
    }

    /// <summary>프리팹의 소유권과 물리 모드를 명확히 확인한다. 이미 제어 스크립트/물리가 있는 프리팹은 덮어쓰지 않는다.</summary>
    public bool ValidateConfiguration(out string error)
    {
        error = null;
        if (mapGenerator == null || mapGenerator.LaneMapRoot == null || player == null
            || vehiclePrefabs == null || vehiclePrefabs.Length == 0 || maximumVehicles < 1 || maximumVehicles > 64
            || !RoadLaneMath.IsFinite(spawnInterval) || spawnInterval < 0.2f
            || trafficDensity < 1 || trafficDensity > 3
            || !RoadLaneMath.IsFinite(spawnRadiusInLaneWidths) || spawnRadiusInLaneWidths < 8f
            || !RoadLaneMath.IsFinite(despawnRadiusInLaneWidths) || despawnRadiusInLaneWidths <= spawnRadiusInLaneWidths)
        {
            error = "RoadTrafficController의 Map Generator, Player, Vehicle Prefabs와 차량 수/생성 간격을 설정해주세요.";
            return false;
        }
        if (Physics2D.simulationMode != SimulationMode2D.FixedUpdate || !player.simulated
            || player.GetComponent<Collider2D>() == null)
        {
            error = "교통 AI는 FixedUpdate 자동 2D 시뮬레이션과 simulated Player Rigidbody2D가 필요합니다.";
            return false;
        }
        // 실제 생성 후보만 검증한다. 제외 대상만 연결된 설정은 조치 가능한 오류로 중단한다.
        var eligibleCount = 0;
        foreach (var prefab in vehiclePrefabs)
        {
            if (prefab != null && prefab.name == ExcludedPrefabName)
            {
                continue;
            }
            if (prefab == null || prefab.GetComponentInChildren<SpriteRenderer>() == null
                || prefab.GetComponentInChildren<SpriteRenderer>().sprite == null
                || prefab.GetComponentsInChildren<Rigidbody2D>(true).Length > 0
                || prefab.GetComponentsInChildren<Collider2D>(true).Length > 0
                || prefab.GetComponentsInChildren<MonoBehaviour>(true).Length > 0)
            {
                error = "Vehicle Prefabs에는 SpriteRenderer가 있는 기존 외형 프리팹만 연결해주세요. 기존 물리/제어 컴포넌트는 보존합니다.";
                return false;
            }
            eligibleCount++;
        }
        if (eligibleCount == 0)
        {
            error = "Vehicle Prefabs에 PCX를 제외한 자동차 외형 프리팹을 연결해주세요.";
            return false;
        }
        return true;
    }

    /// <summary>교체된 맵마다 활성 차량을 반환하고 신호/그래프를 교체한다. 후송 확장에서도 풀 차체는 유지한다.</summary>
    private void ResetNetwork(RoadLaneNetwork current)
    {
        // 맵 교체에서는 차체를 파괴하지 않고 모든 활성 차량만 반환한다.
        ReturnAllInstances();
        mapRoot = mapGenerator.LaneMapRoot;
        mapScale = mapRoot != null ? mapRoot.lossyScale.x : 0f;
        if (mapRoot == null || !HasSupportedMapTransform())
        {
            Debug.LogError("교통 AI는 Map Root의 양수 균일 XY 스케일과 Z축 회전이 필요합니다.", this);
            enabled = false;
            return;
        }
        network = current;
        version = mapGenerator.LaneNetworkVersion;
        random = new System.Random(mapGenerator.CurrentSeed);
        simulation = new RoadTrafficSimulation(network, mapGenerator.CurrentSeed);
        spawnClock = 0f;
        // 풀은 최초 맵에서만 예열한다. 종료/비활성화 때만 소유 루트 전체를 파괴한다.
        if (vehicleRoot == null)
        {
            vehicleRoot = new GameObject("RuntimeTrafficVehicles");
            vehicleRoot.transform.SetParent(mapRoot, false);
            for (var prefabIndex = 0; prefabIndex < spawnPrefabs.Count; prefabIndex++)
            {
                var pool = new Stack<VehicleInstance>(MaximumPerPrefab);
                pools.Add(pool);
                for (var count = 0; count < MaximumPerPrefab; count++)
                {
                    pool.Push(CreateInstance(prefabIndex));
                }
            }
        }
        UpdateSearchArea();
        RefreshSpawnPaths();
        CollectObstacles();
        for (var count = 0; count < TargetVehicles && isActiveAndEnabled; count++)
        {
            TrySpawn(true);
        }
    }

    /// <summary>XY 그래프가 그대로 2D 물리에 대응하는 고정 맵 변환인지 검사한다. 스케일 변경은 다시 맵을 생성해야 한다.</summary>
    private bool HasSupportedMapTransform()
    {
        var xAxis = mapRoot.TransformVector(Vector3.right);
        var yAxis = mapRoot.TransformVector(Vector3.up);
        return mapScale > 0f && Mathf.Abs(xAxis.magnitude - mapScale) < 0.0001f
            && Mathf.Abs(yAxis.magnitude - mapScale) < 0.0001f
            && Mathf.Abs(Vector3.Dot(xAxis.normalized, yAxis.normalized)) < 0.0001f
            && Mathf.Abs(xAxis.z) < 0.0001f && Mathf.Abs(yAxis.z) < 0.0001f;
    }

    /// <summary>플레이어와 레이어에 포함된 외부 차량 바디를 관측한다. 도로/장식 등의 일반 정적 콜라이더는 차량으로 취급하지 않는다.</summary>
    private void CollectObstacles()
    {
        UpdateSearchArea();
        obstacles.Clear();
        // Player는 레이어 필터에서 빠져도 항상 검사한다. Collider의 AABB로 차체 크기를 보수적으로 추정한다.
        if (playerCollider != null && playerCollider.enabled)
        {
            AddObstacle(playerCollider, player);
        }
        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(obstacleLayers);
        Physics2D.OverlapCircle(mapRoot.TransformPoint(searchCenter), searchRadius * mapScale, filter, overlapResults);
        foreach (var collider in overlapResults)
        {
            if (collider == null || collider.isTrigger || !collider.enabled || ownedColliders.Contains(collider))
            {
                continue;
            }
            var body = collider.attachedRigidbody;
            // Everything에도 도로 바닥/장식 콜라이더가 들어갈 수 있다. 실제 시뮬레이션 차량 바디만 외부 차량으로 관측한다.
            if (body == null || !body.simulated || body.bodyType == RigidbodyType2D.Static
                || ownedBodies.Contains(body) || body == player)
            {
                continue;
            }
            AddObstacle(collider, body);
        }
    }

    /// <summary>하나의 콜라이더를 보수적인 정사각 차체 관측으로 등록한다. 회전한 AABB는 원래 차체보다 작게 계산하지 않는다.</summary>
    private void AddObstacle(Collider2D collider, Rigidbody2D body)
    {
        var bounds = collider.bounds;
        var position = (Vector2)mapRoot.InverseTransformPoint(bounds.center);
        var velocity = body != null ? (Vector2)mapRoot.InverseTransformVector(body.linearVelocity) : Vector2.zero;
        var size = Mathf.Max(bounds.size.x, bounds.size.y) / mapScale;
        obstacles.Add(new RoadTrafficObstacle(-obstacles.Count - 1, position, velocity, Vector2.up, size, size));
    }

    /// <summary>플레이어 주변으로 관측/생성/회수 영역을 이동한다. 직교 카메라가 있으면 화면 밖 보충 공간도 확보한다.</summary>
    private void UpdateSearchArea()
    {
        searchCenter = mapRoot.InverseTransformPoint(player.position);
        // 화면 모서리와 카메라 추적 지연까지 포함하는 보수적인 원형 반경이다.
        var laneWidth = network.Paths[0].Width;
        var viewRadius = trafficCamera != null && trafficCamera.orthographic
            ? trafficCamera.orthographicSize * Mathf.Sqrt(1f + trafficCamera.aspect * trafficCamera.aspect) / mapScale
                + Vector2.Distance(searchCenter, mapRoot.InverseTransformPoint(trafficCamera.transform.position))
            : laneWidth * 8f;
        spawnRadius = Mathf.Max(laneWidth * spawnRadiusInLaneWidths, viewRadius + laneWidth * 8f);
        despawnRadius = Mathf.Max(laneWidth * despawnRadiusInLaneWidths, spawnRadius + laneWidth * 8f);
        searchRadius = despawnRadius + laneWidth * 12f;
    }

    /// <summary>생성 후보는 생성 주기에만 갱신한다. 플레이어 근처의 연결된 비교차 경로만 캐시한다.</summary>
    private void RefreshSpawnPaths()
    {
        spawnPaths.Clear();
        foreach (var path in network.Paths)
        {
            if (path.Kind != RoadLanePathKind.Intersection && path.Successors.Count > 0
                && path.TryProject(searchCenter, out var along, out _)
                && path.TrySample(along, out var nearest, out _)
                && (nearest - searchCenter).sqrMagnitude <= spawnRadius * spawnRadius)
            {
                spawnPaths.Add(path.Id);
            }
        }
    }

    /// <summary>외형과 Kinematic 차체를 풀 예열 시 한 번만 만든다. 프리팹 원본에는 쓰지 않는다.</summary>
    private VehicleInstance CreateInstance(int prefabIndex)
    {
        var created = Instantiate(spawnPrefabs[prefabIndex], vehicleRoot.transform);
        created.SetActive(false);
        var renderer = created.GetComponentInChildren<SpriteRenderer>(true);
        var collider = created.AddComponent<BoxCollider2D>();
        // 월드 AABB는 맵 회전 시 부풀기 때문에 스프라이트의 로컬 꼭짓점을 차량 루트로 변환한다.
        var visual = renderer.sprite.bounds;
        var localBounds = new Bounds(created.transform.InverseTransformPoint(renderer.transform.TransformPoint(visual.min)), Vector3.zero);
        localBounds.Encapsulate(created.transform.InverseTransformPoint(renderer.transform.TransformPoint(visual.max)));
        localBounds.Encapsulate(created.transform.InverseTransformPoint(renderer.transform.TransformPoint(new Vector3(visual.min.x, visual.max.y))));
        localBounds.Encapsulate(created.transform.InverseTransformPoint(renderer.transform.TransformPoint(new Vector3(visual.max.x, visual.min.y))));
        collider.offset = localBounds.center;
        collider.size = (Vector2)localBounds.size * 0.88f;
        var body = created.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.simulated = false;
        body.gravityScale = 0f;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.useFullKinematicContacts = true;
        // 풀 내부 차체도 소유 목록에 유지해 외부 장애물 관측에서 제외한다.
        ownedBodies.Add(body);
        ownedColliders.Add(collider);
        return new VehicleInstance
        {
            Root = created, Body = body, Collider = collider, PrefabIndex = prefabIndex,
            Length = collider.size.y * Mathf.Abs(created.transform.localScale.y),
            Width = collider.size.x * Mathf.Abs(created.transform.localScale.x),
            Vehicle = new RoadTrafficVehicle()
        };
    }

    /// <summary>풀의 차량을 주변 경로에 배치한다. 실패 시 풀/물리 상태를 유지하며 후보 횟수에 상한을 둔다.</summary>
    private bool TrySpawn(bool initial)
    {
        if (spawnPrefabs.Count == 0 || spawnPaths.Count == 0 || instances.Count >= TargetVehicles)
        {
            return false;
        }
        // 임의 시작 번호부터 순환해 제한에 걸린 종류 때문에 다른 종류의 보충이 막히지 않게 한다.
        var prefabIndex = random.Next(spawnPrefabs.Count);
        var checkedCount = 0;
        while (checkedCount < spawnPrefabs.Count && (activePerPrefab[prefabIndex] >= MaximumPerPrefab || pools[prefabIndex].Count == 0))
        {
            prefabIndex = (prefabIndex + 1) % spawnPrefabs.Count;
            checkedCount++;
        }
        if (checkedCount == spawnPrefabs.Count)
        {
            return false;
        }
        var instance = pools[prefabIndex].Peek();
        if (instance.Root == null || instance.Body == null || instance.Collider == null)
        {
            Debug.LogError("교통 풀의 소유 차량이 외부에서 파괴되었습니다. 교통 AI를 다시 활성화해주세요.", this);
            enabled = false;
            return false;
        }
        // 최초 배치는 안전 거리 밖, 주행 중 보충은 화면 모서리 밖에서 수행한다.
        var laneWidth = network.Paths[0].Width;
        var minimumRadius = initial ? laneWidth * 7f : spawnRadius - laneWidth * 8f;
        RoadTrafficVehicle vehicle = null;
        for (var attempt = 0; attempt < SpawnAttempts; attempt++)
        {
            var pathId = spawnPaths[random.Next(spawnPaths.Count)];
            var path = network.Paths[pathId];
            if (path.Length <= instance.Length)
            {
                continue;
            }
            // 양 끝에서 차체 전체가 경로 안에 들어가도록 거리 후보를 제한한다.
            var distance = instance.Length * 0.5f + (float)random.NextDouble() * (path.Length - instance.Length);
            if (!path.TrySample(distance, out var point, out _)
                || (point - searchCenter).sqrMagnitude < minimumRadius * minimumRadius
                || (point - searchCenter).sqrMagnitude > spawnRadius * spawnRadius)
            {
                continue;
            }
            if (simulation.TryActivateVehicle(instance.Vehicle, pathId, distance, instance.Length, instance.Width,
                path.Width * (1.8f + (float)random.NextDouble() * 0.6f), obstacles, out vehicle))
            {
                break;
            }
        }
        if (vehicle == null)
        {
            return false;
        }
        // 재활성화 전에 생성 위치를 정한다. 비활성 바디의 위치 설정이 활성화 시 Transform으로 덮이지 않게 한다.
        var position = mapRoot.TransformPoint(vehicle.Position);
        var forward = mapRoot.TransformVector(vehicle.Forward);
        var rotation = Mathf.Atan2(forward.y, forward.x) * Mathf.Rad2Deg - 90f;
        instance.Root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, rotation));
        var body = instance.Body;
        pools[prefabIndex].Pop();
        activePerPrefab[prefabIndex]++;
        instances.Add(instance);
        instance.Root.SetActive(true);
        body.simulated = true;
        // 활성화되어 물리 월드에 등록된 바디에 초기 자세를 확정한다. 이후 지속 이동은 MovePosition/MoveRotation이 담당한다.
        body.position = position;
        body.rotation = rotation;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        body.WakeUp();
        return true;
    }

    /// <summary>예약과 관측 대상에서 제거하고 비활성 차체를 원래 풀로 반환한다.</summary>
    private void RemoveInstance(int index)
    {
        var instance = instances[index];
        simulation.RemoveVehicle(instance.Vehicle);
        activePerPrefab[instance.PrefabIndex]--;
        if (instance.Body != null)
        {
            instance.Body.linearVelocity = Vector2.zero;
            instance.Body.angularVelocity = 0f;
            instance.Body.simulated = false;
        }
        if (instance.Root != null)
        {
            instance.Root.SetActive(false);
        }
        pools[instance.PrefabIndex].Push(instance);
        instances.RemoveAt(index);
    }

    /// <summary>비활성화/종료 경계에서 전체 풀과 소유 상태를 해제한다. 사용자 오브젝트는 제거하지 않는다.</summary>
    private void ClearVehicles()
    {
        ReturnAllInstances();
        if (vehicleRoot != null)
        {
            vehicleRoot.SetActive(false);
            Destroy(vehicleRoot);
        }
        vehicleRoot = null;
        instances.Clear();
        pools.Clear();
        if (activePerPrefab != null)
        {
            Array.Clear(activePerPrefab, 0, activePerPrefab.Length);
        }
        ownedBodies.Clear();
        ownedColliders.Clear();
        obstacles.Clear();
        overlapResults.Clear();
        spawnPaths.Clear();
        simulation = null;
        network = null;
        version = -1;
        spawnClock = 0f;
        mapRoot = null;
        random = null;
        trafficCamera = null;
        playerCollider = null;
        spawnPrefabs.Clear();
    }

    /// <summary>기존 시뮬레이션의 예약과 활성 차체를 모두 반환한다. 맵 미준비/교체 동안 풀은 유지한다.</summary>
    private void ReturnAllInstances()
    {
        for (var index = instances.Count - 1; index >= 0; index--)
        {
            RemoveInstance(index);
        }
        obstacles.Clear();
        overlapResults.Clear();
        spawnPaths.Clear();
    }
}
