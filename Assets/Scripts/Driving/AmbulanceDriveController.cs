using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public sealed class AmbulanceDriveController : MonoBehaviour
{
    [Header("Speed (world units / second)")]
    [SerializeField, Min(0.1f)] private float forwardTopSpeed = 9f;
    [SerializeField, Min(0.1f)] private float reverseTopSpeed = 2f;
    [SerializeField, Min(0f)] private float acceleration = 0.85f;
    [SerializeField, Min(0f)] private float reverseAcceleration = 0.55f;
    [SerializeField, Min(0f)] private float brakeDeceleration = 1.8f;
    // 기존 씬의 제동 튜닝은 보존하며 실제 적용 강도를 1.5배 높인다. 가속도 단위는 월드 유닛/초²다.
    [SerializeField, Min(1f)] private float brakeStrengthMultiplier = 1.5f;
    // 페달을 끝까지 밟아도 최대 제동력까지 최소 0.5초에 걸쳐 올라간다. 해제 반응은 기존 튜닝을 따른다.
    private const float MinimumBrakeApplicationSeconds = 0.5f;
    [SerializeField, Min(0f)] private float rollingResistance = 0.08f;
    [SerializeField, Min(0f)] private float airResistance = 0.012f;
    // 엑셀을 놓았을 때 추가하는 엔진 브레이크 감속도다. 단위는 월드 유닛/초²다.
    [SerializeField, Min(0f)] private float engineBrakeDeceleration = 0.6f;
    [SerializeField, Min(0.1f)] private float throttleResponse = 2f;
    [SerializeField, Min(0.1f)] private float brakeResponse = 6f;

    [Header("Steering and tire grip")]
    [SerializeField, Min(0.01f)] private float wheelbase = 0.68f;
    [SerializeField] private Vector2 axleCenter;
    [SerializeField, Range(1f, 60f)] private float maximumRoadWheelAngle = 45f;
    [SerializeField, Min(0f)] private float lateralGrip = 12f;
    [SerializeField, Min(0f)] private float maximumLateralAcceleration = 3.2f;
    [SerializeField, Min(0f)] private float yawDamping = 0.8f;

    [Header("Responsive steering")]
    // 새 조향을 기본 사용하며, 해제하면 저장된 차축/타이어 설정으로 비교 주행할 수 있다.
    [SerializeField] private bool useResponsiveSteering = true;
    // 충분히 움직일 때 최대 조향으로 회전하는 속도다. 단위는 도/초다.
    [SerializeField, Min(1f)] private float maximumTurnSpeed = 120f;
    // 목표 회전 속도에 접근하는 가속도다. 단위는 도/초²다.
    [SerializeField, Min(1f)] private float turnAcceleration = 1080f;
    // 이 전후방 속도부터 최대 회전 속도를 허용한다. 정지 상태의 제자리 회전을 막는다.
    [SerializeField, Min(0.1f)] private float fullSteeringSpeed = 2f;
    // 충돌 등으로 남은 횡방향 움직임을 주행 방향에 맞추는 응답률이다. 단위는 1/초다.
    [SerializeField, Min(0f)] private float velocityAlignment = 8f;

    [Header("Stopped detection")]
    [SerializeField, Min(0.001f)] private float stoppedSpeed = 0.025f;
    [SerializeField, Min(0f)] private float stoppedAngularSpeed = 3f;

    private Rigidbody2D body;
    [Header("Vehicle collision (world units / second)")]
    [SerializeField, Min(0.1f)] private float impactMinimumSpeed = 3.5f;
    [SerializeField, Min(0.1f)] private float fatalImpactSpeed = 6f;
    [SerializeField, Range(0f, 1f)] private float impactRestitution = 0.15f;
    [SerializeField, Min(0f)] private float impactControlDelay = 0.35f;
    private float impactControlRemaining;
    private float steering;
    private float throttle;
    private float brake;
    private float appliedThrottle;
    private float appliedBrake;
    private bool isReverse;
    private bool isGearChangePending;
    // 이번 물리 스텝에 적용한 브레이크 감속도다. 카메라의 제동 반동에 사용한다.
    private float brakingDeceleration;

    public bool IsReverse => isReverse;
    public float Speed => body != null ? body.linearVelocity.magnitude : 0f;
    /// <summary>카메라 연출에 사용하는 실제 월드 속도다. 준비되지 않은 차체는 정지로 취급한다.</summary>
    public Vector2 Velocity => IsReady ? body.linearVelocity : Vector2.zero;
    /// <summary>실제 적용된 브레이크 감속도다. 공기 저항/충돌 감속은 포함하지 않는다.</summary>
    public float BrakingDeceleration => IsReady ? brakingDeceleration : 0f;
    /// <summary>최대 전진 속도에 대한 현재 속도 비율이다. 카메라 반동의 세기를 제한한다.</summary>
    public float NormalizedSpeed => Mathf.Clamp01(Speed / forwardTopSpeed);
    /// <summary>현재 요청된 브레이크 페달 압력이다. 0~1 범위이며 비활성 차량은 0이다.</summary>
    public float BrakePressure => IsReady ? brake : 0f;
    public bool IsStopped => body != null && body.linearVelocity.sqrMagnitude <= stoppedSpeed * stoppedSpeed
        && Mathf.Abs(body.angularVelocity) <= stoppedAngularSpeed;
    public bool IsReady => isActiveAndEnabled && body != null && body.simulated
        && body.bodyType == RigidbodyType2D.Dynamic;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
    }

    private void OnEnable()
    {
        var bodyCollider = GetComponent<BoxCollider2D>();
        if (body == null || body.bodyType != RigidbodyType2D.Dynamic || !body.simulated
            || bodyCollider == null || bodyCollider.isTrigger || !bodyCollider.enabled
            || float.IsNaN(brakeStrengthMultiplier) || float.IsInfinity(brakeStrengthMultiplier) || brakeStrengthMultiplier < 1f
            || float.IsNaN(engineBrakeDeceleration) || float.IsInfinity(engineBrakeDeceleration) || engineBrakeDeceleration < 0f
            || !RoadLaneMath.IsFinite(impactMinimumSpeed) || impactMinimumSpeed <= 0f
            || !RoadLaneMath.IsFinite(fatalImpactSpeed) || fatalImpactSpeed < impactMinimumSpeed
            || !RoadLaneMath.IsFinite(impactRestitution) || impactRestitution < 0f || impactRestitution > 1f
            || !RoadLaneMath.IsFinite(impactControlDelay) || impactControlDelay < 0f)
        {
            Debug.LogError("구급차의 Dynamic Rigidbody2D, 비트리거 BoxCollider2D와 제동/충돌 설정을 확인해주세요. 충돌 기준 속도는 양수, 사망 기준은 충돌 기준 이상, 반발 계수는 0~1, 제어 유예 시간은 0 이상이어야 합니다.", this);
            enabled = false;
            return;
        }

        ClearInput();
    }

    private void OnDisable()
    {
        ClearInput();
        impactControlRemaining = 0f;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!IsReady || collision.rigidbody == null || collision.contactCount == 0
            || !collision.rigidbody.TryGetComponent<RoadTrafficCollision>(out var target) || !target.isActiveAndEnabled)
        {
            return;
        }
        // 접촉 법선의 반대쪽이 상대 차량을 밀어내는 방향이다.
        var contact = collision.GetContact(0);
        var direction = -contact.normal;
        var closingSpeed = Mathf.Abs(Vector2.Dot(collision.relativeVelocity, direction));
        if (closingSpeed < impactMinimumSpeed)
        {
            return;
        }
        var other = collision.rigidbody;
        target.BeginImpact(closingSpeed >= Mathf.Max(impactMinimumSpeed, fatalImpactSpeed));
        var reducedMass = body.mass * other.mass / (body.mass + other.mass);
        var impulse = direction * (reducedMass * closingSpeed * (1f + impactRestitution));
        // 충돌 해결기가 이미 감속시킨 뒤 추가하는 연출 충격량이다. 양쪽에 같은 크기로 적용한다.
        other.AddForceAtPosition(impulse, contact.point, ForceMode2D.Impulse);
        body.AddForceAtPosition(-impulse, contact.point, ForceMode2D.Impulse);
        impactControlRemaining = impactControlDelay;
    }

    /// <summary>고정 물리 스텝에서 페달 응답, 구동력, 제동과 접지력을 적용한다.</summary>
    private void FixedUpdate()
    {
        // 정지/미준비 상태에서도 카메라에 이전 스텝의 제동을 남기지 않는다.
        brakingDeceleration = 0f;
        if (!IsReady)
        {
            return;
        }

        // 충격 직후 조향 정렬과 페달 힘이 반동을 지우지 않도록 잠시 물리 엔진에 맡긴다.
        if (impactControlRemaining > 0f)
        {
            impactControlRemaining = Mathf.Max(0f, impactControlRemaining - Time.fixedDeltaTime);
            return;
        }

        if (isGearChangePending)
        {
            isGearChangePending = false;
            // 기어 선택만 바꾸고 현재 운동량은 유지해 주행 방향이 순간적으로 뒤집히지 않게 한다.
            isReverse = !isReverse;
            appliedThrottle = 0f;
        }

        // 차량 앞쪽에 대한 선택 방향의 속도(월드 유닛/초)가 음수이면 반대 방향으로 이동 중이다.
        Vector2 forward = Quaternion.Euler(0f, 0f, body.rotation) * Vector2.up;
        float direction = isReverse ? -1f : 1f;
        float driveSpeed = Vector2.Dot(body.linearVelocity, forward) * direction;
        // 반대 기어에서 가속하면 페달 압력(0~1)을 제동에 사용하고, 정지 후 구동력으로 전환한다.
        bool isDirectionBraking = driveSpeed < 0f && throttle > 0f;
        float targetBrake = isDirectionBraking ? Mathf.Max(brake, throttle) : brake;
        float dt = Time.fixedDeltaTime;
        appliedThrottle = Mathf.MoveTowards(appliedThrottle, brake > 0f ? 0f : throttle, throttleResponse * dt);
        // 제동력 상승만 완화하고 페달을 놓으면 기존 속도로 풀어 조작 지연을 줄인다.
        var brakeRampRate = appliedBrake < targetBrake
            ? Mathf.Min(brakeResponse, 1f / MinimumBrakeApplicationSeconds) : brakeResponse;
        appliedBrake = Mathf.MoveTowards(appliedBrake, targetBrake, brakeRampRate * dt);

        if ((brake > 0f || throttle <= 0f) && IsStopped)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            appliedThrottle = 0f;
            return;
        }

        float topSpeed = isReverse ? reverseTopSpeed : forwardTopSpeed;
        float engineAcceleration = isReverse ? reverseAcceleration : acceleration;
        float engineFalloff = Mathf.Clamp01(1f - Mathf.Pow(Mathf.Max(0f, driveSpeed) / topSpeed, 4f));

        // 제동 중에는 구동력을 겹치지 않으며, 방향 전환 제동도 같은 우선순위를 따른다.
        if (!isDirectionBraking && throttle > 0f && brake <= 0f && appliedBrake <= 0f)
        {
            body.AddForce(forward * (direction * appliedThrottle * engineAcceleration * engineFalloff * body.mass));
        }

        float speed = body.linearVelocity.magnitude;
        // 제동은 페달 압력과 기존 감속도 배율로 계산하며 조향의 횡방향 접지력과 독립적으로 적용한다.
        var requestedBrake = appliedBrake * brakeDeceleration * brakeStrengthMultiplier;
        // 페달 해제 시 잔여 구동력은 끊고 기존 엑셀 응답 시간에 맞춰 엔진 제동을 부드럽게 올린다.
        // 브레이크 페달/방향 전환 제동에는 추가하지 않아 기존 제동 강도와 카메라 반동을 보존한다.
        var engineBrake = throttle <= 0f && brake <= 0f && appliedBrake <= 0f
            ? engineBrakeDeceleration * (1f - appliedThrottle) : 0f;
        if (speed > 0f)
        {
            // 전체 감속도는 자연 저항, 페달 제동과 엑셀 해제 시 엔진 제동의 합이다.
            float resistance = rollingResistance + airResistance * speed * speed + requestedBrake + engineBrake;
            // 정지 스텝에서는 소모할 운동량까지만 반대 힘을 적용해 브레이크로 역주행하지 않게 한다.
            float deceleration = Mathf.Min(resistance, speed / dt);
            brakingDeceleration = Mathf.Min(requestedBrake, deceleration);
            body.AddForce(-body.linearVelocity / speed * (deceleration * body.mass));
        }

        if (useResponsiveSteering)
        {
            ApplyResponsiveSteering(forward, dt);
            return;
        }

        float roadWheelAngle = steering * maximumRoadWheelAngle;
        Vector2 frontDirection = Quaternion.Euler(0f, 0f, -roadWheelAngle) * forward;
        Vector2 center = body.position + (Vector2)(Quaternion.Euler(0f, 0f, body.rotation) * (Vector3)axleCenter);
        ApplyTireGrip(center + forward * (wheelbase * 0.5f), frontDirection, dt);
        ApplyTireGrip(center - forward * (wheelbase * 0.5f), forward, dt);
        body.AddTorque(-body.angularVelocity * Mathf.Deg2Rad * body.inertia * yawDamping);
    }

    public void SetInput(float steering, float throttle, float brake)
    {
        if (!IsReady)
        {
            ClearInput();
            return;
        }

        this.steering = Mathf.Clamp(steering, -1f, 1f);
        this.brake = Mathf.Clamp01(brake);
        this.throttle = this.brake > 0f ? 0f : Mathf.Clamp01(throttle);
    }

    /// <summary>다음 물리 스텝에서 속도와 무관하게 기어를 바꾼다. 이동 방향은 페달과 물리 제동으로 전환한다.</summary>
    public void RequestGearChange()
    {
        if (IsReady && Time.timeScale > 0f)
        {
            isGearChangePending = true;
        }
    }

    public void ClearInput()
    {
        steering = 0f;
        throttle = 0f;
        brake = 0f;
        appliedThrottle = 0f;
        appliedBrake = 0f;
        isGearChangePending = false;
        brakingDeceleration = 0f;
    }

    /// <summary>Used by the scoped scene installer only for a newly added controller. Values are in world units.</summary>
    public void ConfigureAxles(Vector2 center, float length)
    {
        axleCenter = center;
        wheelbase = Mathf.Max(0.01f, length);
    }

    private void ApplyTireGrip(Vector2 position, Vector2 tireForward, float dt)
    {
        Vector2 tireRight = new Vector2(tireForward.y, -tireForward.x);
        float sidewaysSpeed = Vector2.Dot(body.GetPointVelocity(position), tireRight);
        float correction = Mathf.Min(lateralGrip, 1f / dt);
        float lateralAcceleration = Mathf.Clamp(-sidewaysSpeed * correction,
            -maximumLateralAcceleration, maximumLateralAcceleration);
        // 각 차축에 질량 절반을 적용한다. 브레이크 입력과 무관하게 기존 횡방향 접지력 한도를 사용한다.
        body.AddForceAtPosition(tireRight * (lateralAcceleration * body.mass * 0.5f), position);
    }

    /// <summary>고정 스텝에서 회전 속도와 이동 방향을 함께 보정한다. 속력은 보존하고 충돌은 물리 엔진에 맡긴다.</summary>
    private void ApplyResponsiveSteering(Vector2 forward, float dt)
    {
        // 실제 전후방 이동 속도로 후진 조향을 판정해 기어만 바꿔도 조향이 뒤집히지 않게 한다.
        var signedSpeed = Vector2.Dot(body.linearVelocity, forward);
        // 저속에서는 회전량을 줄이고, 고속에서는 최대 회전 속도를 제한한다. 단위는 도/초다.
        var targetTurnSpeed = -steering * maximumTurnSpeed
            * Mathf.Clamp(signedSpeed / fullSteeringSpeed, -1f, 1f);
        // 현재 물리 회전 속도다. 반대 조향은 이전 회전 관성을 다음 고정 스텝에서 끊는다.
        var currentTurnSpeed = body.angularVelocity;
        if (currentTurnSpeed * targetTurnSpeed < 0f)
        {
            currentTurnSpeed = 0f;
        }
        body.angularVelocity = Mathf.MoveTowards(currentTurnSpeed, targetTurnSpeed, turnAcceleration * dt);

        // 이번 스텝의 회전만큼 속도 벡터도 돌려, 차체와 주행 경로의 응답을 일치시킨다.
        var turnedVelocity = (Vector2)(Quaternion.Euler(0f, 0f, body.angularVelocity * dt) * body.linearVelocity);
        // 다음 자세의 전방 벡터다. 후진 중에는 반대 방향으로 정렬한다.
        var nextForward = (Vector2)(Quaternion.Euler(0f, 0f, body.angularVelocity * dt) * forward);
        // 옆으로 밀린 경우에만 남은 미끄러짐을 부드럽게 줄인다. 충돌 반동의 앞뒤 부호는 유지한다.
        var alignmentAngle = signedSpeed != 0f
            ? Vector2.SignedAngle(turnedVelocity, nextForward * Mathf.Sign(signedSpeed)) : 0f;
        // 프레임률에 덜 의존하는 보정 비율이며 0~1 범위다.
        var alignmentFraction = 1f - Mathf.Exp(-velocityAlignment * dt);
        body.linearVelocity = Quaternion.Euler(0f, 0f, alignmentAngle * alignmentFraction) * turnedVelocity;
    }
}
