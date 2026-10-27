using System.Collections.Generic;
using UnityEngine;

/// <summary>출동부터 병원 인계까지 직접 차량 접촉을 기록하며 차량별 지속 접촉을 한 번만 집계한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public sealed class MissionPerformance : MonoBehaviour
{
    private readonly Dictionary<Rigidbody2D, List<(Collider2D Local, Collider2D Other)>> contacts
        = new Dictionary<Rigidbody2D, List<(Collider2D Local, Collider2D Other)>>(); // 차량별로 아직 분리되지 않은 콜라이더 쌍이다.
    private readonly List<Rigidbody2D> separatedVehicles = new List<Rigidbody2D>(); // 순회 후 제거할 차량을 재사용한다.
    private readonly List<Vector2> contactNormals = new List<Vector2>(8); // 첫 접촉 속도 계산에 사용할 법선 버퍼다.
    private Rigidbody2D body;        // 이 미션 기록을 소유한 구급차 차체다.
    private bool isRecording;       // 미션 준비 전·종료 후에는 기록하지 않는다.
    private int lowCollisionCount;  // 첫 법선 충격이 40km/h 미만이었던 차량 접촉 횟수다.
    private int highCollisionCount; // 첫 법선 충격이 40km/h 이상이었던 차량 접촉 횟수다.

    public int LowCollisionCount => lowCollisionCount;
    public int HighCollisionCount => highCollisionCount;
    public bool HasVehicleCollision => lowCollisionCount > 0 || highCollisionCount > 0;
    public bool IsRecording => isActiveAndEnabled && isRecording;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
    }

    private void FixedUpdate()
    {
        if (!IsRecording) return;
        // 비활성화·풀 반환에서 Exit 콜백이 생략돼도 이전 차량 접촉이 남지 않게 한다.
        separatedVehicles.Clear();
        foreach (var vehicle in contacts)
        {
            RemoveSeparatedPairs(vehicle.Value);
            if (vehicle.Value.Count == 0) separatedVehicles.Add(vehicle.Key);
        }
        foreach (var vehicle in separatedVehicles) contacts.Remove(vehicle);
    }

    private void OnDisable()
    {
        SetRecording(false);
    }

    // 새 출동에서만 당일 기록을 비우며 저장된 진행 상태나 환자 상태에는 접근하지 않는다.
    public void BeginMission()
    {
        lowCollisionCount = 0;
        highCollisionCount = 0;
        contacts.Clear();
        isRecording = true;
    }

    // 미션 비활성화·인계 시 기록을 멈추되 확정된 횟수는 다음 출동까지 보존한다.
    public void SetRecording(bool value)
    {
        isRecording = value;
        if (!value) contacts.Clear();
    }

    // 구급차의 기존 충격 처리 전에 호출해 첫 접촉의 상대 속도와 모든 접점 법선을 사용한다.
    internal void RecordContact(Collision2D collision)
    {
        if (!IsRecording || collision.contactCount == 0 || collision.rigidbody == null
            || collision.rigidbody == body || collision.collider == null || collision.otherCollider == null
            || collision.otherCollider.attachedRigidbody != body) return;
        var vehicle = collision.rigidbody;
        if (!vehicle.TryGetComponent<RoadTrafficCollision>(out _)
            && !vehicle.TryGetComponent<AmbulanceDriveController>(out _)) return;

        if (!contacts.TryGetValue(vehicle, out var pairs))
        {
            pairs = new List<(Collider2D Local, Collider2D Other)>(2);
            contacts.Add(vehicle, pairs);
        }
        RemoveSeparatedPairs(pairs);
        if (pairs.Count == 0)
        {
            contactNormals.Clear();
            for (var index = 0; index < collision.contactCount; index++)
                contactNormals.Add(collision.GetContact(index).normal);
            var speed = PerformanceRules.CalculateImpactSpeedKmh(collision.relativeVelocity, contactNormals);
            if (PerformanceRules.GetCollisionLevel(speed) == CollisionLevel.High) highCollisionCount++;
            else lowCollisionCount++;
        }

        // 같은 차체의 다른 콜라이더 진입은 접촉 수명만 늘리고 첫 등급을 다시 판정하지 않는다.
        foreach (var pair in pairs)
            if (pair.Local == collision.otherCollider && pair.Other == collision.collider) return;
        pairs.Add((collision.otherCollider, collision.collider));
    }

    // 일부 콜라이더만 떨어졌다면 차량 전체의 접촉은 계속 유지한다.
    internal void ReleaseContact(Collision2D collision)
    {
        if (collision.rigidbody == null || !contacts.TryGetValue(collision.rigidbody, out var pairs)) return;
        for (var index = pairs.Count - 1; index >= 0; index--)
            if (pairs[index].Local == collision.otherCollider && pairs[index].Other == collision.collider)
                pairs.RemoveAt(index);
        if (pairs.Count == 0) contacts.Remove(collision.rigidbody);
    }

    // 실제 물리 접촉이 없거나 콜라이더가 제거된 쌍만 지워 Exit 누락과 순서 차이에 대비한다.
    private static void RemoveSeparatedPairs(List<(Collider2D Local, Collider2D Other)> pairs)
    {
        for (var index = pairs.Count - 1; index >= 0; index--)
        {
            var pair = pairs[index];
            if (pair.Local == null || pair.Other == null || !pair.Local.isActiveAndEnabled
                || !pair.Other.isActiveAndEnabled || !pair.Local.IsTouching(pair.Other)) pairs.RemoveAt(index);
        }
    }
}
