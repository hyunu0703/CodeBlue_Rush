using System.Collections.Generic;
using UnityEngine;

/// <summary>일반 차량 접촉의 미션 판정과 양쪽 차량의 넉백 및 경직을 연결한다</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AmbulanceController), typeof(Rigidbody2D), typeof(BoxCollider2D))]
public sealed class AmbulanceCollision : MonoBehaviour
{
    [SerializeField] private PatientReport report;
    [SerializeField] private PatientECG ecg;
    [SerializeField] private HospitalTransfer transfer;
    [SerializeField, Min(0.1f)] private float stunDuration = 2f;
    [SerializeField, Min(0.02f)] private float knockbackDuration = 0.25f;
    [SerializeField, Min(0.1f)] private float knockbackSpeed = 5f;
    private readonly HashSet<Collider2D> contacts = new HashSet<Collider2D>();
    private AmbulanceController controller;
    private Rigidbody2D body;
    private BoxCollider2D shape;
    public float LastCollisionSpeed { get; private set; }

    // 충돌 순간에 사용할 실제 물리 참조를 캐싱한다
    private void Awake()
    {
        controller = GetComponent<AmbulanceController>();
        body = GetComponent<Rigidbody2D>();
        shape = GetComponent<BoxCollider2D>();
    }

    // 실제 미션 소유자와 기존 ECG 및 이송 상태를 연결한다
    public void Configure(PatientReport mission, PatientECG patientECG, HospitalTransfer hospital)
    {
        report = mission;
        ecg = patientECG;
        transfer = hospital;
    }

    // 기존 차량 Trigger의 새 접촉만 처리하고 넉백 전 실제 속도를 보존한다
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!isActiveAndEnabled || !other.attachedRigidbody || !other.attachedRigidbody.TryGetComponent(out VehicleAI vehicle) || other != vehicle.Shape || !vehicle.isActiveAndEnabled)
            return;
        if (controller.IsControlLocked || vehicle.IsStunned || !contacts.Add(other))
            return;

        Vector2 velocity = body.linearVelocity;
        LastCollisionSpeed = velocity.magnitude;
        int mission = report ? report.MissionId : 0;
        bool eligible = report && report.IsPatientOnBoard && !report.HasFatalCollision && ecg && ecg.isActiveAndEnabled && ecg.HasPatient && ecg.Value > 0f && transfer && transfer.isActiveAndEnabled && transfer.MissionId == mission && transfer.Status == HospitalTransfer.TransferStatus.Transporting;
        bool fatal = LastCollisionSpeed >= 5f;
        Vector2 direction = CollisionDirection(other, velocity - other.attachedRigidbody.linearVelocity);
        float duration = Mathf.Max(0.1f, stunDuration);
        float moveDuration = Mathf.Clamp(knockbackDuration, 0.02f, duration);
        float speed = Mathf.Max(0.1f, knockbackSpeed);
        controller.ApplyCollision(direction * speed, duration, moveDuration);
        vehicle.ApplyCollision(this, -direction * speed, duration, moveDuration);
        if (!eligible)
            return;
        report.RecordCollision(mission, fatal);
        if (!fatal)
            ecg.TakeDamage(50f);
    }

    // 접촉 표면의 중심과 상대 이동 방향으로 서로 반대인 밀림 방향을 얻는다
    private Vector2 CollisionDirection(Collider2D other, Vector2 relativeVelocity)
    {
        ColliderDistance2D contact = shape.Distance(other);
        Vector2 direction = contact.pointA - contact.pointB;
        Vector2 centers = (Vector2)shape.bounds.center - (Vector2)other.bounds.center;
        if (direction.sqrMagnitude < 0.0001f)
            direction = centers;
        if (direction.sqrMagnitude < 0.0001f)
            direction = -relativeVelocity;
        if (direction.sqrMagnitude < 0.0001f)
            direction = -transform.up;
        if (Vector2.Dot(direction, centers) < 0f)
            direction = -direction;
        return direction.normalized;
    }

    // 완전히 분리된 접촉은 다음 사고로 다시 처리할 수 있게 한다
    private void OnTriggerExit2D(Collider2D other)
    {
        Forget(other);
    }

    // 풀 반환은 Exit callback 유무와 관계없이 이전 상대를 정리한다
    internal void Forget(Collider2D other)
    {
        contacts.Remove(other);
    }

    // 비활성화 시 접촉 참조를 남기지 않는다
    private void OnDisable()
    {
        ResetCollision();
    }

    // 미션 복귀 시 이전 물리 접촉과 속도 기록을 정리한다
    public void ResetCollision()
    {
        contacts.Clear();
        LastCollisionSpeed = 0f;
    }
}
