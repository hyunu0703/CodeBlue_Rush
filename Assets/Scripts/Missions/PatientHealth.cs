using UnityEngine;

/// <summary>미션 시작부터 환자 HP를 시간에 따라 감소시키고 픽업 후에는 차량 충격 피해도 적용한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public sealed class PatientHealth : MonoBehaviour
{
    [SerializeField, Min(1f)] private float survivalSeconds = 180f;            // 충돌이 없을 때 미션 시작부터 HP 100%가 소진되는 시간(초)이다.
    [SerializeField, Range(0f, 100f)] private float healthPercent = 100f;      // 현재 HP로, 플레이 모드 Inspector에서 상태를 확인할 수 있다.
    private bool isTransporting; // 픽업 완료부터 병원 인계 사이에만 차량 충격 피해를 허용한다.
    private bool isTimerRunning; // 픽업 전 이동과 구출 컷신을 포함한 미션 진행 중 시간 감소를 허용한다.

    public float NormalizedHealth => healthPercent / 100f;
    /// <summary>인계 결과에서 중간 환산 없이 읽는 실제 HP 백분율이다.</summary>
    public float HealthPercent => healthPercent;

    // 새 미션 준비 시 체력을 복원하고 타이머와 차량 충격 피해를 중지한다.
    public void ResetPatient()
    {
        healthPercent = 100f;
        isTransporting = false;
        isTimerRunning = false;
    }

    // 미션 단계가 바뀔 때 픽업 후 차량 충격 피해 적용 여부를 동기화한다.
    public void SetTransporting(bool value)
    {
        isTransporting = value;
    }

    // 미션 시작과 종료에 맞춰 시간에 따른 체력 감소를 제어한다.
    public void SetTimerRunning(bool value)
    {
        isTimerRunning = value;
    }

    private void Update()
    {
        Advance(Time.deltaTime);
    }

    // 일시정지와 미션 밖 대기를 제외하고 픽업 전부터 경과 시간을 HP에 반영한다.
    internal void Advance(float seconds)
    {
        if (ApplicationPause.IsPaused || !isTimerRunning || seconds <= 0f || !RoadLaneMath.IsFinite(seconds)) return;
        healthPercent = Mathf.Max(0f, healthPercent - seconds * 100f / Mathf.Max(1f, survivalSeconds));
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // 건물 접촉과 지속적으로 차량을 미는 동작은 추가 피해로 계산하지 않는다.
        if (ApplicationPause.IsPaused || !isActiveAndEnabled || !isTransporting || collision.rigidbody == null
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
        if (ApplicationPause.IsPaused || !isTransporting || !RoadLaneMath.IsFinite(worldSpeed) || worldSpeed <= 0f) return;
        var damage = CalculateImpactDamage(worldSpeed * 6f * 3.6f);
        healthPercent = Mathf.Max(0f, healthPercent - damage);
    }

    // 지정된 속도별 HP 감소량 사이를 선형 보간하고 120km/h부터는 18%p로 제한한다.
    internal static float CalculateImpactDamage(float speedKmh)
    {
        if (speedKmh <= 0f) return 0f;
        if (speedKmh < 30f) return speedKmh * (2f / 30f);
        if (speedKmh < 60f) return 2f + (speedKmh - 30f) * 0.1f;
        if (speedKmh < 80f) return 5f + (speedKmh - 60f) * 0.15f;
        if (speedKmh < 100f) return 8f + (speedKmh - 80f) * 0.2f;
        if (speedKmh < 120f) return 12f + (speedKmh - 100f) * 0.3f;
        return 18f;
    }
}
