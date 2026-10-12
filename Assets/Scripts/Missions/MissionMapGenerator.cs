using System.Collections.Generic;
using UnityEngine;

/// <summary>시드 계획을 기존 프리팹 연결 정보로 배치한다. 생성한 구역의 소유자이며 다음 미션에서 풀로 재사용한다.</summary>
[DisallowMultipleComponent]
public sealed class MissionMapGenerator : MonoBehaviour
{
    // 현재 도로 원본의 1254px 규격에서 확인한 차선 폭 84.6px와 도보 폭 64px 비율이다.
    private const float LaneWidthRatio = 84.6f / 1254f;
    private const float SidewalkWidthRatio = 64f / 1254f;
    private const int BranchRoadCount = 2;
    // 에디터 설정 메뉴로 연결하는 원본 카탈로그, 맵 부모, 마지막 구역의 도착 표시다.
    [SerializeField] private RoadPrefabCatalog catalog;
    [SerializeField] private Transform mapRoot;
    [SerializeField] private Transform destination;
    // 첫 수락 이후 숨길 기존 GamePlay 도로 미리보기다. 원본 씬 오브젝트를 삭제하지 않는다.
    [SerializeField] private GameObject roadPreview;

    // 진행 경로 39개와 미선택 출구의 직선 최대 12개를 단계별로 소유하고 같은 풀에서 재사용한다.
    private readonly List<RoadPrefab> activeRoads = new List<RoadPrefab>(51);
    private readonly Dictionary<RoadIndex, Stack<RoadPrefab>> pool = new Dictionary<RoadIndex, Stack<RoadPrefab>>();
    private Transform poolRoot;
    // 안내 대상은 추가 분기를 제외한 진행 경로다. 현재 단계의 월드 좌표만 보관한다.
    private readonly List<Vector2> navigationRoute = new List<Vector2>(42);
    // 두 단계까지만 연결한다. 동일한 픽업/후송 시드 쌍은 풀 상태와 무관하게 같은 배치를 만든다.
    private int pickupSeed;
    private bool hasTransport;
    // 마지막 출구 방향과 프리팹 회전은 맵 부모 기준으로 저장한다.
    private RoadDirection finalDirection;
    private int finalRotation;

    /// <summary>현재 배치된 도로 목록이다. 다음 미션에서 풀로 반환되므로 소비자는 영구 참조로 보관하지 않는다.</summary>
    public IReadOnlyList<RoadPrefab> ActiveRoads => activeRoads;
    /// <summary>현재 목적지까지 실제 연결 순서로 이어진 도로 중심 경로다.</summary>
    public IReadOnlyList<Vector2> NavigationRoute => navigationRoute;

    /// <summary>성공적으로 배치된 맵의 시드다. 생성 전에는 0이다.</summary>
    public int CurrentSeed { get; private set; }
    /// <summary>첫 구역 안의 시작 위치다. Rigidbody 순간이동에 사용하는 월드 좌표다.</summary>
    public Vector2 StartPosition { get; private set; }
    /// <summary>첫 구역의 북쪽에 맞춘 구급차 시작 회전이다. 단위는 월드 Z축 각도다.</summary>
    public float StartRotation => mapRoot != null ? mapRoot.eulerAngles.z : 0f;
    /// <summary>마지막 직선 구역 안에 배치된 도착 표시의 월드 좌표다.</summary>
    public Vector2 DestinationPosition => destination != null ? (Vector2)destination.position : Vector2.zero;

    /// <summary>기존 월드 도착 표시를 현재 미션 목적지 이름으로 바꾼다. 영구 씬/프리팹 에셋은 수정하지 않는다.</summary>
    public void SetDestinationLabel(string label)
    {
        if (destination != null && destination.TryGetComponent<TMPro.TMP_Text>(out var text))
        {
            text.text = label;
        }
    }

