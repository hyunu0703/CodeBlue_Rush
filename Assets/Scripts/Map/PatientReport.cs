using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>현재 도시의 환자 후보를 캐싱하고 접근 가능한 현장 보고와 목적지를 소유한다</summary>
[DisallowMultipleComponent]
public sealed class PatientReport : MonoBehaviour
{
    public enum ReportStatus { Idle, Active, NavigationUnavailable, MapUnavailable, NoCandidates, NoReachablePoint, InvalidStart, Cancelled }

    /// <summary>생성된 실제 차선 위의 환자 생성 위치와 접근 목적지를 표현한다</summary>
    public readonly struct SpawnPoint
    {
        public CityMap Map { get; }
        public TrafficLane Lane { get; }
        public float Distance { get; }

        // 실제 도시와 차선상의 거리로 생성 위치를 고정한다
        internal SpawnPoint(CityMap map, TrafficLane lane, float distance)
        {
            Map = map;
            Lane = lane;
            Distance = distance;
        }

        // 파괴되거나 다른 도시로 교체된 위치를 제외하고 현재 월드 위치와 방향을 반환한다
        public bool TryGetPose(out Vector3 position, out Vector3 direction)
        {
            position = Vector3.zero;
            direction = Vector3.zero;
            return Map && Map.ContainsLane(Lane) && float.IsFinite(Distance) && Distance >= 0f && Distance <= Lane.Length && Lane.TrySample(Distance, out position, out direction);
        }
    }

    [SerializeField] private NavigationRoute navigation;

    private NavigationRoute subscribedNavigation;
    private CityMap map;
    private CityLayout cachedLayout;
    private readonly List<SpawnPoint> candidates = new List<SpawnPoint>();
    private int[] order = Array.Empty<int>();
    private int[] slots = Array.Empty<int>();
    private int previous = -1;
    private uint random;
    private bool reporting;
    private int revision;

    public bool IsActive { get; private set; }
    public SpawnPoint Patient { get; private set; }
    public ReportStatus Status { get; private set; }
    public int CandidateCount => candidates.Count;
    public string Message => IsActive ? "환자가 발생했습니다\n현장으로 이동하세요" : string.Empty;
    public event Action ReportChanged;

    // 참조 교체 전에 기존 보고를 정리하고 현재 도시를 연결한다
    public void Configure(NavigationRoute route)
    {
        CancelReport();
        Unsubscribe();
        navigation = route;
        Bind();
    }

    // 초기화 순서와 관계없이 현재 도시 상태를 연결한다
    private void OnEnable()
    {
        Bind();
    }

    // 비활성화 시 보고와 이벤트 구독을 정리한다
    private void OnDisable()
    {
        Unsubscribe();
        CancelReport();
    }

    // 변경된 참조만 재구독하고 같은 도시의 캐시와 난수 진행 상태를 유지한다
    private void Bind()
    {
        CityMap currentMap = navigation ? navigation.Map : null;
        if (subscribedNavigation != navigation || map != currentMap)
        {
            Unsubscribe();
            if (map != currentMap)
                ClearCache();
            subscribedNavigation = navigation;
            map = currentMap;
        }
        if (isActiveAndEnabled)
        {
            if (subscribedNavigation)
            {
                subscribedNavigation.RouteChanged -= HandleRouteChanged;
                subscribedNavigation.RouteChanged += HandleRouteChanged;
            }
            if (map)
            {
                map.StateChanged -= HandleMapChanged;
                map.StateChanged += HandleMapChanged;
            }
        }
        RefreshCandidates();
    }

    // 구독에 사용한 실제 참조에서 이벤트를 해제한다
    private void Unsubscribe()
    {
        if (subscribedNavigation)
            subscribedNavigation.RouteChanged -= HandleRouteChanged;
        if (map)
            map.StateChanged -= HandleMapChanged;
    }

