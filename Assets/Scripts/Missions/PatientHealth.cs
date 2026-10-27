using UnityEngine;

/// <summary>후송 중인 환자의 HP를 시간과 차량 충격으로 감소시키며 미션 종료 판단에는 관여하지 않는다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public sealed class PatientHealth : MonoBehaviour
{
    [SerializeField, Min(1f)] private float survivalSeconds = 120f;            // 충돌이 없을 때 HP 100%가 소진되는 후송 시간(초)이다.
    [SerializeField, Min(0f)] private float damagePerTenKmh = 5f;              // 접촉 법선 방향 충격 속도 10km/h당 감소하는 HP 퍼센트다.
    [SerializeField, Range(0f, 100f)] private float maximumImpactDamage = 30f; // 한 번의 차량 접촉으로 감소할 수 있는 최대 HP다.
    [SerializeField, Range(0f, 100f)] private float healthPercent = 100f;      // 현재 HP로, 플레이 모드 Inspector에서 상태를 확인할 수 있다.
    private bool isTransporting;                                               // 픽업 완료부터 병원 인계 사이에만 HP 감소를 허용한다.

    public float NormalizedHealth => healthPercent / 100f;
    /// <summary>인계 결과에서 중간 환산 없이 읽는 실제 HP 백분율이다.</summary>
    public float HealthPercent => healthPercent;

    // 새 환자의 상태를 완전히 복원하며 픽업 전 시간과 충돌은 계산하지 않는다.
    public void ResetPatient()
    {
        healthPercent = 100f;
        isTransporting = false;
    }

    // 미션 단계가 바뀔 때 후송 중 HP 감소 여부를 동기화한다.
    public void SetTransporting(bool value)
    {
        isTransporting = value;
    }

    private void Update()
    {
        Advance(Time.deltaTime);
    }

    // 일시정지와 픽업 전 대기를 제외한 경과 시간만 HP에 반영한다.
    internal void Advance(float seconds)
    {
        if (!isTransporting || seconds <= 0f || !RoadLaneMath.IsFinite(seconds)) return;
        healthPercent = Mathf.Max(0f, healthPercent - seconds * 100f / Mathf.Max(1f, survivalSeconds));
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // 건물 접촉과 지속적으로 차량을 미는 동작은 추가 피해로 계산하지 않는다.
        if (!isActiveAndEnabled || !isTransporting || collision.rigidbody == null
            || (!collision.rigidbody.TryGetComponent<RoadTrafficCollision>(out _)
                && !collision.rigidbody.TryGetComponent<AmbulanceDriveController>(out _))) return;
        var impactSpeed = 0f;
        for (var index = 0; index < collision.contactCount; index++)
        {
            impactSpeed = Mathf.Max(impactSpeed,
                Mathf.Abs(Vector2.Dot(collision.relativeVelocity, collision.GetContact(index).normal)));
        }
        ApplyVehicleImpact(impactSpeed);
    }

    // 기존 프로젝트의 1유닛=6m 스케일로 법선 충격 속도를 km/h로 환산한다.
    internal void ApplyVehicleImpact(float worldSpeed)
    {
        if (!isTransporting || !RoadLaneMath.IsFinite(worldSpeed) || worldSpeed <= 0f) return;
        var damage = Mathf.Min(maximumImpactDamage, worldSpeed * 6f * 3.6f / 10f * damagePerTenKmh);
        healthPercent = Mathf.Max(0f, healthPercent - damage);
    }
}
