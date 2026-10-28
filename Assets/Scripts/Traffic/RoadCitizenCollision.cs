using UnityEngine;

/// <summary>시민의 차량 접촉 기록과 쓰러짐을 처리하고 충돌 뒤 이동을 2D 물리에 위임한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D), typeof(Rigidbody2D), typeof(SpriteRenderer))]
public sealed class RoadCitizenCollision : MonoBehaviour
{
    [SerializeField, Min(1f)] private float mass = 70f;                // 시민 물리 차체의 게임용 기준 질량이며 단위는 kg다.
    [SerializeField, Min(0.1f)] private float slideDamping = 3f;       // 충격 뒤 미끄러짐을 줄이는 선형 감쇠다.
    [SerializeField, Min(0.1f)] private float spinDamping = 3f;        // 쓰러진 뒤 회전이 멎도록 하는 각 감쇠다.
    [SerializeField, Range(0f, 1f)] private float restitution = 0.15f; // 최초 차량 충격을 전달할 때 사용하는 반발 비율이다.
    [SerializeField, Min(0.01f)] private float grayFadeSeconds = 0.8f; // 충돌 당시 색에서 진한 회색까지 변하는 게임 시간(초)이다.

    // 쓰러진 시민을 구분하는 진한 회색이다.
    private static readonly Color FallenColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    private RoadCitizenSimulation.Citizen citizen; // 사각형 외형과 별개로 유지하는 현재 시민의 미션 내 식별자다.
    private Rigidbody2D body;                      // 보행 중에는 Kinematic, 충돌 후에는 Dynamic으로 사용하는 차체다.
    private BoxCollider2D bodyCollider;            // 보행 중 접촉 감지와 쓰러진 뒤 실제 충돌을 담당한다.
    private SpriteRenderer spriteRenderer;         // 쓰러짐 자세와 색을 적용할 현재 외형이다.
    private SpriteRenderer outline;                // 쓰러짐과 회전에도 외형을 따라가며 진한 회색을 유지하는 얇은 테두리다.
    private Vector3 uprightScale;                  // 풀 재사용 때 복구할 충돌 직전 외형 크기다.
    private Quaternion uprightRotation;            // 물리 회전 이전의 로컬 방향이다.
    private Color uprightColor;                    // 다음 시민으로 초기화할 때 복구할 충돌 직전 색이다.
    private bool restorePose;                      // OnDisable 뒤에도 유지하여 풀에서 이전 쓰러짐 자세를 복구한다.
    private float grayFadeElapsed;                 // 현재 시민의 색 전환에 사용한 게임 시간(초)이다.
    private bool isFading;                         // 최초 충돌에서만 시작하고 재충돌로 다시 시작하지 않는 색 전환 상태다.

    public Rigidbody2D Body => body;
    public SpriteRenderer Renderer => spriteRenderer;

    // 사각형 안쪽의 얇은 테두리를 공유 스프라이트로 표시하고 풀 재사용 때 정렬을 갱신한다.
    internal void ConfigureOutline(Sprite sprite)
    {
        if (outline == null)
        {
            var root = new GameObject("CitizenOutline");
            root.transform.SetParent(transform, false);
            outline = root.AddComponent<SpriteRenderer>();
        }
        outline.sprite = sprite;
        outline.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        outline.sharedMaterial = spriteRenderer.sharedMaterial;
        outline.sortingLayerID = spriteRenderer.sortingLayerID;
        outline.sortingOrder = spriteRenderer.sortingOrder + 1;
    }

    private void Awake()
    {
        CacheComponents();
    }

    private void Update()
    {
        AdvanceFade(Time.deltaTime);
    }

    // 새 시민 식별자를 연결하고 이전 쓰러짐의 자세·색·물리 상태를 보행 상태로 초기화한다.
    internal void Configure(RoadCitizenSimulation.Citizen value)
    {
        CacheComponents();
        isFading = false;
        grayFadeElapsed = 0f;
        if (restorePose)
        {
            transform.localScale = uprightScale;
            transform.localRotation = uprightRotation;
            spriteRenderer.color = uprightColor;
            restorePose = false;
        }
        citizen = value;
        body.bodyType = RigidbodyType2D.Kinematic;
        body.simulated = true;
        body.gravityScale = 0f;
        body.mass = mass;
        body.linearDamping = 0f;
        body.angularDamping = 0f;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        body.constraints = RigidbodyConstraints2D.None;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.useFullKinematicContacts = true;
        bodyCollider.isTrigger = true;
        body.rotation = transform.eulerAngles.z;
        body.position = transform.position;
    }