    // 새 도시에서만 생성된 차선 목록을 한 번 순회하여 후보와 선택 배열을 준비한다
    private void RefreshCandidates()
    {
        if (!map || !map.IsReady || ReferenceEquals(cachedLayout, map.Layout))
            return;
        ClearCache();
        cachedLayout = map.Layout;
        random = unchecked((uint)map.Seed) ^ 0xA511E9B3u;
        for (int i = 0; i < map.LaneCount; i++)
        {
            TrafficLane lane = map.GetLane(i);
            if (!lane || lane.Length <= 0f)
                continue;
            SpawnPoint point = new SpawnPoint(map, lane, lane.Length * 0.5f);
            if (lane.TrySample(point.Distance, out _, out _))
                candidates.Add(point);
        }
        order = new int[candidates.Count];
        slots = new int[candidates.Count];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
            slots[i] = i;
        }
    }

    // 이전 도시의 후보와 직전 위치 및 순서를 제거한다
    private void ClearCache()
    {
        candidates.Clear();
        order = Array.Empty<int>();
        slots = Array.Empty<int>();
        cachedLayout = null;
        previous = -1;
    }

    // 중복 없는 유한 후보 선택 후 기존 네비게이션이 성공한 현장만 확정한다
    public bool TryReport(out string error)
    {
        error = null;
        if (reporting || IsActive)
        {
            error = "이미 상황 보고를 처리하거나 미션을 진행 중입니다.";
            return false;
        }
        if (!isActiveAndEnabled || !navigation || !navigation.isActiveAndEnabled)
            return Fail(ReportStatus.NavigationUnavailable, "활성 네비게이션 참조가 필요합니다.", out error);
        Bind();
        if (!map || !map.IsReady)
            return Fail(ReportStatus.MapUnavailable, "현재 도시가 준비되지 않았습니다.", out error);
        if (candidates.Count == 0)
            return Fail(ReportStatus.NoCandidates, "환자가 발생할 수 있는 위치가 없습니다.", out error);

        reporting = true;
        int request = revision;
        SpawnPoint attempted = default;
        try
        {
            int remaining = order.Length;
            // 직전 위치는 다른 후보가 모두 실패한 경우에만 마지막으로 시도한다
            if (previous >= 0)
            {
                Swap(slots[previous], remaining - 1);
                remaining--;
            }
            for (int attempt = 0; attempt < candidates.Count; attempt++)
            {
                int index;
                if (remaining > 0)
                {
                    random = unchecked(random * 1664525u + 1013904223u);
                    int slot = (int)((random >> 8) % (uint)remaining);
                    index = order[slot];
                    Swap(slot, --remaining);
                }
                else
                    index = previous;
                SpawnPoint point = candidates[index];
                if (!point.TryGetPose(out _, out _))
                    continue;
                attempted = point;
                bool found = navigation.SetDestination(point.Lane, point.Distance);
                if (request != revision || !isActiveAndEnabled)
                {
                    error = "상황 보고 요청이 취소되었습니다.";
                    return false;
                }
                if (found && OwnsDestination(point))
                {
                    Patient = point;
                    previous = index;
                    IsActive = true;
                    Status = ReportStatus.Active;
                    ReportChanged?.Invoke();
                    if (!IsActive)
                        error = "상황 보고 요청이 취소되었습니다.";
                    return IsActive;
                }
                if (navigation.Status == NavigationRoute.RouteStatus.InvalidStart)
                    return Fail(ReportStatus.InvalidStart, "구급차의 현재 도로 위치가 유효하지 않습니다.", out error);
            }
            return Fail(ReportStatus.NoReachablePoint, "접근 가능한 환자 위치를 찾지 못했습니다.", out error);
        }
        finally
        {
            if (!IsActive && OwnsDestination(attempted))
                navigation.ClearDestination();
            reporting = false;
        }
    }

    // 선택 배열과 역색인을 함께 교환하여 직전 후보 제외와 무복원 추출을 상수 시간에 처리한다
    private void Swap(int a, int b)
    {
        int first = order[a];
        int second = order[b];
        order[a] = second;
        order[b] = first;
        slots[first] = b;
        slots[second] = a;
    }

    // 외부 흐름에서 보고를 취소하고 이 보고가 소유한 목적지만 제거한다
    public void CancelReport()
    {
        EndReport(ReportStatus.Idle);
    }

    // 보고 상태를 먼저 변경한 뒤 소유 목적지와 알림을 정리한다
    private void EndReport(ReportStatus status)
    {
        revision++;
        SpawnPoint old = Patient;
        bool notify = IsActive;
        IsActive = false;
        Patient = default;
        Status = status;
        if (OwnsDestination(old))
            navigation.ClearDestination();
        if (notify)
            ReportChanged?.Invoke();
    }

    // 현재 목적지가 이 보고의 차선과 거리에 해당하는지 확인한다
    private bool OwnsDestination(SpawnPoint point)
    {
        return navigation && navigation.HasDestination && !ReferenceEquals(point.Lane, null) && navigation.DestinationLane == point.Lane && navigation.DestinationDistance == point.Distance;
    }

    // 실패 이유를 API와 보고 UI에 전달한다
    private bool Fail(ReportStatus status, string message, out string error)
    {
        Status = status;
        error = message;
        ReportChanged?.Invoke();
        return false;
    }

    // 도시 교체나 비활성화 시 이전 보고를 제거하고 새 도시의 후보를 준비한다
    private void HandleMapChanged()
    {
        if (!map || !map.IsReady || !ReferenceEquals(cachedLayout, map.Layout))
            CancelReport();
        RefreshCandidates();
    }

    // 임시 재탐색은 기다리고 사라진 목적지나 확정된 경로 실패는 보고 취소로 반영한다
    private void HandleRouteChanged()
    {
        if (reporting || !IsActive)
            return;
        if (navigation && navigation.isActiveAndEnabled && OwnsDestination(Patient) && Patient.TryGetPose(out _, out _) && (navigation.Status == NavigationRoute.RouteStatus.Ready || navigation.Status == NavigationRoute.RouteStatus.Invalidated))
            return;
        EndReport(ReportStatus.Cancelled);
    }
}
