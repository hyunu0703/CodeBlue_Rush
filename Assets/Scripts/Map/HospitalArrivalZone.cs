using System;
using UnityEngine;

/// <summary>고정 Scene의 병원 접근 지점과 차량 도착 영역을 제공한다</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CircleCollider2D))]
public sealed class HospitalArrivalZone : MonoBehaviour
{
    [SerializeField] private CityMap map;
    [SerializeField] private EnvironmentSlot slot;
    private CircleCollider2D area;
    public CityMap Map => map;
    public TrafficLane Lane => slot ? slot.Lane : null;
    public float Distance => slot ? slot.Distance : 0f;
    public Vector3 HospitalPoint => transform.position;
    public event Action<HospitalArrivalZone, Rigidbody2D> VehicleEntered;

    // 영역 참조만 캐싱하고 Scene에 저장한 병원 위치를 유지한다
    private void Awake()
    {
        area = GetComponent<CircleCollider2D>();
    }

    // 명시적인 고정 병원 접근 지점을 연결한다
    public void Configure(CityMap city, EnvironmentSlot access)
    {
        map = city;
        slot = access;
    }

    // Scene의 고정 병원과 도로 접근 참조를 확인한다
    public bool IsValidFor(CityMap city)
    {
        if (!area)
            area = GetComponent<CircleCollider2D>();
        return isActiveAndEnabled && map == city && map && slot && slot.IsValidFor(map, EnvironmentSlot.Usage.Hospital) && area && area.enabled && area.isTrigger && (slot.transform.position - transform.position).sqrMagnitude < 0.0025f;
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