    /// <summary>마지막 도로의 진행 방향 오른쪽 끝 차선으로 픽업을 옮긴다. 도보 대기 위치와 도로 회전을 출력한다.</summary>
    public bool TryConfigurePickup(Vector2 parkingSize, out Vector2 sidewalkPosition, out float rotation, out string error)
    {
        sidewalkPosition = Vector2.zero;
        rotation = 0f;
        error = null;
        if (!isActiveAndEnabled || hasTransport || activeRoads.Count == 0 || destination == null || mapRoot == null)
        {
            error = "픽업 배치에는 생성된 픽업 도로와 목적지 참조가 필요합니다.";
            return false;
        }
        // 프리팹 회전과 최종 이동 방향을 사용한다. 화면의 임의 오른쪽이 아니라 주행 방향의 오른쪽이다.
        var road = activeRoads[activeRoads.Count - 1];
        if (road == null || !road.TryGetConnectionPosition(finalDirection, finalRotation, out var exit))
        {
            error = "마지막 도로의 출구 정보를 확인해주세요.";
            return false;
        }
        var forward = exit.normalized;
        var right = new Vector2(forward.y, -forward.x);
        var lanes = road.GetLaneCount(finalDirection, finalRotation);
        var laneWidth = road.ImageSize.x * LaneWidthRatio;
        var sidewalkWidth = road.ImageSize.x * SidewalkWidthRatio;
        var worldLaneWidth = mapRoot.TransformVector(right * laneWidth).magnitude;
        var worldRoadLength = mapRoot.TransformVector(exit * 2f).magnitude;
        if (parkingSize.x <= 0f || parkingSize.y <= 0f || parkingSize.x > worldLaneWidth
            || parkingSize.y > worldRoadLength * 0.5f)
        {
            error = "구급차와 픽업 여백이 마지막 차선/구역 크기보다 큽니다. 차량 크기 또는 Pickup Padding을 확인해주세요.";
            return false;
        }
        // 기존 도착 지점의 세로 위치를 유지하고 가장 바깥 차선 중앙과 인접 도보 중앙으로 각각 이동한다.
        var center = (Vector2)road.transform.localPosition + exit * 0.5f;
        var laneOffset = right * (laneWidth * (lanes * 0.5f - 0.5f));
        destination.position = mapRoot.TransformPoint(center + laneOffset);
        sidewalkPosition = mapRoot.TransformPoint(center + right * (laneWidth * lanes * 0.5f + sidewalkWidth * 0.5f));
        var worldForward = mapRoot.TransformVector(forward);
        rotation = Mathf.Atan2(-worldForward.x, worldForward.y) * Mathf.Rad2Deg;
        // 마지막 도로 안에서 바깥 차선으로 이동한 뒤 실제 픽업 사각형까지 이어지는 안내 경로다.
        if (navigationRoute.Count > 0)
        {
            navigationRoute.RemoveAt(navigationRoute.Count - 1);
            navigationRoute.Add(mapRoot.TransformPoint((Vector2)road.transform.localPosition + laneOffset));
            navigationRoute.Add(DestinationPosition);
        }
        return true;
    }

    /// <summary>에디터 설정 도구가 원본 참조를 주입한다. 런타임 에셋을 변경하지 않는다.</summary>
    public void Configure(RoadPrefabCatalog catalog, Transform mapRoot, Transform destination, GameObject roadPreview)
    {
        this.catalog = catalog;
        this.mapRoot = mapRoot;
        this.destination = destination;
        this.roadPreview = roadPreview;
    }

