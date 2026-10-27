using UnityEngine;

/// <summary>교통 차량의 연쇄 충돌 전환, 유한 질량 충격과 사고 후 마찰을 처리하며 풀 재사용 시 상태를 초기화한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public sealed class RoadTrafficCollision : MonoBehaviour
{
    // 1유닛=6m로 환산한 지구 중력 가속도다. 단위는 유닛/초²다.
    private const float ScaledGravity = 9.80665f / 6f;
    // 사고 후 제동된 타이어가 건조 포장도로에서 미끄러지는 가정이다(NHTSA 재구성 표).
    private const float RoadFriction = 0.6f;
    private const float TrafficMassScale = 1.2f; // 구급차를 제외한 차량의 기준 공차중량에 적용할 배율이다.
    private const float CarMass = 1500f * TrafficMassScale;  // 미등록 차량에 적용하는 질량이다. 단위는 kg다.
    private const float BusMass = 10400f * TrafficMassScale; // 11m급 에어로시티의 기준 공차중량에 배율을 적용한 질량이다. 단위는 kg다.
    private Rigidbody2D body;
    // 풀 재사용 사이에도 차량 종류에 따른 질량을 유지한다.
    private float vehicleMass = CarMass;
    private float chainFatalSpeed = 4f;      // 연쇄 충돌에 전달할 최초 구급차의 파손 판정 속도(유닛/초)다.
    private float chainRestitution = 0.15f;  // 연쇄 충돌에도 유지할 최초 구급차의 기준 반발 계수다.
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

    /// <summary>원본 프리팹 이름으로 대표 공차중량을 선택한다. 풀 생성 시 한 번 호출하며 kg 단위다.</summary>
    internal void ConfigureVehicleModel(string prefabName)
    {
        // 연식/트림 정보가 없는 외형은 아래 제조사 대표 사양을 기준으로 하며 구급차 외에는 질량 배율을 적용한다.
        // 현대 헤리티지/제원: 아반떼 2023 1.6, 그랜저 2022 2.5, 쏘나타 뉴라이즈 LPi, 포터II 2021 디젤.
        // https://www.hyundai.com/kr/ko/brand/brandstory/heritage/2023-the-new-avante
        // https://www.hyundai.com/kr/ko/brand/brandstory/model/grandeur-history/2022-the-all-new-grandeur
        // https://www.hyundai.com/kr/ko/vehicles/sonata-taxi/20my/specifications.html
        // https://www.hyundai.com/kr/ko/brand/brandstory/heritage/2021-porter2
        // 기아 제원: 레이 가솔린 14인치, 쏘렌토 2.5 2WD 5인승 18인치,
        // 스포티지 1.6 2WD 17인치, 카니발 3.5 9인승 18인치.
        // https://www.kia.com/kr/vehicles/ray/specification
        // https://www.kia.com/kr/vehicles/sorento/specification
        // https://www.kia.com/kr/vehicles/sportage/specification
        // https://www.kia.com/kr/vehicles/carnival/specification
        // 버스는 현대 Super Aero City 카탈로그의 City 21+1석 공차중량을 사용한다.
        // https://www.hyundai.com/content/dam/hyundai/et/en/data/marketing/brochure/product/super-aero-city/2012-superaerocity.pdf
        switch (prefabName)
        {
            case "Avante": vehicleMass = 1250f * TrafficMassScale; break;
            case "Grandeur": vehicleMass = 1620f * TrafficMassScale; break;
            case "SonataTaxi": vehicleMass = 1470f * TrafficMassScale; break;
            case "Ray": vehicleMass = 1085f * TrafficMassScale; break;
            case "Sorento": vehicleMass = 1765f * TrafficMassScale; break;
            case "Sportage": vehicleMass = 1575f * TrafficMassScale; break;
            case "Carnival": vehicleMass = 2080f * TrafficMassScale; break;
            case "PorterII": vehicleMass = 1755f * TrafficMassScale; break;
            case "Bus": vehicleMass = BusMass; break;
            // 구급차는 기존 주행 차체의 2500kg을 유지한다. 특장 장비를 포함한 대표 근사값이다.
            case "Ambulance": vehicleMass = 2500f; break;
            // PCX는 현재 교통 생성에서 제외된다. 추후 명시적으로 사용할 경우의 차체 중량만 정의한다.
            // https://www.honda.co.jp/PCX/
            case "PCX": vehicleMass = 133f * TrafficMassScale; break;
            default:
                vehicleMass = CarMass;
                Debug.LogWarning($"{prefabName}: 차량 공차중량이 등록되지 않아 {CarMass}kg을 사용합니다. RoadTrafficCollision의 차종 표를 추가해주세요.", this);
                break;
        }
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
        chainFatalSpeed = 4f;
        chainRestitution = 0.15f;
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
        body.linearDamping = 0f;
        body.angularDamping = 0f;
        body.WakeUp();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // 일반 AI끼리의 접촉으로 사고를 시작하지 않고 구급차에서 전달된 물리 충돌만 이어간다.
        if (!isActiveAndEnabled || !IsReacting) return;
        HandleTrafficContact(body, collision, true, chainFatalSpeed, chainRestitution);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (!isActiveAndEnabled || !IsReacting) return;
        HandleTrafficContact(body, collision, false, chainFatalSpeed, chainRestitution);
    }

    // 최초 접촉에서만 Kinematic 차체의 응답을 유한 질량 충돌로 교체한다.
    internal static void HandleTrafficContact(Rigidbody2D body, Collision2D collision, bool applyImpulse, float fatalImpactSpeed, float impactRestitution)
    {
        if (body == null || !body.simulated || body.bodyType != RigidbodyType2D.Dynamic || collision.rigidbody == null || collision.contactCount == 0
            || !collision.rigidbody.TryGetComponent<RoadTrafficCollision>(out var target) || !target.isActiveAndEnabled)
        {
            return;
        }
        // 접촉 법선의 반대쪽이 상대 차량을 밀어내는 방향이다.
        var contact = collision.GetContact(0);
        var direction = -contact.normal;
        var closingSpeed = Mathf.Abs(Vector2.Dot(collision.relativeVelocity, direction));
        var impactSpeed = collision.relativeVelocity.magnitude;
        var other = collision.rigidbody;
        // 전환 전 상대 속도는 Kinematic 접촉 해결의 영향을 받지 않는다. 단위는 월드 유닛/초다.
        var wasKinematic = other.bodyType == RigidbodyType2D.Kinematic;
        var fatal = impactSpeed >= fatalImpactSpeed;
        target.chainFatalSpeed = fatalImpactSpeed;
        target.chainRestitution = impactRestitution;
        if (!target.IsReacting || (fatal && !target.IsWrecked) || other.bodyType != RigidbodyType2D.Dynamic)
        {
            target.BeginImpact(fatal);
        }
        if (!applyImpulse)
        {
            return;
        }
        if (!wasKinematic)
        {
            return;
        }
        // 면 접촉의 중심을 사용해 정면 접촉에서 모서리 하나 때문에 생기는 인위적 회전을 피한다.
        var point = Vector2.zero;
        var solvedLinearImpulse = Vector2.zero;
        // 기존 해결기가 충돌을 전달한 차체에 적용한 선충격량과 각충격량만 취소한다(kg·유닛/초, kg·유닛²/초).
        var solvedAngularImpulse = 0f;
        for (var index = 0; index < collision.contactCount; index++)
        {
            var sample = collision.GetContact(index);
            point += sample.point;
            var tangent = new Vector2(sample.normal.y, -sample.normal.x);
            var solvedImpulse = sample.normal * sample.normalImpulse + tangent * sample.tangentImpulse;
            solvedLinearImpulse += solvedImpulse;
            var arm = sample.point - body.worldCenterOfMass;
            solvedAngularImpulse += arm.x * solvedImpulse.y - arm.y * solvedImpulse.x;
        }
        point /= collision.contactCount;
        // 상대속도의 부호로 입사 속도를 추정하면 후방 충돌/이탈 접촉에서 속도가 반전될 수 있다.
        // 이 접촉의 엔진 응답만 되돌려 다른 차량과 동시에 충돌했을 때의 속도 변화도 보존한다.
        body.linearVelocity -= solvedLinearImpulse / body.mass;
        if (body.inertia > 0f && (body.constraints & RigidbodyConstraints2D.FreezeRotation) == 0)
        {
            body.angularVelocity -= solvedAngularImpulse / body.inertia * Mathf.Rad2Deg;
        }
        // 속도에 따른 변형 손실은 실차 보정 전 가정이다. 10m/s를 반발 감소의 기준 속도로 사용한다.
        var restitution = impactRestitution / (1f + closingSpeed * 6f / 10f);
        ResolveFiniteMassImpact(body, other, point, direction, restitution);
    }

    // 접촉점의 상대속도와 관성으로 법선 충격량을 계산한다. 단위는 kg·유닛/초다.
    private static void ResolveFiniteMassImpact(Rigidbody2D first, Rigidbody2D second, Vector2 point, Vector2 normal, float restitution)
    {
        // 질량중심에서 접촉점까지의 모멘트 팔은 유닛 단위다.
        var firstOffset = point - first.worldCenterOfMass;
        var secondOffset = point - second.worldCenterOfMass;
        var firstArm = firstOffset.x * normal.y - firstOffset.y * normal.x;
        var secondArm = secondOffset.x * normal.y - secondOffset.y * normal.x;
        var firstInverseInertia = first.inertia > 0f && (first.constraints & RigidbodyConstraints2D.FreezeRotation) == 0 ? 1f / first.inertia : 0f;
        var secondInverseInertia = second.inertia > 0f && (second.constraints & RigidbodyConstraints2D.FreezeRotation) == 0 ? 1f / second.inertia : 0f;
        var relative = first.GetPointVelocity(point) - second.GetPointVelocity(point);
        var closing = Vector2.Dot(relative, normal);
        if (closing <= 0f)
        {
            return;
        }
        // 동일 거리 스케일에서는 kg·유닛² 관성과 유닛/초 속도로 계산해도 운동량이 보존된다.
        var inverseMass = 1f / first.mass + 1f / second.mass
            + firstArm * firstArm * firstInverseInertia + secondArm * secondArm * secondInverseInertia;
        var impulse = normal * ((1f + restitution) * closing / inverseMass);
        first.AddForceAtPosition(-impulse, point, ForceMode2D.Impulse);
        second.AddForceAtPosition(impulse, point, ForceMode2D.Impulse);
        // 차체 사이 접선 마찰 계수 0.3은 실차 보정 전 가정이다. 노면 마찰과 별도로 제한한다.
        var tangent = new Vector2(-normal.y, normal.x);
        firstArm = firstOffset.x * tangent.y - firstOffset.y * tangent.x;
        secondArm = secondOffset.x * tangent.y - secondOffset.y * tangent.x;
        inverseMass = 1f / first.mass + 1f / second.mass
            + firstArm * firstArm * firstInverseInertia + secondArm * secondArm * secondInverseInertia;
        relative = first.GetPointVelocity(point) - second.GetPointVelocity(point);
        var tangentMagnitude = Mathf.Clamp(Vector2.Dot(relative, tangent) / inverseMass,
            -impulse.magnitude * 0.3f, impulse.magnitude * 0.3f);
        first.AddForceAtPosition(-tangent * tangentMagnitude, point, ForceMode2D.Impulse);
        second.AddForceAtPosition(tangent * tangentMagnitude, point, ForceMode2D.Impulse);
    }

    /// <summary>사고 차량의 네 분포 접점에 노면 마찰 충격량을 적용해 이동과 회전을 함께 줄인다.</summary>
    private void FixedUpdate()
    {
        if (!IsReacting || body == null || !body.simulated || body.bodyType != RigidbodyType2D.Dynamic || body.IsSleeping())
        {
            return;
        }
        // 질량과 관성으로 구한 회전 반경은 유닛 단위다. 분포 접점은 차체 관성에 맞춘 근사다.
        var radius = Mathf.Sqrt(body.inertia / body.mass);
        // 한 접점이 담당하는 수직 하중은 전체의 1/4이며 충격량은 kg·유닛/초다.
        var maximumImpulse = RoadFriction * ScaledGravity * body.mass * Time.fixedDeltaTime * 0.25f;
        for (var index = 0; index < 4; index++)
        {
            // 차체 기준 네 접점을 사용해 편심 충돌의 회전 에너지도 노면으로 소산한다.
            var offset = (Vector2)(Quaternion.Euler(0f, 0f, body.rotation + index * 90f + 45f) * Vector2.up) * radius;
            var point = body.worldCenterOfMass + offset;
            var velocity = body.GetPointVelocity(point);
            var speed = velocity.magnitude;
            if (speed <= 0f)
            {
                continue;
            }
            var direction = velocity / speed;
            var arm = offset.x * direction.y - offset.y * direction.x;
            var inverseInertia = body.inertia > 0f && (body.constraints & RigidbodyConstraints2D.FreezeRotation) == 0
                ? 1f / body.inertia : 0f;
            // 유효 질량 상한으로 접점 속도 반전과 마찰에 의한 에너지 증가를 방지한다.
            var impulse = Mathf.Min(maximumImpulse, speed / (1f / body.mass + arm * arm * inverseInertia));
            body.AddForceAtPosition(-direction * impulse, point, ForceMode2D.Impulse);
        }
    }

    // 정지 시에는 물리 엔진의 수면 상태를 사용한다. Static으로 고정하지 않아 재접촉과 지속 밀기가 가능하다.
}
