using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Scene에 저장된 고정 도로와 미션 후보를 조회하고 연결 상태를 검증한다</summary>
[DisallowMultipleComponent]
public sealed class CityMap : MonoBehaviour
{
    [SerializeField] private RoadChunk[] fixedRoads = Array.Empty<RoadChunk>();
    [SerializeField] private TrafficLane[] fixedLanes = Array.Empty<TrafficLane>();
    [SerializeField] private SidewalkPath[] fixedSidewalks = Array.Empty<SidewalkPath>();
    [SerializeField] private EnvironmentSlot[] patientPoints = Array.Empty<EnvironmentSlot>();
    [SerializeField] private CameraTurnZone[] fixedTurnZones = Array.Empty<CameraTurnZone>();
    [SerializeField, Min(1)] private int width = 9;
    [SerializeField, Min(1)] private int height = 9;
    [SerializeField, Min(4f)] private float cellSize = 20f;
    [SerializeField] private bool useRoadGrid = true;
    private readonly Dictionary<Vector2Int, List<TrafficLane>> laneCells = new Dictionary<Vector2Int, List<TrafficLane>>();
    private readonly HashSet<TrafficLane> laneSet = new HashSet<TrafficLane>();
    private readonly HashSet<SidewalkPath> sidewalkSet = new HashSet<SidewalkPath>();
    private bool ready;
    public bool IsReady => ready && isActiveAndEnabled;
    public float CellSize => cellSize;
    public int Width => width;
    public int Height => height;
    public int LaneCount => fixedLanes.Length;
    public int SidewalkPathCount => fixedSidewalks.Length;
    public int IncidentSlotCount => patientPoints.Length;
    public bool UsesRoadGrid => useRoadGrid;
    public event Action StateChanged;

    // 저장된 참조만 준비하며 도로와 환경 객체를 생성하지 않는다
    private void OnEnable()
    {
        Initialize(out _);
    }

    // 소비자에게 고정 맵을 일시적으로 사용할 수 없음을 알린다
    private void OnDisable()
    {
        ready = false;
        StateChanged?.Invoke();
    }

    // 시작 순서에 의존하지 않도록 명시적으로 캐시를 준비한다
    public bool Initialize(out string error)
    {
        error = null;
        if (IsReady)
            return true;
        if (!isActiveAndEnabled || fixedRoads.Length == 0 || (useRoadGrid && fixedRoads.Length != width * height) || fixedLanes.Length == 0 || !float.IsFinite(cellSize) || cellSize < 4f)
        {
            error = "고정 맵의 도로와 차선 및 격자 참조를 확인하세요.";
            return false;
        }
        laneSet.Clear();
        sidewalkSet.Clear();
        laneCells.Clear();
        foreach (TrafficLane lane in fixedLanes)
        {
            if (!lane || !laneSet.Add(lane))
            {
                error = "고정 맵의 차선이 누락되었거나 중복되었습니다.";
                return false;
            }
            if (!useRoadGrid)
                CacheLane(lane);
        }
        foreach (SidewalkPath path in fixedSidewalks)
            if (path)
                sidewalkSet.Add(path);
        ready = true;
        StateChanged?.Invoke();
        return true;
    }

    // 고정 격자의 도로를 상수 시간에 조회한다
    public RoadChunk GetRoad(int x, int y)
    {
        return x >= 0 && y >= 0 && x < width && y < height && y * width + x < fixedRoads.Length ? fixedRoads[y * width + x] : null;
    }

    // 저장된 차선을 인덱스로 조회한다
    public TrafficLane GetLane(int index)
    {
        return index >= 0 && index < fixedLanes.Length ? fixedLanes[index] : null;
    }

    // 저장된 보도 경로를 인덱스로 조회한다
    public SidewalkPath GetSidewalkPath(int index)
    {
        return index >= 0 && index < fixedSidewalks.Length ? fixedSidewalks[index] : null;
    }

    // 현재 활성화된 고정 보도인지 확인한다
    public bool ContainsSidewalkPath(SidewalkPath path)
    {
        return IsReady && path && path.isActiveAndEnabled && sidewalkSet.Contains(path);
    }

    // 지정된 환자 후보를 인덱스로 조회한다
    public EnvironmentSlot GetIncidentSlot(int index)
    {
        return index >= 0 && index < patientPoints.Length ? patientPoints[index] : null;
    }