    /// <summary>월드 위치가 활성 교차로 구역 안에 있는지 반환한다. 이미지 전체 크기를 사용하며 회전과 스케일을 반영한다.</summary>
    public bool IsInsideIntersection(Vector3 worldPosition)
    {
        if (!isActiveAndEnabled)
        {
            return false;
        }
        foreach (var road in activeRoads)
        {
            if (road == null || !road.gameObject.activeInHierarchy || !IsIntersection(road.Index))
            {
                continue;
            }

            if (ContainsPoint(road, worldPosition))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>현재 위치의 활성 직선 도로가 향하는 월드 축을 반환한다. 축의 양방향 중 시점 선택은 카메라가 담당한다.</summary>
    public bool TryGetStraightRoadAxis(Vector3 worldPosition, out Vector3 axis)
    {
        axis = Vector3.zero;
        if (!isActiveAndEnabled)
        {
            return false;
        }
        foreach (var road in activeRoads)
        {
            if (road == null || !road.gameObject.activeInHierarchy
                || (road.Index != RoadIndex.Straight2Lane && road.Index != RoadIndex.Straight4Lane
                    && road.Index != RoadIndex.Straight6Lane))
            {
                continue;
            }
            if (ContainsPoint(road, worldPosition))
            {
                axis = road.transform.up;
                return true;
            }
        }
        return false;
    }

    /// <summary>프리팹 전체 크기를 로컬 좌표로 검사하여 회전된 구역의 AABB 바깥 모서리를 제외한다.</summary>
    private static bool ContainsPoint(RoadPrefab road, Vector3 worldPosition)
    {
        var localPosition = road.transform.InverseTransformPoint(worldPosition);
        var halfSize = road.ImageSize * 0.5f;
        return Mathf.Abs(localPosition.x) <= halfSize.x && Mathf.Abs(localPosition.y) <= halfSize.y;
    }

    /// <summary>Cross, 일반 T, 혼합 차선 T만 카메라 회전 구역으로 분류한다. 직선과 알 수 없는 식별자는 제외한다.</summary>
    private static bool IsIntersection(RoadIndex index)
    {
        switch (index)
        {
            case RoadIndex.Cross2Lane:
            case RoadIndex.Cross4Lane:
            case RoadIndex.Cross6Lane:
            case RoadIndex.T2Lane:
            case RoadIndex.T4Lane:
            case RoadIndex.T6Lane:
            case RoadIndex.TV2LaneH4Lane:
            case RoadIndex.TV4LaneH2Lane:
            case RoadIndex.TV4LaneH6Lane:
            case RoadIndex.TV6LaneH4Lane:
                return true;
            default:
                return false;
        }
    }

    /// <summary>실행에 필요한 참조와 정사각 구역 규격을 확인한다. 누락된 참조는 복구 방법을 반환한다.</summary>
    public bool ValidateConfiguration(out string error)
    {
        error = null;
        if (catalog == null || mapRoot == null || destination == null)
        {
            error = "MissionMapGenerator의 Catalog, Map Root, Destination을 연결해주세요.";
            return false;
        }
        // 현재 시드 경로의 격자 검사는 동일한 크기의 중심 pivot 정사각 프리팹을 사용한다.
        var size = Vector2.zero;
        for (var index = 1; index <= 13; index++)
        {
            if (!catalog.TryGetPrefab((RoadIndex)index, out var prefab))
            {
                error = $"도로 카탈로그에 {(RoadIndex)index} 프리팹이 없습니다.";
                return false;
            }
            var renderer = prefab.GetComponent<SpriteRenderer>();
            if (index == 1)
            {
                size = prefab.ImageSize;
            }
            if (renderer == null || renderer.sprite == null || size.x <= 0f
                || Mathf.Abs(size.x - size.y) > 0.001f || (prefab.ImageSize - size).sqrMagnitude > 0.000001f
                || (prefab.transform.localScale - Vector3.one).sqrMagnitude > 0.000001f
                || renderer.sprite.bounds.center.sqrMagnitude > 0.000001f
                || ((Vector2)renderer.sprite.bounds.size - size).sqrMagnitude > 0.000001f)
            {
                error = $"{prefab.name}: 동일한 정사각 Image Size, 중심 pivot, 스케일 1인 도로 프리팹이 필요합니다.";
                return false;
            }
        }
        return true;
    }

    /// <summary>연결과 겹침을 모두 확인한 후에만 이전 맵을 반환하고 새 맵을 배치한다. 실패 시 이전 맵을 유지한다.</summary>
    public bool TryGenerate(int seed, out string error)
    {
        return TryGenerateStage(seed, false, out error);
    }

    /// <summary>픽업 도로의 출구에 새 시드 경로를 연결한다. 차선 불일치와 기존 구역 겹침이면 맵을 보존한다.</summary>
    public bool TryAppendTransport(int seed, out string error)
    {
        return TryGenerateStage(seed, true, out error);
    }

    /// <summary>교차로의 미선택 출구 도로까지 겹치지 않는 픽업/후송 시드 쌍을 사전 검증한다.</summary>
    public bool TrySelectTransportSeed(int seed, out int transportSeed, out string error)
    {
        transportSeed = 0;
        if (!ValidateConfiguration(out error)
            || !MissionSeedRules.TryBuildRoute(seed, out var pickup, out error)
            || !TryPlanRoads(pickup, Vector2.zero, new List<RoadPlacement>(), out var pickupRoads,
                out var pickupBranches, out error))
        {
            return false;
        }
        var occupied = new List<RoadPlacement>(pickupRoads);
        occupied.AddRange(pickupBranches);
        var last = pickupRoads[pickupRoads.Count - 1];
        var random = new System.Random(seed ^ 0x5F3759DF);
        for (var attempt = 0; attempt < 1024; attempt++)
        {
            var candidate = MissionSeedRules.CreateSeed(random);
            if (!MissionSeedRules.TryBuildTransportRoute(seed, candidate, out var transport, out error))
            {
                continue;
            }
            if (!catalog.TryGetPrefab(transport[0].Index, out var first)
                || !TryFindRotation(first, transport[0], out var rotation))
            {
                error = "후송 첫 도로의 연결 정보를 확인해주세요.";
                return false;
            }
            last.Prefab.TryGetConnectionPosition(pickup[pickup.Length - 1].Outgoing, last.Rotation, out var exit);
            first.TryGetConnectionPosition(Opposite(transport[0].Incoming), rotation, out var entry);
            if (TryPlanRoads(transport, last.Position + exit - entry, occupied, out _, out _, out error))
            {
                transportSeed = candidate;
                return true;
            }
        }
        error = "교차로의 추가 도로까지 겹치지 않는 후송 경로를 찾지 못했습니다. 다른 픽업 시드를 사용해주세요.";
        return false;
    }

    /// <summary>전체 연결 검증 뒤에만 풀과 목적지를 변경한다. 후송은 기존 도로와 구급차 위치를 유지한다.</summary>
    private bool TryGenerateStage(int seed, bool append, out string error)
    {
        if (!isActiveAndEnabled)
        {
            error = "MissionMapGenerator가 비활성화되어 있습니다.";
            return false;
        }
        if (!ValidateConfiguration(out error))
        {
            return false;
        }
        if (append && (hasTransport || activeRoads.Count == 0 || activeRoads[activeRoads.Count - 1] == null))
        {
            error = "후송 도로는 생성된 픽업 맵에 한 번만 연결할 수 있습니다.";
            return false;
        }
        // 후송 계획은 픽업 출구에 맞춰 회전된 방향과 절대 격자 좌표를 이미 포함한다.
        MissionRoadPiece[] route;
        var isValid = append
            ? MissionSeedRules.TryBuildTransportRoute(pickupSeed, seed, out route, out error)
            : MissionSeedRules.TryBuildRoute(seed, out route, out error);
        if (!isValid)
        {
            return false;
        }

        // 기존 도로와 추가 분기도 생성 전 겹침 검사에 포함한다.
        var occupied = new List<RoadPlacement>();
        var origin = Vector2.zero;
        if (append)
        {
            foreach (var road in activeRoads)
            {
                if (road != null)
                {
                    occupied.Add(new RoadPlacement(road, 0, road.transform.localPosition));
                }
            }
            if (!catalog.TryGetPrefab(route[0].Index, out var first)
                || !TryFindRotation(first, route[0], out var rotation))
            {
                error = "후송 첫 도로의 연결 정보를 확인해주세요.";
                return false;
            }
            var previous = activeRoads[activeRoads.Count - 1];
            previous.TryGetConnectionPosition(finalDirection, finalRotation, out var exit);
            first.TryGetConnectionPosition(Opposite(finalDirection), rotation, out var entry);
            origin = (Vector2)previous.transform.localPosition + exit - entry;
        }
        if (!TryPlanRoads(route, origin, occupied, out var roads, out var branches, out error))
        {
            return false;
        }

        EnsurePoolRoot();
        // 후송 경로는 픽업 위치에서 기존 마지막 구역의 출구를 거쳐 새 구역들로 이어진다.
        var pickupPosition = DestinationPosition;
        navigationRoute.Clear();
        if (!append)
        {
            ReturnActiveRoads();
            StartPosition = mapRoot.TransformPoint(new Vector3(0f, -roads[0].Prefab.ImageSize.y * 0.25f, 0f));
            navigationRoute.Add(StartPosition);
        }
        else
        {
            navigationRoute.Add(pickupPosition);
            var previous = activeRoads[activeRoads.Count - 1];
            previous.TryGetConnectionPosition(finalDirection, finalRotation, out var exit);
            navigationRoute.Add(mapRoot.TransformPoint((Vector2)previous.transform.localPosition + exit));
        }
        // 분기를 먼저 보관하여 목록의 마지막 도로는 계속 미션 경로의 끝을 가리킨다.
        foreach (var branch in branches)
        {
            PlaceRoad(branch);
        }
        foreach (var road in roads)
        {
            var instance = PlaceRoad(road);
            navigationRoute.Add(instance.transform.position);
        }

        // 시작과 도착은 구역 안쪽에 둔다. 끝 가장자리 바깥으로 이동할 필요가 없다.
        CurrentSeed = seed;
        if (!append)
        {
            pickupSeed = seed;
        }
        hasTransport = append;
        var last = route.Length - 1;
        finalDirection = route[last].Outgoing;
        finalRotation = roads[last].Rotation;
        roads[last].Prefab.TryGetConnectionPosition(finalDirection, finalRotation, out var finalExit);
        destination.position = mapRoot.TransformPoint(roads[last].Position + finalExit * 0.5f);
        navigationRoute.Add(DestinationPosition);
        destination.gameObject.SetActive(true);
        if (roadPreview != null)
        {
            roadPreview.SetActive(false);
        }
        return true;
    }

    /// <summary>미션 경로를 그대로 배치하고 나머지 열린 교차로 출구에 직선 두 개씩 연결한다.</summary>
    private bool TryPlanRoads(MissionRoadPiece[] route, Vector2 origin, List<RoadPlacement> existing,
        out List<RoadPlacement> roads, out List<RoadPlacement> branches, out string error)
    {
        roads = new List<RoadPlacement>(route.Length);
        branches = new List<RoadPlacement>(12);
        var occupied = new List<RoadPlacement>(existing);
        error = null;
        for (var index = 0; index < route.Length; index++)
        {
            var piece = route[index];
            if (!catalog.TryGetPrefab(piece.Index, out var prefab)
                || !TryFindRotation(prefab, piece, out var rotation))
            {
                error = $"{piece.Index}의 진입 {piece.EntryLanes}차선 / 출구 {piece.ExitLanes}차선 연결 정보가 맞지 않습니다.";
                return false;
            }
            var position = origin;
            if (index > 0)
            {
                var previous = roads[index - 1];
                previous.Prefab.TryGetConnectionPosition(route[index - 1].Outgoing, previous.Rotation, out var exit);
                prefab.TryGetConnectionPosition(Opposite(piece.Incoming), rotation, out var entry);
                position = previous.Position + exit - entry;
            }
            var placement = new RoadPlacement(prefab, rotation, position);
            if (!TryReserveRoad(placement, occupied))
            {
                error = "미션 경로가 기존 도로 또는 교차로의 추가 도로와 겹칩니다.";
                return false;
            }
            roads.Add(placement);
        }
        for (var index = 0; index < route.Length; index++)
        {
            var piece = route[index];
            if (!IsIntersection(piece.Index))
            {
                continue;
            }
            var junction = roads[index];
            for (var directionIndex = 0; directionIndex < 4; directionIndex++)
            {
                var direction = (RoadDirection)directionIndex;
                if (direction == Opposite(piece.Incoming) || direction == piece.Outgoing
                    || junction.Prefab.GetLaneCount(direction, junction.Rotation) == 0)
                {
                    continue;
                }
                if (!junction.Prefab.TryGetStraightRoad(direction, junction.Rotation, out var straightIndex, out var rotation)
                    || !catalog.TryGetPrefab(straightIndex, out var prefab)
                    || prefab.GetLaneCount(Opposite(direction), rotation) != junction.Prefab.GetLaneCount(direction, junction.Rotation)
                    || prefab.GetLaneCount(direction, rotation) != junction.Prefab.GetLaneCount(direction, junction.Rotation))
                {
                    error = $"{piece.Index}의 {direction} 출구에 맞는 직선 도로 연결 정보를 확인해주세요.";
                    return false;
                }
                junction.Prefab.TryGetConnectionPosition(direction, junction.Rotation, out var exit);
                prefab.TryGetConnectionPosition(Opposite(direction), rotation, out var entry);
                var position = junction.Position + exit - entry;
                prefab.TryGetConnectionPosition(direction, rotation, out var straightExit);
                for (var count = 0; count < BranchRoadCount; count++)
                {
                    var placement = new RoadPlacement(prefab, rotation, position);
                    if (!TryReserveRoad(placement, occupied))
                    {
                        error = "교차로의 추가 도로가 미션 경로 또는 다른 추가 도로와 겹칩니다.";
                        return false;
                    }
                    branches.Add(placement);
                    position += straightExit - entry;
                }
            }
        }
        return true;
    }

    /// <summary>검증된 동일 크기 정사각 구역의 면적 겹침을 확인하고 배치를 예약한다.</summary>
    private static bool TryReserveRoad(RoadPlacement placement, List<RoadPlacement> occupied)
    {
        foreach (var other in occupied)
        {
            var offset = placement.Position - other.Position;
            var size = (placement.Prefab.ImageSize + other.Prefab.ImageSize) * 0.5f;
            if (Mathf.Abs(offset.x) < size.x - 0.001f && Mathf.Abs(offset.y) < size.y - 0.001f)
            {
                return false;
            }
        }
        occupied.Add(placement);
        return true;
    }

    /// <summary>미션 경로와 추가 도로 모두 같은 풀과 활성 목록으로 소유한다.</summary>
    private RoadPrefab PlaceRoad(RoadPlacement placement)
    {
        var instance = Acquire(placement.Prefab);
        instance.transform.SetParent(mapRoot, false);
        instance.transform.localPosition = placement.Position;
        instance.transform.localRotation = Quaternion.Euler(0f, 0f, -90f * placement.Rotation);
        instance.transform.localScale = Vector3.one;
        activeRoads.Add(instance);
        instance.gameObject.SetActive(true);
        return instance;
    }

    /// <summary>인스턴스를 생성하기 전에 검증하는 원본 프리팹과 맵 로컬 배치 정보다.</summary>
    private readonly struct RoadPlacement
    {
        public readonly RoadPrefab Prefab;
        public readonly int Rotation;
        public readonly Vector2 Position;

        public RoadPlacement(RoadPrefab prefab, int rotation, Vector2 position)
        {
            Prefab = prefab;
            Rotation = rotation;
            Position = position;
        }
    }

    /// <summary>차선 수와 입구/출구 방향이 일치하는 첫 회전을 선택한다. 원본 카탈로그 순서와 풀 상태에 의존하지 않는다.</summary>
    private static bool TryFindRotation(RoadPrefab prefab, MissionRoadPiece piece, out int rotation)
    {
        for (rotation = 0; rotation < 4; rotation++)
        {
            if (prefab.GetLaneCount(Opposite(piece.Incoming), rotation) == piece.EntryLanes
                && prefab.GetLaneCount(piece.Outgoing, rotation) == piece.ExitLanes)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>입구 가장자리를 구하기 위한 반대 방향을 반환한다.</summary>
    private static RoadDirection Opposite(RoadDirection direction)
    {
        return (RoadDirection)(((int)direction + 2) % 4);
    }

    /// <summary>생성 인스턴스가 초기 배치 전에 활성화되지 않도록 비활성 부모를 만든다. 씬 종료 시 함께 파괴된다.</summary>
    private void EnsurePoolRoot()
    {
        if (poolRoot != null)
        {
            return;
        }
        var root = new GameObject("MissionRoadPool");
        root.transform.SetParent(transform, false);
        root.SetActive(false);
        poolRoot = root.transform;
    }

    /// <summary>현재 구역을 숨겨 종류별 풀에 반환한다. 원본 프리팹과 씬 미리보기는 반환하지 않는다.</summary>
    private void ReturnActiveRoads()
    {
        foreach (var road in activeRoads)
        {
            if (road == null)
            {
                continue;
            }
            road.gameObject.SetActive(false);
            road.transform.SetParent(poolRoot, false);
            if (!pool.TryGetValue(road.Index, out var available))
            {
                available = new Stack<RoadPrefab>();
                pool.Add(road.Index, available);
            }
            available.Push(road);
        }
        activeRoads.Clear();
    }

    /// <summary>살아 있는 반환 구역을 우선 사용하며 부족할 때만 전체 구역 프리팹을 복제한다.</summary>
    private RoadPrefab Acquire(RoadPrefab prefab)
    {
        if (pool.TryGetValue(prefab.Index, out var available))
        {
            while (available.Count > 0)
            {
                var road = available.Pop();
                if (road != null)
                {
                    return road;
                }
            }
        }
        var created = Instantiate(prefab, poolRoot);
        created.gameObject.SetActive(false);
        return created;
    }
}
