using System;
using UnityEngine;

/// <summary>생성된 도시의 실제 차선에 병원 지점과 차량 도착 영역을 등록한다</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CircleCollider2D))]
public sealed class HospitalArrivalZone : MonoBehaviour
{
    [SerializeField] private CityMap map;
    [SerializeField, Min(0)] private int laneOffset;
    private CityMap subscribedMap;
    private CityLayout layout;
    private CircleCollider2D area;
    public CityMap Map => map;
    public TrafficLane Lane { get; private set; }
    public float Distance { get; private set; }
    public Vector3 HospitalPoint => transform.position;
    public event Action<HospitalArrivalZone, Rigidbody2D> VehicleEntered;

    // 영역 참조를 한 번 캐싱한다
    private void Awake()
    {
        area = GetComponent<CircleCollider2D>();
    }

    // 기존 도시 생성 이벤트와 현재 도시를 연결한다
    private void OnEnable()
    {
        Bind();
    }

    // 이전 도시와 차선 참조를 해제한다
    private void OnDisable()
    {
        Unsubscribe();
        layout = null;
        Lane = null;
        Distance = 0f;
    }

    // 도로 생성기를 변경하지 않고 병원 차선 선택 정보를 연결한다
    public void Configure(CityMap city, int offset = 0)
    {
        Unsubscribe();
        map = city;
        laneOffset = Mathf.Max(0, offset);
        layout = null;
        Lane = null;
        if (isActiveAndEnabled)
            Bind();
    }

    // 같은 맵 이벤트를 중복 없이 구독한다
    private void Bind()
    {
        Unsubscribe();
        subscribedMap = map;
        if (subscribedMap)
            subscribedMap.StateChanged += Refresh;
        Refresh();
    }

    // 실제 구독한 맵에서 해제한다
    private void Unsubscribe()
    {
        if (subscribedMap)
            subscribedMap.StateChanged -= Refresh;
        subscribedMap = null;
    }

    // 도시 생성 시 Seed와 차선 배열의 직접 색인으로 병원 위치를 확보한다
    private void Refresh()
    {
        if (!map || !map.IsReady || map.LaneCount == 0)
        {
            Lane = null;
            layout = null;
            return;
        }
        if (layout == map.Layout)
            return;
        layout = map.Layout;
        int index = (int)(((uint)map.Seed + (ulong)(uint)laneOffset) % (uint)map.LaneCount);
        Lane = map.GetLane(index);
        Distance = Lane ? Lane.Length * 0.5f : 0f;
        if (Lane && Lane.TrySample(Distance, out Vector3 point, out Vector3 direction))
            transform.SetPositionAndRotation(point, Quaternion.FromToRotation(Vector3.up, direction));
    }

    // 현재 도시와 차선 및 실제 병원 영역의 위치가 일치하는지 확인한다
    public bool IsValidFor(CityMap city)
    {
        return isActiveAndEnabled && map == city && map && layout == map.Layout && map.ContainsLane(Lane) && area && area.enabled && area.isTrigger && Lane.TrySample(Distance, out Vector3 point, out _) && (point - transform.position).sqrMagnitude < 0.0025f;
    }

    // 실제 물리 차량이 현재 영역과 겹치는지 검사한다
    public bool Contains(Rigidbody2D vehicle)
    {
        return vehicle && vehicle.simulated && vehicle.gameObject.activeInHierarchy && area && area.OverlapPoint(vehicle.position);
    }

    // 실제 Rigidbody 참조를 전달하여 다른 차량을 구분한다
    private void OnTriggerEnter2D(Collider2D other)
    {
        VehicleEntered?.Invoke(this, other.attachedRigidbody);
    }

    // 영역 안에서 픽업한 경우에도 다음 물리 주기에 도착을 검사한다
    private void OnTriggerStay2D(Collider2D other)
    {
        VehicleEntered?.Invoke(this, other.attachedRigidbody);
    }
}
