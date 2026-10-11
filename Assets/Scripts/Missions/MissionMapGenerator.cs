using System.Collections.Generic;
using UnityEngine;

/// <summary>시드 계획을 기존 프리팹 연결 정보로 배치한다. 생성한 구역의 소유자이며 다음 미션에서 풀로 재사용한다.</summary>
[DisallowMultipleComponent]
public sealed class MissionMapGenerator : MonoBehaviour
{
    // 에디터 설정 메뉴로 연결하는 원본 카탈로그, 맵 부모, 마지막 구역의 도착 표시다.
    [SerializeField] private RoadPrefabCatalog catalog;
    [SerializeField] private Transform mapRoot;
    [SerializeField] private Transform destination;
    // 첫 수락 이후 숨길 기존 GamePlay 도로 미리보기다. 원본 씬 오브젝트를 삭제하지 않는다.
    [SerializeField] private GameObject roadPreview;

    // 현재 맵과 종류별 비활성 풀은 이 컴포넌트 인스턴스만 소유한다.
    private readonly List<RoadPrefab> activeRoads = new List<RoadPrefab>(39);
    private readonly Dictionary<RoadIndex, Stack<RoadPrefab>> pool = new Dictionary<RoadIndex, Stack<RoadPrefab>>();
    private Transform poolRoot;

    /// <summary>성공적으로 배치된 맵의 시드다. 생성 전에는 0이다.</summary>
    public int CurrentSeed { get; private set; }
    /// <summary>첫 구역 안의 시작 위치다. Rigidbody 순간이동에 사용하는 월드 좌표다.</summary>
    public Vector2 StartPosition { get; private set; }
    /// <summary>첫 구역의 북쪽에 맞춘 구급차 시작 회전이다. 단위는 월드 Z축 각도다.</summary>
    public float StartRotation => mapRoot != null ? mapRoot.eulerAngles.z : 0f;
    /// <summary>마지막 직선 구역 안에 배치된 도착 표시의 월드 좌표다.</summary>
    public Vector2 DestinationPosition => destination != null ? (Vector2)destination.position : Vector2.zero;

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
        if (!isActiveAndEnabled)
        {
            error = "MissionMapGenerator가 비활성화되어 있습니다.";
            return false;
        }
        if (!ValidateConfiguration(out error) || !MissionSeedRules.TryBuildRoute(seed, out var route, out error))
        {
            return false;
        }

        // 인스턴스 생성 전에 원본 메타데이터만으로 모든 회전과 연결 위치를 검증한다.
        var prefabs = new RoadPrefab[route.Length];
        var rotations = new int[route.Length];
        var positions = new Vector2[route.Length];
        for (var index = 0; index < route.Length; index++)
        {
            var piece = route[index];
            if (!catalog.TryGetPrefab(piece.Index, out prefabs[index])
                || !TryFindRotation(prefabs[index], piece, out rotations[index]))
            {
                error = $"{piece.Index}의 진입 {piece.EntryLanes}차선 / 출구 {piece.ExitLanes}차선 연결 정보가 맞지 않습니다.";
                return false;
            }
            if (index > 0)
            {
                // 이전 출구와 다음 입구의 중심을 맞춰 이미지 전체 구역을 빈틈 없이 연결한다.
                prefabs[index - 1].TryGetConnectionPosition(route[index - 1].Outgoing, rotations[index - 1], out var exit);
                prefabs[index].TryGetConnectionPosition(Opposite(piece.Incoming), rotations[index], out var entry);
                positions[index] = positions[index - 1] + exit - entry;
            }
        }

        EnsurePoolRoot();
        ReturnActiveRoads();
        for (var index = 0; index < route.Length; index++)
        {
            var instance = Acquire(prefabs[index]);
            instance.transform.SetParent(mapRoot, false);
            instance.transform.localPosition = positions[index];
            instance.transform.localRotation = Quaternion.Euler(0f, 0f, -90f * rotations[index]);
            instance.transform.localScale = Vector3.one;
            activeRoads.Add(instance);
            instance.gameObject.SetActive(true);
        }

        // 시작과 도착은 구역 안쪽에 둔다. 끝 가장자리 바깥으로 이동할 필요가 없다.
        CurrentSeed = seed;
        StartPosition = mapRoot.TransformPoint(new Vector3(0f, -prefabs[0].ImageSize.y * 0.25f, 0f));
        var last = route.Length - 1;
        prefabs[last].TryGetConnectionPosition(route[last].Outgoing, rotations[last], out var finalExit);
        destination.position = mapRoot.TransformPoint(positions[last] + finalExit * 0.5f);
        destination.gameObject.SetActive(true);
        if (roadPreview != null)
        {
            roadPreview.SetActive(false);
        }
        return true;
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
