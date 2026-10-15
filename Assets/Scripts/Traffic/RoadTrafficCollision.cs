using UnityEngine;

/// <summary>교통 컨트롤러가 생성한 차체의 충돌 수명을 소유한다. 풀 재사용 시 명시적으로 초기화한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public sealed class RoadTrafficCollision : MonoBehaviour
{
    // 최초 속도는 빠르게 전달하고 이후 수 초 동안 점진적으로 감속한다. 단위는 1/초다.
    private const float ImpactDamping = 1.5f;
    // 버스는 지속해서 밀 때도 이동량이 작도록 승용차보다 높은 노면 저항을 적용한다. 단위는 1/초다.
    private const float BusImpactDamping = 3f;
    // 사고 후 회전은 타이어/노면 저항으로 빠르게 줄인다. 단위는 1/초와 도/초다.
    private const float ImpactAngularDamping = 10f;
    private const float MaximumImpactAngularSpeed = 30f;
    // 일반 승용차와 버스의 질량(kg)이다. 버스는 같은 충격과 지속 구동력에 훨씬 적게 밀린다.
    private const float CarMass = 1500f;
    private const float BusMass = 6000f;
    private Rigidbody2D body;
    // 풀 재사용 사이에도 차량 종류에 따른 질량을 유지한다.
    private float vehicleMass = CarMass;
    /// <summary>한 번 접촉하면 풀 초기화 전까지 AI 주행과 차로 복귀를 금지한다.</summary>
    public bool IsReacting { get; private set; }
    public bool IsWrecked { get; private set; }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
    }

    /// <summary>생성기가 프리팹 종류를 전달한다. 비활성 풀 안에서 설정하고 활성화 전에 질량을 적용한다.</summary>
    internal void ConfigureVehicle(bool isBus)
    {
        vehicleMass = isBus ? BusMass : CarMass;
    }

    /// <summary>소유 컨트롤러가 풀에서 꺼내기 전에 호출한다. 이전 충격과 사망 상태를 제거한다.</summary>
    internal void ResetForSpawn()
    {
        if (body == null)
        {
            body = GetComponent<Rigidbody2D>();
        }
        IsReacting = false;
        IsWrecked = false;
        body.bodyType = RigidbodyType2D.Kinematic;
        body.linearDamping = 0f;
        body.angularDamping = 0f;
        body.mass = vehicleMass;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    /// <summary>충돌 순간에만 AI 제어를 물리 제어로 넘긴다. 사망은 같은 활성 수명 동안 취소하지 않는다.</summary>
    internal void BeginImpact(bool fatal)
    {
        if (!isActiveAndEnabled || body == null || !body.simulated)
        {
            return;
        }
        IsReacting = true;
        IsWrecked |= fatal;
        body.bodyType = RigidbodyType2D.Dynamic;
        body.linearDamping = vehicleMass >= BusMass ? BusImpactDamping : ImpactDamping;
        body.angularDamping = ImpactAngularDamping;
        LimitImpactRotation();
        body.WakeUp();
    }

    /// <summary>접촉 해결 직후의 과도한 회전 에너지만 제거한다. 위치나 차체 방향을 복원하지 않는다.</summary>
    internal void LimitImpactRotation()
    {
        if (IsReacting && body != null)
        {
            body.angularVelocity = Mathf.Clamp(body.angularVelocity, -MaximumImpactAngularSpeed, MaximumImpactAngularSpeed);
        }
    }

    /// <summary>다른 사고 차량/장애물과 부딪혀도 회전이 누적되어 폭주하지 않게 한다.</summary>
    private void OnCollisionEnter2D(Collision2D collision)
    {
        LimitImpactRotation();
    }

    /// <summary>접촉을 유지하면서 생기는 회전도 동일한 노면 저항 정책을 따른다.</summary>
    private void OnCollisionStay2D(Collision2D collision)
    {
        LimitImpactRotation();
    }

    // 정지 시에는 물리 엔진의 수면 상태를 사용한다. Static으로 고정하지 않아 재접촉과 지속 밀기가 가능하다.
}
