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
    // 차량 수/생성 간격은 게임플레이 튜닝 값이며 새 신호 현시는 항상 5초다.
    [SerializeField, Range(1, 64)] private int maximumVehicles = 32;
    [SerializeField, Min(0.2f)] private float spawnInterval = 1f;
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
            if (prefab != null && prefab.name != ExcludedPrefabName)
            {
                spawnPrefabs.Add(prefab);
            }
        }
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
                ClearVehicles();
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
            if (instance.Root == null || instance.Body == null || instance.Vehicle.IsFinished)
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
        if (spawnClock >= spawnInterval && instances.Count < maximumVehicles)
        {
            spawnClock = 0f;
            TrySpawn(false);
        }
    }

    /// <summary>프리팹의 소유권과 물리 모드를 명확히 확인한다. 이미 제어 스크립트/물리가 있는 프리팹은 덮어쓰지 않는다.</summary>
    public bool ValidateConfiguration(out string error)
    {
        error = null;
        if (mapGenerator == null || mapGenerator.LaneMapRoot == null || player == null
            || vehiclePrefabs == null || vehiclePrefabs.Length == 0 || maximumVehicles < 1 || maximumVehicles > 64
            || !RoadLaneMath.IsFinite(spawnInterval) || spawnInterval < 0.2f)
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

    /// <summary>교체된 맵마다 기존 차량/신호를 정리하고 새 스냅숏을 바인딩한다. 후송 확장도 동일한 소유 경계다.</summary>
    private void ResetNetwork(RoadLaneNetwork current)
    {
        ClearVehicles();
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
        vehicleRoot = new GameObject("RuntimeTrafficVehicles");
        vehicleRoot.transform.SetParent(mapRoot, false);
        // 맵 전체를 한 번의 2D 쿼리로 관측한다. 레이어/트리거 정책은 명시적이며 AI 차체는 별도 관측으로 중복을 제외한다.
        var bounds = new Bounds(network.Roads[0].Position, Vector3.zero);
        foreach (var road in network.Roads)
        {
            bounds.Encapsulate((Vector3)(road.Position - road.Layout.ImageSize * 0.5f));
            bounds.Encapsulate((Vector3)(road.Position + road.Layout.ImageSize * 0.5f));
        }
        searchCenter = bounds.center;
        searchRadius = ((Vector2)bounds.extents).magnitude + network.Paths[0].Width * 4f;
        // 초기에는 맵 전체 직선에 분산하고, 이후에는 외부 경계의 진입 차로에서만 생성한다.
        var incoming = new int[network.Paths.Count];
        foreach (var path in network.Paths)
        {
            foreach (var next in path.Successors)
            {
                incoming[next]++;
            }
        }
        spawnPaths.Clear();
        foreach (var path in network.Paths)
        {
            if (path.StartsAtBoundary && incoming[path.Id] == 0 && path.Successors.Count > 0)
            {
                spawnPaths.Add(path.Id);
            }
        }
        CollectObstacles();
        for (var count = 0; count < maximumVehicles; count++)
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

    /// <summary>기존 외형 프리팹을 런타임에 복제하고 이 작업 소유의 Kinematic Rigidbody2D와 박스 차체를 추가한다.</summary>
    private bool TrySpawn(bool initial)
    {
        if (spawnPrefabs.Count == 0 || (!initial && spawnPaths.Count == 0))
        {
            return false;
        }
        // 실패 후보는 즉시 비활성화/파괴한다. 최대 12회 후보만 확인해 정체 시 생성 루프를 제한한다.
        var prefab = spawnPrefabs[random.Next(spawnPrefabs.Count)];
        if (prefab == null)
        {
            return false;
        }
        var created = Instantiate(prefab, vehicleRoot.transform);
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
        // 생성 위치에서 차량 로컬 up은 맵의 up이다. 월드 크기는 맵의 균일 스케일로 나누어 그래프 단위로 환산한다.
        var length = collider.size.y * Mathf.Abs(created.transform.localScale.y);
        var width = collider.size.x * Mathf.Abs(created.transform.localScale.x);
        RoadTrafficVehicle vehicle = null;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var pathId = initial ? random.Next(network.Paths.Count) : spawnPaths[random.Next(spawnPaths.Count)];
            var path = network.Paths[pathId];
            if (initial && path.Kind != RoadLanePathKind.Straight)
            {
                continue;
            }
            var distance = initial ? path.Length * (0.2f + (float)random.NextDouble() * 0.6f) : length * 0.5f;
            if (!path.TrySample(distance, out var point, out _)
                || RoadLaneMath.Distance(point, mapRoot.InverseTransformPoint(player.position)) < path.Width * 7f)
            {
                continue;
            }
            if (simulation.TryAddVehicle(pathId, distance, length, width,
                path.Width * (1.8f + (float)random.NextDouble() * 0.6f), obstacles, out vehicle))
            {
                break;
            }
        }
        if (vehicle == null)
        {
            Destroy(created);
            return false;
        }
        // 물리 바디를 추가하기 전에 생성 위치를 정한다. 비활성 바디의 위치 설정이 활성화 시 Transform으로 덮이지 않게 한다.
        var position = mapRoot.TransformPoint(vehicle.Position);
        var forward = mapRoot.TransformVector(vehicle.Forward);
        var rotation = Mathf.Atan2(forward.y, forward.x) * Mathf.Rad2Deg - 90f;
        created.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, rotation));
        var body = created.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.simulated = true;
        body.gravityScale = 0f;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.useFullKinematicContacts = true;
        instances.Add(new VehicleInstance { Root = created, Body = body, Collider = collider, Vehicle = vehicle });
        ownedBodies.Add(body);
        ownedColliders.Add(collider);
        created.SetActive(true);
        // 활성화되어 물리 월드에 등록된 바디에 초기 자세를 확정한다. 이후 지속 이동은 MovePosition/MoveRotation이 담당한다.
        body.position = position;
        body.rotation = rotation;
        body.WakeUp();
        return true;
    }

    /// <summary>개별 생성 차량을 그래프와 네이티브 월드 양쪽에서 제거한다.</summary>
    private void RemoveInstance(int index)
    {
        var instance = instances[index];
        simulation.RemoveVehicle(instance.Vehicle);
        ownedBodies.Remove(instance.Body);
        ownedColliders.Remove(instance.Collider);
        if (instance.Root != null)
        {
            instance.Root.SetActive(false);
            Destroy(instance.Root);
        }
        instances.RemoveAt(index);
    }

    /// <summary>활성 수명 또는 네트워크 교체 경계에서 전체 소유 상태를 비운다. 사용자 오브젝트는 제거하지 않는다.</summary>
    private void ClearVehicles()
    {
        if (vehicleRoot != null)
        {
            vehicleRoot.SetActive(false);
            Destroy(vehicleRoot);
        }
        vehicleRoot = null;
        instances.Clear();
        ownedBodies.Clear();
        ownedColliders.Clear();
        obstacles.Clear();
        spawnPaths.Clear();
        simulation = null;
        network = null;
        version = -1;
        spawnClock = 0f;
    }
}
