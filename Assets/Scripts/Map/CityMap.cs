using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>도시 도로의 생성 수명과 Prefab 배치 및 차선 연결 검증을 소유한다</summary>
[DisallowMultipleComponent]
public sealed class CityMap : MonoBehaviour
{
    [SerializeField] private RoadChunk straight;
    [SerializeField] private RoadChunk corner;
    [SerializeField] private RoadChunk tJunction;
    [SerializeField] private RoadChunk intersection;
    [SerializeField, Range(5, 24)] private int width = 9;
    [SerializeField, Range(5, 24)] private int height = 9;
    [SerializeField, Range(0, 100)] private int density = 35;
    [SerializeField, Min(4f)] private float cellSize = 20f;
    [SerializeField] private bool randomSeedOnStart = true;
    [SerializeField] private int initialSeed = 12345;

    private GameObject cityRoot;
    private RoadChunk[] roads;
    private RoadConnection[,] ports;
    private TrafficLane[] lanes;
    private bool ready;
    private Coroutine reconnectRoutine;
    public CityLayout Layout { get; private set; }
    public float CellSize { get; private set; }
    public bool IsReady => ready && isActiveAndEnabled && cityRoot;
    public int Seed => Layout == null ? initialSeed : Layout.Seed;
    public int LaneCount => lanes == null ? 0 : lanes.Length;
    public event Action<CityMap> Generated;

    /// <summary>한 연결 마스크에 대응하는 Prefab과 회전 및 포트 인덱스를 저장한다</summary>
    private sealed class Variant
    {
        internal RoadChunk prefab;
        internal int rotation;
        internal int[] portIndices;
    }

    // 게임 시작 시 한 번만 도시를 준비한다
    private void Start()
    {
        if (!EnsureGenerated(out string error))
            Debug.LogError(error, this);
    }

    // 도시를 다시 활성화할 때 기존 인스턴스의 연결만 복구한다
    private void OnEnable()
    {
        if (!cityRoot)
            return;
        reconnectRoutine = StartCoroutine(Reconnect());
    }

    // 자식들의 활성화 콜백이 끝난 다음 기존 그래프를 복구한다
    private IEnumerator Reconnect()
    {
        yield return null;
        ready = Connect(Layout, ports, out string error) && Validate(out error);
        reconnectRoutine = null;
        if (!ready)
            Debug.LogError(error, this);
    }

    // 비활성 상태에서는 소비자가 도시를 사용하지 못하도록 표시한다
    private void OnDisable()
    {
        if (reconnectRoutine != null)
        {
            StopCoroutine(reconnectRoutine);
            reconnectRoutine = null;
        }
        ready = false;
    }

    // 생성기 컴포넌트만 제거되어도 소유한 도시를 정리한다
    private void OnDestroy()
    {
        Release(cityRoot);
        cityRoot = null;
    }

    // 이미 생성된 도시는 재사용하고 최초 호출에서만 Seed를 선택한다
    public bool EnsureGenerated(out string error)
    {
        error = null;
        if (cityRoot)
        {
            if (!IsReady)
                error = "기존 도시가 활성화되고 연결된 상태가 아닙니다.";
            return IsReady;
        }
        return Build(randomSeedOnStart ? CreateSeed() : initialSeed, out error);
    }

    // 향후 Game Over 처리자가 새로운 Seed로 도시 교체를 요청한다
    public bool TryStartNewCity(int seed, out string error)
    {
        if (cityRoot && seed == Seed)
        {
            error = "새 도시에는 기존 도시와 다른 Seed가 필요합니다.";
            return false;
        }
        return Build(seed, out error);
    }

    // 전역 Unity Random 상태를 변경하지 않고 새 Seed를 발급한다
    public int CreateSeed()
    {
        int seed = Guid.NewGuid().GetHashCode();
        return seed == Seed ? unchecked(seed + 1) : seed;
    }

    // 완성된 도시의 도로를 격자 좌표로 직접 조회한다
    public RoadChunk GetRoad(int x, int y)
    {
        return Layout != null && x >= 0 && y >= 0 && x < Layout.Width && y < Layout.Height ? roads[y * Layout.Width + x] : null;
    }

    // 네비게이션 등이 사용할 차선을 생성 순서로 조회한다
    public TrafficLane GetLane(int index)
    {
        return index >= 0 && index < LaneCount ? lanes[index] : null;
    }