    // Scene에 미리 배치한 회전 영역에 카메라를 연결한다
    public void BindCamera(AmbulanceCamera camera)
    {
        foreach (CameraTurnZone zone in fixedTurnZones)
            if (zone)
                zone.Bind(camera);
    }

    // 현재 활성화된 고정 차선인지 상수 시간에 확인한다
    public bool ContainsLane(TrafficLane lane)
    {
        return IsReady && lane && lane.isActiveAndEnabled && laneSet.Contains(lane);
    }

    // 현재 셀과 인접 셀의 실제 차선만 검사하여 가까운 도로 위치를 찾는다
    public bool TryLocate(Vector3 position, float maxOffset, out TrafficLane lane, out float distance)
    {
        lane = null;
        distance = 0f;
        if (!IsReady || !float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z) || !float.IsFinite(maxOffset) || maxOffset < 0f || maxOffset > CellSize * 0.5f)
            return false;
        Vector3 local = transform.InverseTransformPoint(position);
        if (!useRoadGrid)
            return TryLocateInCells(local, position, maxOffset, out lane, out distance);
        if (local.x < -CellSize || local.y < -CellSize || local.x > width * CellSize || local.y > height * CellSize)
            return false;
        int cx = Mathf.RoundToInt(local.x / CellSize);
        int cy = Mathf.RoundToInt(local.y / CellSize);
        float best = maxOffset * maxOffset;
        for (int y = cy - 1; y <= cy + 1; y++)
        {
            for (int x = cx - 1; x <= cx + 1; x++)
            {
                RoadChunk road = GetRoad(x, y);
                if (!road || !road.isActiveAndEnabled)
                    continue;
                for (int i = 0; i < road.LaneCount; i++)
                {
                    TrafficLane candidate = road.GetLane(i);
                    if (!ContainsLane(candidate) || !candidate.TryProject(position, out float along, out float offset) || offset > best || (lane && offset == best))
                        continue;
                    lane = candidate;
                    distance = along;
                    best = offset;
                }
            }
        }
        return lane;
    }

    // 비격자 Scene도 초기화 시 차선의 공간 목록을 만들어 같은 위치 조회 API를 사용한다.
    private void CacheLane(TrafficLane lane)
    {
        Bounds bounds = new Bounds(transform.InverseTransformPoint(lane.StartPoint), Vector3.zero);
        for (int i = 1; i < lane.PointCount; i++)
            bounds.Encapsulate(transform.InverseTransformPoint(lane.GetWorldPoint(i)));
        Vector2Int min = Cell(bounds.min);
        Vector2Int max = Cell(bounds.max);
        for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                Vector2Int key = new Vector2Int(x, y);
                if (!laneCells.TryGetValue(key, out List<TrafficLane> list))
                    laneCells.Add(key, list = new List<TrafficLane>());
                list.Add(lane);
            }
    }

    private Vector2Int Cell(Vector3 position)
    {
        return new Vector2Int(Mathf.FloorToInt(position.x / cellSize), Mathf.FloorToInt(position.y / cellSize));
    }

    // 저장된 주변 셀만 검사하며 Scene 검색이나 매번 목록 생성을 하지 않는다.
    private bool TryLocateInCells(Vector3 local, Vector3 position, float maxOffset, out TrafficLane lane, out float distance)
    {
        lane = null;
        distance = 0f;
        Vector2Int center = Cell(local);
        float best = maxOffset * maxOffset;
        for (int y = center.y - 1; y <= center.y + 1; y++)
            for (int x = center.x - 1; x <= center.x + 1; x++)
            {
                if (!laneCells.TryGetValue(new Vector2Int(x, y), out List<TrafficLane> list))
                    continue;
                foreach (TrafficLane candidate in list)
                {
                    if (!ContainsLane(candidate) || !candidate.TryProject(position, out float along, out float offset) || offset > best || (lane && offset == best))
                        continue;
                    lane = candidate;
                    distance = along;
                    best = offset;
                }
            }
        return lane;
    }


    // 편집 및 명시적 검증 요청에서만 고정 도로 그래프를 검사한다
    public bool Validate(out string error)
    {
        if (!Initialize(out error))
            return false;
        foreach (RoadChunk road in fixedRoads)
            if (road && !road.Validate(out error))
                return false;
        return ValidateLanes(fixedLanes, out error);
    }

    // 저장된 후속 차선의 기하 연결과 양방향 도달 가능성을 검사한다
    private static bool ValidateLanes(TrafficLane[] paths, out string error)
    {
        error = null;
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


}
