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
    private const int MaximumPerPrefab = 2; // 같은 차종은 주변에 최대 두 대까지 배치한다.
    private const int SpawnAttempts = 24;
    private const int LocalTrafficTarget = 6; // 전방 교차로를 미리 채울 때도 현재 도로에 남겨둘 목표 대수다.
    // 외부 차량 바디를 추가로 관측할 레이어다. 플레이어와 소유 AI 감지는 이 마스크와 별도로 유지한다.
    [SerializeField, Tooltip("외부 Dynamic/Kinematic Rigidbody2D 감지 레이어. 정적 도로/장식 콜라이더는 제외하며 플레이어와 AI 차량은 별도 감지합니다.")]
    private LayerMask obstacleLayers = ~0;
    // 저장된 씬에 PCX 참조가 남아 있어도 생성하지 않는 명시적 제외 정책이다.
    private const string ExcludedPrefabName = "PCX";
    // 네트워크와 인스턴스는 이 컴포넌트의 활성 수명에만 존재한다. 검색 버퍼는 잘림 없는 List다.
    private RoadTrafficSimulation simulation;
    private RoadCitizenController citizens; // 같은 고정 스텝에서 차량보다 먼저 점유를 확정하는 시민 시스템이다.
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
    private readonly List<int> localSpawnPaths = new List<int>(); // 현재 도로 양방향의 생성 후보다.
    private readonly List<int> junctionSpawnPaths = new List<int>(); // 진입 전에 준비할 전방 교차로의 생성 후보다.
    private readonly HashSet<int> spawnRoads = new HashSet<int>(); // 전방 교차로와 직접 연결된 도로 번호다.
    private readonly HashSet<int> localRoads = new HashSet<int>(); // 현재 도로와 이어지는 직선 도로 번호다.
    private readonly Queue<int> connectedRoadQueue = new Queue<int>(); // 직선 구간 전체를 중복 없이 탐색하는 재사용 버퍼다.
    private Vector2 trafficForward; // 현재 도로의 정방향과 반대 방향 차량 수를 구분할 기준이다.
    private Vector2 supplyForward;  // 실제 이동 방향의 화면 밖에 보충 차량을 준비할 기준이다.
    private readonly Plane[] cameraPlanes = new Plane[6]; // 카메라 회전과 화면 비율을 반영하는 재사용 시야 평면이다.
    // 프리팹별 최대 2개의 차체와 managed 주행 상태를 소유한다. 활성 목록과 풀은 중복 소유하지 않는다.
    private readonly List<Stack<VehicleInstance>> pools = new List<Stack<VehicleInstance>>();
    private int[] activePerPrefab;
    private Camera trafficCamera;
    private float spawnRadius;
    private float despawnRadius;
    private float preparedRadius; // 전방 교차로 차량이 도착 전에 회수되지 않도록 보존할 반경이다.
    private float localTrafficRadius; // 먼 직선이나 교차로 차량을 주변 교통으로 세지 않을 근거리 반경이다.
    private int TargetVehicles => Math.Min(maximumVehicles * trafficDensity, spawnPrefabs.Count * MaximumPerPrefab);
    private System.Random random;
    private int version = -1;
    // 동일 미션의 후송 확장인 경우에만 활성 차량을 보존한다.
    private int mapSession = -1;
    private float spawnClock;
    private float searchRadius;
    private Vector2 searchCenter;
    private float mapScale;
    // 플레이어 차체는 활성화 시 캐시하고 파괴되면 런타임 검증에서 중단한다.
    private Collider2D playerCollider;
    public RoadTrafficSimulation Simulation => isActiveAndEnabled ? simulation : null;
    internal RoadLaneNetwork SignalNetwork => isActiveAndEnabled ? network : null; // 신호와 도로 번호가 일치하는 스냅숏이다.

    /// <summary>실제 차체와 순수 AI 상태의 소유 관계다. Finished 또는 파괴된 차체는 다음 스텝에 제거한다.</summary>
    private sealed class VehicleInstance
    {
        // 오브젝트와 Rigidbody는 컨트롤러가 생성/제거하며 프리팹 원본은 수정하지 않는다.
        internal GameObject Root;
        internal Rigidbody2D Body;
        internal BoxCollider2D Collider;
        internal RoadTrafficVehicle Vehicle;
        internal RoadTrafficCollision Collision;
        // 반환 대상 풀 번호와 그래프 단위의 차체 크기는 생성 시 한 번 계산한다.
        internal int PrefabIndex;
        internal float Length;
        internal float Width;
        internal float VisualRadius; // 차체 중심에서 외형 끝까지의 보수적인 맵 로컬 반경이다.
    }

    /// <summary>활성화마다 의존성을 다시 검증한다. 설정은 Play 이전에 완료되어 있어야 한다.</summary>
    private void OnEnable()
    {
        citizens = GetComponent<RoadCitizenController>();
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
        if (citizens != null) citizens.Clear();
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
                if (citizens != null) citizens.Clear();
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
        foreach (var instance in instances)
        {
            if (instance.Body == null || instance.Collision == null)
            {
                continue;
            }
            if (instance.Collision.IsReacting && !instance.Vehicle.IsPhysicsControlled)
            {
                // 접촉 후에는 속도/차로 변경/교차로 예약을 폐기한다. 같은 수명에서 AI로 복귀하지 않는다.
                instance.Vehicle.Speed = 0f;
                instance.Vehicle.TargetSpeed = 0f;
                instance.Vehicle.TargetLaneId = -1;
                instance.Vehicle.ReservedMovement = -1;
                instance.Vehicle.ExitClearanceRemaining = -1f;
            }
            instance.Vehicle.IsPhysicsControlled = instance.Collision.IsReacting;
        }
        CollectObstacles();
        var remaining = Time.fixedDeltaTime;
        while (remaining > 0f)
        {
            var step = Mathf.Min(0.1f, remaining);
            if (citizens != null)
            {
                citizens.Step(mapGenerator, network, simulation, searchCenter, step, obstacles);
            }
            simulation.Tick(step, obstacles);
            remaining -= step;
        }
        for (var index = instances.Count - 1; index >= 0; index--)
        {
            var instance = instances[index];
            // 주변 사고 차량은 물리 제어로 남기되 멀리 벗어난 차체는 반환해 생성 풀이 고갈되지 않게 한다.
            if (instance.Root != null && instance.Body != null && instance.Collision.IsReacting)
            {
                // 충돌 뒤 AI 경로 위치는 갱신되지 않으므로 실제 차체 중심으로 회수 여부를 판단한다.
                var wreckPosition = (Vector2)mapRoot.InverseTransformPoint(instance.Collider.bounds.center);
                if ((wreckPosition - searchCenter).sqrMagnitude > despawnRadius * despawnRadius
                    && !IsVisible(wreckPosition, instance.VisualRadius))
                {
                    RemoveInstance(index);
                }
                continue;
            }
            if (instance.Root == null || instance.Body == null || instance.Vehicle.IsFinished
                || !mapGenerator.IsRoadActive(network.Paths[instance.Vehicle.PathId].RoadOrdinal)
                || (instance.Vehicle.Position - searchCenter).sqrMagnitude > despawnRadius * despawnRadius)
            {
                RemoveInstance(index);
                continue;
            }
            // Kinematic 2D 바디의 지속 이동은 FixedUpdate의 MovePosition/MoveRotation으로만 적용한다.
            var position = mapRoot.TransformPoint(instance.Vehicle.Position);
            var forward = mapRoot.TransformVector(instance.Vehicle.Forward);
            var rotation = Mathf.Atan2(forward.y, forward.x) * Mathf.Rad2Deg - 90f;
            instance.Body.MovePosition(GetBodyPosition(instance, position, rotation));
            instance.Body.MoveRotation(rotation);
        }
        spawnClock += Time.fixedDeltaTime;
        if (spawnClock >= spawnInterval / trafficDensity)
        {
            spawnClock = 0f;
            RefreshSpawnPaths();
            RebalanceTraffic();
            FillTraffic(false);
        }
    }

    /// <summary>프리팹에 저장된 차체 콜라이더를 검증한다. Rigidbody2D와 제어 스크립트는 생성기가 소유한다.</summary>
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
                || prefab.GetComponentsInChildren<MonoBehaviour>(true).Length > 0)
            {
                error = "Vehicle Prefabs에는 SpriteRenderer가 있고 Rigidbody2D/제어 스크립트가 없는 차량 프리팹을 연결해주세요.";
                return false;
            }
            var colliders = prefab.GetComponentsInChildren<Collider2D>(true);
            var collider = prefab.GetComponent<BoxCollider2D>();
            if (colliders.Length != 1 || collider == null || !collider.enabled || collider.isTrigger
                || !RoadLaneMath.IsFinite(collider.size.x) || !RoadLaneMath.IsFinite(collider.size.y)
                || collider.size.x <= 0f || collider.size.y <= 0f
                || !RoadLaneMath.IsFinite(collider.offset.x) || !RoadLaneMath.IsFinite(collider.offset.y))
            {
                error = $"{prefab.name}: 루트에 활성화된 비트리거 Box Collider 2D 하나가 필요합니다. Size는 양수, Offset은 유한한 값으로 설정해주세요.";
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

    /// <summary>후송 확장은 활성 차량과 신호를 보존한다. 새 미션에서만 차량을 풀로 반환한다.</summary>
    private void ResetNetwork(RoadLaneNetwork current)
    {
        // 같은 맵 변환과 세션에서 기존 경로 번호를 유지하는 확장만 차량 상태를 승계한다.
        var preservesTraffic = simulation != null && mapRoot != null && mapSession == mapGenerator.LaneNetworkSession
            && mapRoot == mapGenerator.LaneMapRoot && HasSupportedMapTransform()
            && simulation.TryExtendNetwork(current);
        if (!preservesTraffic)
        {
            ReturnAllInstances();
        }
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
        mapSession = mapGenerator.LaneNetworkSession;
        if (!preservesTraffic)
        {
            random = new System.Random(mapGenerator.CurrentSeed);
            simulation = new RoadTrafficSimulation(network, mapGenerator.CurrentSeed);
            spawnClock = 0f;
        }
        if (mapGenerator.TryGetPickupTrafficZone(out var pickupPath, out var pickupStart, out var pickupEnd))
        {
            var ambulanceRenderer = player.GetComponent<SpriteRenderer>();
            var ambulanceLength = ambulanceRenderer != null && ambulanceRenderer.sprite != null
                ? ambulanceRenderer.sprite.bounds.size.y * Mathf.Abs(player.transform.lossyScale.y) / mapScale
                : playerCollider.bounds.size.magnitude / mapScale;
            if (playerCollider is BoxCollider2D ambulanceBox)
            {
                ambulanceLength = Mathf.Max(ambulanceLength,
                    ambulanceBox.size.y * Mathf.Abs(player.transform.lossyScale.y) / mapScale);
            }
            simulation.ConfigurePickupZone(pickupPath, pickupStart, pickupEnd, ambulanceLength * 3f);
        }
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
        FillTraffic(!preservesTraffic);
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
        // 사고 차량은 AI 경로의 가상 위치 대신 실제 콜라이더 위치로 관측한다.
        foreach (var instance in instances)
        {
            if (instance.Body != null && instance.Collider != null && instance.Collision.IsReacting)
            {
                AddObstacle(instance.Collider, instance.Body);
            }
        }
    }

    /// <summary>하나의 콜라이더를 보수적인 정사각 차체 관측으로 등록한다. 회전한 AABB는 원래 차체보다 작게 계산하지 않는다.</summary>
    private void AddObstacle(Collider2D collider, Rigidbody2D body)
    {
        var bounds = collider.bounds;
        var position = (Vector2)mapRoot.InverseTransformPoint(bounds.center);
        var velocity = body != null ? (Vector2)mapRoot.InverseTransformVector(body.linearVelocity) : Vector2.zero;
        var size = Mathf.Max(bounds.size.x, bounds.size.y) / mapScale;
        // 외부 차량도 실제 차체의 로컬 up을 사용한다. 가로 도로에서 후방 차량의 방향을 잘못 판단하지 않는다.
        var forward = body != null ? (Vector2)mapRoot.InverseTransformVector(body.transform.up).normalized : Vector2.up;
        obstacles.Add(new RoadTrafficObstacle(-obstacles.Count - 1, position, velocity, forward, size, size));
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
        // 긴 직선에서도 이웃 한 칸 너머에 화면 밖 보충 공간을 확보한다.
        var corridorRadius = network.Roads[0].Layout.ImageSize.x * 2f;
        localTrafficRadius = Mathf.Max(corridorRadius * 0.75f, viewRadius + laneWidth * 8f);
        spawnRadius = Mathf.Max(preparedRadius, Mathf.Max(corridorRadius,
            Mathf.Max(laneWidth * spawnRadiusInLaneWidths, viewRadius + laneWidth * 8f)));
        despawnRadius = Mathf.Max(laneWidth * despawnRadiusInLaneWidths, spawnRadius + laneWidth * 8f);
        searchRadius = despawnRadius + laneWidth * 12f;
    }

    // 현재 도로 양방향과 미리 준비할 전방 교차로를 별도 후보로 유지한다.
    private void RefreshSpawnPaths()
    {
        preparedRadius = 0f;
        UpdateSearchArea();
        spawnPaths.Clear();
        localSpawnPaths.Clear();
        junctionSpawnPaths.Clear();
        spawnRoads.Clear();
        localRoads.Clear();
        var currentRoad = -1;
        var currentSquared = float.PositiveInfinity;
        trafficForward = ((Vector2)mapRoot.InverseTransformVector(player.transform.up)).normalized;
        if (trafficCamera != null)
        {
            GeometryUtility.CalculateFrustumPlanes(trafficCamera, cameraPlanes);
        }
        // 차체 방향 대신 현재 차로의 접선으로 양방향 교통의 보충 우선순위를 정한다.
        var laneSquared = network.Paths[0].Width * network.Paths[0].Width;
        foreach (var path in network.Paths)
        {
            if (path.Kind != RoadLanePathKind.Intersection && mapGenerator.IsRoadActive(path.RoadOrdinal)
                && path.TryProject(searchCenter, out var along, out var squared) && squared < laneSquared)
            {
                laneSquared = squared;
                path.TrySample(along, out _, out trafficForward);
            }
        }
        for (var index = 0; index < network.Roads.Count; index++)
        {
            var squared = (network.Roads[index].Position - searchCenter).sqrMagnitude;
            if (mapGenerator.IsRoadActive(index) && squared < currentSquared)
            {
                currentSquared = squared;
                currentRoad = index;
            }
        }
        if (currentRoad >= 0 && !network.Roads[currentRoad].Layout.IsIntersection)
        {
            localRoads.Add(currentRoad);
            AddConnectedRoads(currentRoad, localRoads, false);
        }
        // 현재 시야 반경에 두 도로 타일을 더해 교차로에 도달하기 전에 배치를 시작한다.
        var tileSize = network.Roads[0].Layout.ImageSize.magnitude;
        var preloadRadius = spawnRadius + tileSize * 2f;
        var travelForward = player.linearVelocity.sqrMagnitude > 0.01f
            ? ((Vector2)mapRoot.InverseTransformVector(player.linearVelocity)).normalized : trafficForward;
        supplyForward = travelForward;
        var nearestRoad = -1;
        var nearestSquared = float.PositiveInfinity;
        for (var index = 0; index < network.Roads.Count; index++)
        {
            var road = network.Roads[index];
            var delta = road.Position - searchCenter;
            var squared = delta.sqrMagnitude;
            if (road.Layout.IsIntersection && mapGenerator.IsRoadActive(index) && squared < nearestSquared
                && squared <= preloadRadius * preloadRadius
                && (index == currentRoad || Vector2.Dot(delta, travelForward) > 0f))
            {
                nearestRoad = index;
                nearestSquared = squared;
            }
        }
        if (nearestRoad >= 0)
        {
            spawnRoads.Add(nearestRoad);
            AddConnectedRoads(nearestRoad, spawnRoads, true);
            // 미리 배치한 차량이 플레이어의 거리 기반 회수 조건에 바로 걸리지 않도록 한다.
            spawnRadius = Mathf.Max(spawnRadius, Mathf.Sqrt(nearestSquared) + tileSize * 2f);
            preparedRadius = spawnRadius;
            despawnRadius = Mathf.Max(despawnRadius, spawnRadius + tileSize);
        }
        foreach (var path in network.Paths)
        {
            if (path.IsTrafficAllowed && mapGenerator.IsRoadActive(path.RoadOrdinal)
                && path.Kind != RoadLanePathKind.Intersection && path.Successors.Count > 0
                && path.TryProject(searchCenter, out var along, out _)
                && path.TrySample(along, out var nearest, out _)
                && (nearest - searchCenter).sqrMagnitude <= spawnRadius * spawnRadius)
            {
                if (localRoads.Contains(path.RoadOrdinal))
                {
                    localSpawnPaths.Add(path.Id);
                    spawnPaths.Add(path.Id);
                }
                else if (spawnRoads.Contains(path.RoadOrdinal))
                {
                    junctionSpawnPaths.Add(path.Id);
                    spawnPaths.Add(path.Id);
                }
            }
        }
        // 현재 도로를 먼저 채워 교차로 선행 배치가 주변 교통을 비우지 않도록 한다.
        localSpawnPaths.Sort((left, right) =>
        {
            var leftPath = network.Paths[left];
            var rightPath = network.Paths[right];
            var leftCurrent = leftPath.RoadOrdinal == currentRoad;
            var rightCurrent = rightPath.RoadOrdinal == currentRoad;
            if (leftCurrent != rightCurrent) return leftCurrent ? -1 : 1;
            leftPath.TryProject(searchCenter, out _, out var leftSquared);
            rightPath.TryProject(searchCenter, out _, out var rightSquared);
            return leftSquared.CompareTo(rightSquared);
        });
        searchRadius = despawnRadius + network.Paths[0].Width * 12f;
    }

    // 교차로는 바로 연결된 출구를, 현재 직선은 반경 안에서 다음 교차로 전까지 연결된 구간을 수집한다.
    private void AddConnectedRoads(int road, HashSet<int> result, bool includeIntersections)
    {
        connectedRoadQueue.Clear();
        connectedRoadQueue.Enqueue(road);
        var limit = spawnRadius + network.Roads[0].Layout.ImageSize.x;
        while (connectedRoadQueue.Count > 0)
        {
            var current = connectedRoadQueue.Dequeue();
            foreach (var path in network.Paths)
            {
                foreach (var successor in path.Successors)
                {
                    var next = network.Paths[successor].RoadOrdinal;
                    var neighbor = path.RoadOrdinal == current ? next : next == current ? path.RoadOrdinal : -1;
                    if (neighbor < 0 || !mapGenerator.IsRoadActive(neighbor)
                        || (!includeIntersections && (network.Roads[neighbor].Layout.IsIntersection
                            || (network.Roads[neighbor].Position - searchCenter).sqrMagnitude > limit * limit)))
                    {
                        continue;
                    }
                    if (result.Add(neighbor) && !includeIntersections) connectedRoadQueue.Enqueue(neighbor);
                }
            }
            if (includeIntersections) break;
        }
    }

    // 실제 주행 위치로 현재 도로 차량을 세어 양방향 보충 우선순위를 정한다.
    private int CountLocalTraffic(int direction = 0)
    {
        var count = 0;
        foreach (var instance in instances)
        {
            if (IsLocalTraffic(instance)
                && (direction == 0 || Vector2.Dot(instance.Vehicle.Forward, trafficForward) * direction > 0f))
            {
                count++;
            }
        }
        return count;
    }

    // 같은 직선 위라도 멀리 뒤처진 차량은 화면 주변의 보충 목표를 차지하지 않는다.
    private bool IsLocalTraffic(VehicleInstance instance)
    {
        var delta = instance.Vehicle.Position - searchCenter;
        return localRoads.Contains(network.Paths[instance.Vehicle.PathId].RoadOrdinal)
            && delta.sqrMagnitude <= localTrafficRadius * localTrafficRadius
            && Vector2.Dot(delta, supplyForward) >= -network.Paths[0].Width * 4f;
    }

    // 다음 교차로의 선행 배치가 현재 주행 구간의 차량을 모두 차지하지 않도록 절반 이상을 예약한다.
    private int GetLocalTrafficTarget()
    {
        return junctionSpawnPaths.Count > 0 ? Mathf.Min(TargetVehicles, Mathf.Max(LocalTrafficTarget, (TargetVehicles + 1) / 2))
            : TargetVehicles;
    }

    // 현재 도로 양방향을 먼저 확보하고 남은 수량으로 전방 교차로를 미리 채운다.
    private void FillTraffic(bool allowVisible)
    {
        var localTarget = GetLocalTrafficTarget();
        for (var attempt = 0; attempt < TargetVehicles * 2 && instances.Count < TargetVehicles && isActiveAndEnabled; attempt++)
        {
            var needsLocal = CountLocalTraffic() < localTarget && localSpawnPaths.Count > 0;
            var preferred = needsLocal ? localSpawnPaths : junctionSpawnPaths;
            var direction = needsLocal ? (CountLocalTraffic(1) <= CountLocalTraffic(-1) ? 1 : -1) : 0;
            if (!TrySpawn(preferred, allowVisible, direction)
                && !TrySpawn(preferred, allowVisible, -direction))
            {
                TrySpawn(spawnPaths, allowVisible, 0);
            }
        }
    }

    // 시야 밖의 이전 구역 차량만 반환해 현재 도로와 다음 교차로의 배치 공간을 확보한다.
    private void RebalanceTraffic()
    {
        var localCount = CountLocalTraffic();
        var localTarget = GetLocalTrafficTarget();
        var junctionCount = instances.Count - localCount;
        for (var index = instances.Count - 1; index >= 0; index--)
        {
            var instance = instances[index];
            if (instance.Collision.IsReacting || IsVisible(instance.Vehicle.Position, instance.VisualRadius))
            {
                continue;
            }
            var road = network.Paths[instance.Vehicle.PathId].RoadOrdinal;
            var isLocal = IsLocalTraffic(instance);
            if ((!localRoads.Contains(road) && !spawnRoads.Contains(road))
                || (isLocal && localCount > localTarget && junctionSpawnPaths.Count > 0)
                || (!isLocal && localCount < localTarget && junctionCount > TargetVehicles - localTarget))
            {
                RemoveInstance(index);
                if (isLocal) localCount--;
                else junctionCount--;
            }
        }
    }

    // 차체 외형과 화면 가장자리 여유까지 포함해 회전한 카메라의 시야와 겹치는지 검사한다.
    private bool IsVisible(Vector2 position, float radius)
    {
        if (trafficCamera == null)
        {
            // 카메라가 없으면 주행 중 갑작스러운 생성을 허용하지 않는다.
            return true;
        }
        var worldRadius = (radius + network.Paths[0].Width * 2f) * mapScale;
        return GeometryUtility.TestPlanesAABB(cameraPlanes,
            new Bounds(mapRoot.TransformPoint(position), Vector3.one * (worldRadius * 2f)));
    }

    /// <summary>저장된 콜라이더 설정을 그대로 복제하고 Kinematic 바디만 풀 예열 시 추가한다.</summary>
    private VehicleInstance CreateInstance(int prefabIndex)
    {
        var created = Instantiate(spawnPrefabs[prefabIndex], vehicleRoot.transform);
        created.SetActive(false);
        var collider = created.GetComponent<BoxCollider2D>();
        var body = created.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.simulated = false;
        body.gravityScale = 0f;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.useFullKinematicContacts = true;
        var collision = created.AddComponent<RoadTrafficCollision>();
        // 원본 이름으로 차종별 공차중량을 한 번 설정한다. 풀에서 다시 꺼낼 때도 이 질량을 유지한다.
        collision.ConfigureVehicleModel(spawnPrefabs[prefabIndex].name);
        var sprite = created.GetComponentInChildren<SpriteRenderer>(true);
        var spriteSize = Vector3.Scale(sprite.sprite.bounds.size, sprite.transform.lossyScale) / mapScale;
        var spriteCenter = sprite.transform.TransformPoint(sprite.sprite.bounds.center);
        var colliderCenter = collider.transform.TransformPoint(collider.offset);
        var visualRadius = spriteSize.magnitude * 0.5f + Vector3.Distance(spriteCenter, colliderCenter) / mapScale;
        // 풀 내부 차체도 소유 목록에 유지해 외부 장애물 관측에서 제외한다.
        ownedBodies.Add(body);
        ownedColliders.Add(collider);
        return new VehicleInstance
        {
            Root = created, Body = body, Collider = collider, PrefabIndex = prefabIndex,
            Length = collider.size.y * Mathf.Abs(created.transform.localScale.y),
            Width = collider.size.x * Mathf.Abs(created.transform.localScale.x), VisualRadius = visualRadius,
            Vehicle = new RoadTrafficVehicle(), Collision = collision
        };
    }

    /// <summary>AI 위치는 콜라이더 중심이다. Inspector의 Offset 변경을 회전된 차체 위치에 반영한다.</summary>
    private static Vector3 GetBodyPosition(VehicleInstance instance, Vector3 center, float rotation)
    {
        var offset = Vector3.Scale((Vector3)instance.Collider.offset, instance.Root.transform.lossyScale);
        return center - Quaternion.Euler(0f, 0f, rotation) * offset;
    }

    /// <summary>풀의 차량을 주변 경로에 배치한다. 실패 시 풀/물리 상태를 유지하며 후보 횟수에 상한을 둔다.</summary>
    private bool TrySpawn(List<int> candidates, bool allowVisible, int direction)
    {
        if (spawnPrefabs.Count == 0 || candidates.Count == 0 || instances.Count >= TargetVehicles)
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
        // 플레이어 차선도 보충하되 바로 옆과 화면 안에는 생성하지 않는다. 앞뒤 차체 간격도 검사한다.
        var laneWidth = network.Paths[0].Width;
        var minimumRadius = laneWidth * 2f;
        var isLocalRefill = !allowVisible && ReferenceEquals(candidates, localSpawnPaths);
        RoadTrafficVehicle vehicle = null;
        var pathOffset = ReferenceEquals(candidates, localSpawnPaths) ? 0 : random.Next(candidates.Count);
        var distanceOffset = random.Next(SpawnAttempts);
        // 무작위 재추첨으로 빈 차로를 놓치지 않도록 모든 후보 차로의 거리 구간을 순회한다.
        for (var attempt = 0; attempt < candidates.Count * SpawnAttempts; attempt++)
        {
            var pathId = candidates[(pathOffset + attempt) % candidates.Count];
            var path = network.Paths[pathId];
            if (!mapGenerator.IsRoadActive(path.RoadOrdinal) || path.Length <= instance.Length)
            {
                continue;
            }
            // 양 끝에서 차체 전체가 경로 안에 들어가도록 거리 후보를 제한한다.
            var slot = (distanceOffset + attempt / candidates.Count) % SpawnAttempts;
            var usableLength = path.Length - instance.Length;
            var offset = (float)slot / (SpawnAttempts - 1) * usableLength;
            if (path.Kind == RoadLanePathKind.Straight)
            {
                // 차체를 회전시키지 않고 차로 번호에 따라 앞뒤 위치를 엇갈리게 배치한다.
                offset = Mathf.Repeat(offset + (path.EntryLaneNumber - 1) * (instance.Length + path.Width), usableLength);
            }
            var distance = instance.Length * 0.5f + offset;
            if (!path.TrySample(distance, out var point, out var laneForward)
                || (direction != 0 && Vector2.Dot(laneForward, trafficForward) * direction <= 0f)
                || (!allowVisible && IsVisible(point, instance.VisualRadius))
                // 현재 타일 뒤쪽 빈자리만 반복해서 채우지 않고 진행 방향의 화면 밖에서 보충한다.
                || (isLocalRefill && (Vector2.Dot(point - searchCenter, supplyForward) < 0f
                    || (point - searchCenter).sqrMagnitude > localTrafficRadius * localTrafficRadius))
                || (point - searchCenter).sqrMagnitude < minimumRadius * minimumRadius
                || (point - searchCenter).sqrMagnitude > spawnRadius * spawnRadius)
            {
                continue;
            }
            if (!simulation.HasStaggeredSpawnSpace(pathId, distance, instance.Length)) continue;
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
        var center = mapRoot.TransformPoint(vehicle.Position);
        var forward = mapRoot.TransformVector(vehicle.Forward);
        var rotation = Mathf.Atan2(forward.y, forward.x) * Mathf.Rad2Deg - 90f;
        var position = GetBodyPosition(instance, center, rotation);
        instance.Root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, rotation));
        var body = instance.Body;
        instance.Collision.ResetForSpawn();
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
        spawnRoads.Clear();
        localSpawnPaths.Clear();
        junctionSpawnPaths.Clear();
        localRoads.Clear();
        preparedRadius = 0f;
        simulation = null;
        network = null;
        version = -1;
        mapSession = -1;
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
        spawnRoads.Clear();
        localSpawnPaths.Clear();
        junctionSpawnPaths.Clear();
        localRoads.Clear();
        preparedRadius = 0f;
    }
}