    // 임시 도시를 완전히 검증한 뒤 기존 도시와 교체한다
    private bool Build(int seed, out string error)
    {
        error = null;
        if (!isActiveAndEnabled || transform.lossyScale != Vector3.one || Vector3.Dot(transform.forward, Vector3.forward) < 0.9999f)
        {
            error = "CityMap은 활성 상태, 월드 스케일 1, XY 평면이어야 합니다.";
            return false;
        }
        GameObject candidate = null;
        try
        {
            CityLayout layout = new CityLayout(seed, width, height, density);
            Variant[] catalog = BuildCatalog();
            candidate = new GameObject("City_" + seed);
            candidate.transform.SetParent(transform, false);
            RoadChunk[] built = new RoadChunk[width * height];
            RoadConnection[,] builtPorts = new RoadConnection[width * height, 4];
            List<TrafficLane> builtLanes = new List<TrafficLane>();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int mask = layout.GetMask(x, y);
                    if (mask == 0)
                        continue;
                    Variant variant = catalog[mask];
                    if (variant == null)
                        throw new InvalidOperationException("연결 마스크에 맞는 도로 Prefab이 없습니다: " + mask);
                    RoadChunk road = Instantiate(variant.prefab, candidate.transform, false);
                    road.name = x + "_" + y + "_" + variant.prefab.name;
                    road.transform.localPosition = new Vector3(x * cellSize, y * cellSize, 0f);
                    road.transform.localRotation = Quaternion.Euler(0f, 0f, -90f * variant.rotation);
                    int index = y * width + x;
                    built[index] = road;
                    for (int d = 0; d < 4; d++)
                    {
                        if (variant.portIndices[d] >= 0)
                            builtPorts[index, d] = road.GetConnection(variant.portIndices[d]);
                    }
                    for (int i = 0; i < road.LaneCount; i++)
                        builtLanes.Add(road.GetLane(i));
                }
            }
            TrafficLane[] laneArray = builtLanes.ToArray();
            if (!Connect(layout, builtPorts, out error) || !ValidateCity(layout, built, builtPorts, laneArray, out error))
                throw new InvalidOperationException(error);