    // 보행 위치는 Kinematic 이동으로 적용하고, 새 시민의 최초 배치만 순간 이동한다.
    internal void MoveTo(Vector2 position, bool teleport = false)
    {
        if (citizen == null || citizen.IsFallen) return;
        if (teleport)
        {
            // 최초 생성·풀 재사용 위치를 과거 물리 자세와 보간하면 원점이나 회수 위치에 잔상이 보인다.
            var interpolation = body.interpolation;
            body.interpolation = RigidbodyInterpolation2D.None;
            body.position = position;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            body.interpolation = interpolation;
        }
        else body.MovePosition(position);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandleContact(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // 미션 시작 전에 겹쳐 있던 시민도 기록 시작 뒤 최초 한 번만 집계한다.
        HandleContact(other);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // 쓰러진 시민의 재충돌은 엔진이 해결하며 구급차 실적 기록만 같은 식별자로 전달한다.
        RecordContact(collision.collider);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        RecordContact(collision.collider);
    }

    private void OnDisable()
    {
        isFading = false;
        grayFadeElapsed = 0f;
        citizen = null;
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.simulated = false;
        }
    }

    // 최초 접촉만 보행에서 물리로 전환한다. 차량끼리의 사고 전환이나 실적 규칙은 변경하지 않는다.
    private void HandleContact(Collider2D other)
    {
        if (citizen == null || ApplicationPause.IsPaused || other.isTrigger || other.attachedRigidbody == null) return;
        var vehicle = other.attachedRigidbody;
        var ambulance = vehicle.TryGetComponent<AmbulanceDriveController>(out var drive) && drive.IsReady;
        var traffic = vehicle.TryGetComponent<RoadTrafficCollision>(out var collision) && collision.isActiveAndEnabled;
        if (!ambulance && !traffic) return;
        RecordContact(other);
        if (citizen.IsFallen) return;

        var relativeVelocity = vehicle.GetPointVelocity(body.position) - body.linearVelocity;
        var direction = body.position - vehicle.worldCenterOfMass;
        if (direction.sqrMagnitude < 0.000001f) direction = relativeVelocity;
        direction = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector2.up;
        var closingSpeed = Mathf.Max(0f, Vector2.Dot(relativeVelocity, direction));

        citizen.IsFallen = true;
        citizen.Crossing = false;
        citizen.Speed = 0f;
        uprightScale = transform.localScale;
        uprightRotation = transform.localRotation;
        uprightColor = spriteRenderer.color;
        restorePose = true;
        grayFadeElapsed = 0f;
        isFading = true;
        // 사각형을 가로로 눕힌 실루엣으로 바꾸며 충돌 영역도 같은 비율로 갱신한다.
        var scale = transform.localScale;
        transform.localScale = new Vector3(scale.x * 1.35f, scale.y * (2f / 3f), scale.z);
        body.bodyType = RigidbodyType2D.Dynamic;
        bodyCollider.isTrigger = false;
        body.linearDamping = slideDamping;
        body.angularDamping = spinDamping;

        // 트리거의 첫 접촉에는 엔진 충격이 없으므로 유한 질량 충격을 한 번 전달한다.
        // 이후의 차량·건물·시민 재충돌에는 실제 Dynamic 콜라이더의 엔진 응답만 사용한다.
        var inverseVehicleMass = vehicle.bodyType == RigidbodyType2D.Dynamic ? 1f / vehicle.mass : 0f;
        var impulse = direction * ((1f + restitution) * closingSpeed / (1f / body.mass + inverseVehicleMass));
        body.AddForce(impulse, ForceMode2D.Impulse);
        if (vehicle.bodyType == RigidbodyType2D.Dynamic) vehicle.AddForce(-impulse, ForceMode2D.Impulse);
        body.angularVelocity = closingSpeed * 30f;
        body.WakeUp();
    }

    // 게임 시간으로 최초 충돌 색을 보간하며 일시정지와 비활성화·풀 재사용 경계를 지킨다.
    private void AdvanceFade(float seconds)
    {
        if (!isFading || ApplicationPause.IsPaused || seconds <= 0f) return;
        var duration = Mathf.Max(0.01f, grayFadeSeconds);
        grayFadeElapsed = Mathf.Min(duration, grayFadeElapsed + seconds);
        spriteRenderer.color = Color.Lerp(uprightColor, FallenColor, grayFadeElapsed / duration);
        isFading = grayFadeElapsed < duration;
    }

    // 비활성 풀에서 설정할 때도 필수 컴포넌트를 확보하고 이후 접촉에서는 캐시를 사용한다.
    private void CacheComponents()
    {
        if (body == null) body = GetComponent<Rigidbody2D>();
        if (bodyCollider == null) bodyCollider = GetComponent<BoxCollider2D>();
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
    }

    // 다른 AI 차량은 제외하고 구급차의 활성 미션에만 시민 접촉을 전달한다.
    private void RecordContact(Collider2D other)
    {
        if (citizen == null || other.attachedRigidbody == null) return;
        var body = other.attachedRigidbody;
        if (body.TryGetComponent<AmbulanceDriveController>(out var drive) && drive.IsReady
            && body.TryGetComponent<MissionPerformance>(out var performance))
        {
            performance.RecordCitizenContact(citizen);
        }
    }
}
