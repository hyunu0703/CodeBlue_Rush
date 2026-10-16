using UnityEngine;

/// <summary>교통 컨트롤러가 생성한 차체의 충돌 수명을 소유한다. 풀 재사용 시 명시적으로 초기화한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public sealed class RoadTrafficCollision : MonoBehaviour
{
    // 1유닛=6m로 환산한 지구 중력 가속도다. 단위는 유닛/초²다.
    private const float ScaledGravity = 9.80665f / 6f;
    // 사고 후 제동된 타이어가 건조 포장도로에서 미끄러지는 가정이다(NHTSA 재구성 표).
    private const float RoadFriction = 0.6f;
    // 미등록 차량의 호환 기본값과 11m급 에어로시티 대표 공차중량이다. 단위는 kg다.
    private const float CarMass = 1500f;
    private const float BusMass = 10400f;
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

    /// <summary>원본 프리팹 이름으로 대표 공차중량을 선택한다. 풀 생성 시 한 번 호출하며 kg 단위다.</summary>
    internal void ConfigureVehicleModel(string prefabName)
    {
        // 연식/트림 정보가 없는 외형은 아래 제조사 대표 사양으로 고정하고 승객/화물은 추가하지 않는다.
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
            case "Avante": vehicleMass = 1250f; break;
            case "Grandeur": vehicleMass = 1620f; break;
            case "SonataTaxi": vehicleMass = 1470f; break;
            case "Ray": vehicleMass = 1085f; break;
            case "Sorento": vehicleMass = 1765f; break;
            case "Sportage": vehicleMass = 1575f; break;
            case "Carnival": vehicleMass = 2080f; break;
            case "PorterII": vehicleMass = 1755f; break;
            case "Bus": vehicleMass = BusMass; break;
            // 구급차는 기존 주행 차체의 2500kg을 유지한다. 특장 장비를 포함한 대표 근사값이다.
            case "Ambulance": vehicleMass = 2500f; break;
            // PCX는 현재 교통 생성에서 제외된다. 추후 명시적으로 사용할 경우의 차체 중량만 정의한다.
            // https://www.honda.co.jp/PCX/
            case "PCX": vehicleMass = 133f; break;
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