            GameObject previous = cityRoot;
            cityRoot = candidate;
            roads = built;
            ports = builtPorts;
            lanes = laneArray;
            Layout = layout;
            CellSize = cellSize;
            ready = true;
            candidate = null;
            Release(previous);
        }
        catch (Exception exception)
        {
            Release(candidate);
            error = "도시 생성 실패: " + exception.Message;
            return false;
        }
        Generated?.Invoke(this);
        return true;
    }

    // Prefab 포트의 규격을 검사하고 16개 방향 마스크 조회표를 만든다
    private Variant[] BuildCatalog()
    {
        if (float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize < 4f)
            throw new InvalidOperationException("Cell Size가 올바르지 않습니다.");
        Variant[] catalog = new Variant[16];
        RoadChunk[] prefabs = { straight, corner, tJunction, intersection };
        int[] expected = { 5, 6, 7, 15 };
        for (int p = 0; p < prefabs.Length; p++)
        {
            RoadChunk prefab = prefabs[p];
            if (!prefab || !prefab.gameObject.activeSelf || prefab.transform.localScale != Vector3.one)
                throw new InvalidOperationException("도로 Prefab 누락 또는 활성 상태/스케일 오류입니다: " + p);
            if (!prefab.Validate(out string error))
                throw new InvalidOperationException(prefab.name + ": " + error);
            int[] indices = { -1, -1, -1, -1 };
            int mask = 0;
            for (int i = 0; i < prefab.ConnectionCount; i++)
            {
                RoadConnection port = prefab.GetConnection(i);
                Vector3 point = prefab.transform.InverseTransformPoint(port.Position);
                Vector3 direction = prefab.transform.InverseTransformDirection(port.Outward);
                int matched = -1;
                for (int d = 0; d < 4; d++)
                {
                    Vector3 axis = (Vector2)CityLayout.Direction(d);
                    if (Vector3.Distance(point, axis * cellSize * 0.5f) < RoadConnection.PositionTolerance && Vector3.Dot(direction, axis) > 0.999f)
                        matched = d;
                }
                if (matched < 0 || indices[matched] >= 0 || port.HasTarget || !port.enabled || !port.gameObject.activeSelf)
                    throw new InvalidOperationException(prefab.name + ": 포트는 셀 변 중앙에 하나씩 있어야 하며 Target은 비워야 합니다.");
                indices[matched] = i;
                mask |= 1 << matched;
            }
            bool correctShape = false;
            for (int r = 0; r < 4; r++)
            {
                int rotated = 0;
                int[] rotatedIndices = { -1, -1, -1, -1 };
                for (int d = 0; d < 4; d++)
                {
                    if ((mask & (1 << d)) == 0)
                        continue;
                    int rotatedDirection = (d + r) % 4;
                    rotated |= 1 << rotatedDirection;
                    rotatedIndices[rotatedDirection] = indices[d];
                }
                if (rotated == expected[p])
                    correctShape = true;
                catalog[rotated] = new Variant { prefab = prefab, rotation = r, portIndices = rotatedIndices };
            }
            if (!correctShape)
                throw new InvalidOperationException(prefab.name + ": Inspector의 도로 종류와 포트 방향이 일치하지 않습니다.");
        }
        return catalog;
    }

    // 북쪽과 동쪽 이웃만 처리하여 각 도로 접속을 한 번씩 연결한다
    private static bool Connect(CityLayout layout, RoadConnection[,] points, out string error)
    {
        error = null;
        for (int y = 0; y < layout.Height; y++)
        {
            for (int x = 0; x < layout.Width; x++)
            {
                for (int d = 0; d < 2; d++)
                {
                    RoadConnection port = points[y * layout.Width + x, d];
                    if (!port)
                        continue;
                    Vector2Int delta = CityLayout.Direction(d);
                    int nx = x + delta.x;
                    int ny = y + delta.y;
                    if (nx >= layout.Width || ny >= layout.Height)
                    {
                        error = "도시 경계 밖으로 열린 도로입니다.";
                        return false;
                    }
                    if (!port.TryConnect(points[ny * layout.Width + nx, d + 2], out error))
                        return false;
                }
            }
        }
        return true;
    }

    // 현재 도시의 경계와 방향 그래프가 여전히 완결되었는지 검사한다
    public bool Validate(out string error)
    {
        error = "생성된 도시가 없습니다.";
        ready = isActiveAndEnabled && cityRoot && ValidateCity(Layout, roads, ports, lanes, out error);
        return ready;
    }

    // 모든 도로 접속과 차선의 정방향 및 역방향 도달 가능성을 검사한다
    private static bool ValidateCity(CityLayout layout, RoadChunk[] chunks, RoadConnection[,] points, TrafficLane[] paths, out string error)
    {
        error = null;
        for (int y = 0; y < layout.Height; y++)
        {
            for (int x = 0; x < layout.Width; x++)
            {
                int index = y * layout.Width + x;
                if (layout.GetMask(x, y) == 0)
                    continue;
                if (!chunks[index] || !chunks[index].Validate(out error))
                {
                    error = error ?? "도로 인스턴스가 누락되었습니다.";
                    return false;
                }
                for (int d = 0; d < 4; d++)
                {
                    if ((layout.GetMask(x, y) & (1 << d)) == 0)
                        continue;
                    Vector2Int delta = CityLayout.Direction(d);
                    int nx = x + delta.x;
                    int ny = y + delta.y;
                    RoadConnection port = points[index, d];
                    if (!port || !port.isActiveAndEnabled || nx < 0 || ny < 0 || nx >= layout.Width || ny >= layout.Height || !port.ConnectedTo || port.ConnectedTo != points[ny * layout.Width + nx, (d + 2) % 4] || port.ConnectedTo.ConnectedTo != port)
                    {
                        error = "미연결 또는 잘못된 이웃 도로가 있습니다: " + x + ", " + y;
                        return false;
                    }
                }
            }
        }
        Dictionary<TrafficLane, int> indices = new Dictionary<TrafficLane, int>(paths.Length);
        List<int>[] forward = new List<int>[paths.Length];
        List<int>[] backward = new List<int>[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            if (!paths[i] || !paths[i].isActiveAndEnabled || indices.ContainsKey(paths[i]))
            {
                error = "누락, 비활성 또는 중복 차선입니다.";
                return false;
            }
            indices.Add(paths[i], i);
            forward[i] = new List<int>();
            backward[i] = new List<int>();
        }
        for (int i = 0; i < paths.Length; i++)
        {
            TrafficLane lane = paths[i];
            if (lane.NextCount == 0)
            {
                error = "진행할 수 없는 차선입니다: " + lane.name;
                return false;
            }
            for (int n = 0; n < lane.NextCount; n++)
            {
                TrafficLane next = lane.GetNext(n);
                if (!next || !indices.TryGetValue(next, out int nextIndex) || Vector3.Distance(lane.EndPoint, next.StartPoint) > RoadConnection.PositionTolerance || Vector3.Dot(lane.EndDirection, next.StartDirection) < 0.98f)
                {
                    error = "차선 그래프가 도시 밖으로 나가거나 기하적으로 끊어졌습니다.";
                    return false;
                }
                forward[i].Add(nextIndex);
                backward[nextIndex].Add(i);
            }
        }
        if (!ReachAll(forward) || !ReachAll(backward))
            error = "차선 그래프에 접근할 수 없거나 탈출할 수 없는 영역이 있습니다.";
        return error == null;
    }

    // 한 번의 BFS로 시작점에서 모든 정점에 도달하는지 검사한다
    private static bool ReachAll(List<int>[] edges)
    {
        if (edges.Length == 0)
            return false;
        bool[] visited = new bool[edges.Length];
        Queue<int> queue = new Queue<int>();
        visited[0] = true;
        queue.Enqueue(0);
        int count = 0;
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            count++;
            foreach (int next in edges[current])
            {
                if (visited[next])
                    continue;
                visited[next] = true;
                queue.Enqueue(next);
            }
        }
        return count == edges.Length;
    }

    // 소유한 도시를 즉시 비활성화하고 실행 환경에 맞게 해제한다
    private static void Release(GameObject root)
    {
        if (!root)
            return;
        root.SetActive(false);
        if (Application.isPlaying)
            Destroy(root);
        else
            DestroyImmediate(root);
    }
}
