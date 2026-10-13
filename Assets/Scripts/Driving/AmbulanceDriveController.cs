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
    [SerializeField, Min(0.1f)] private float throttleResponse = 2f;
    [SerializeField, Min(0.1f)] private float brakeResponse = 6f;

    [Header("Steering and tire grip")]
    [SerializeField, Min(0.01f)] private float wheelbase = 0.68f;
    [SerializeField] private Vector2 axleCenter;
    [SerializeField, Range(1f, 60f)] private float maximumRoadWheelAngle = 45f;
    [SerializeField, Min(0f)] private float lateralGrip = 12f;
    [SerializeField, Min(0f)] private float maximumLateralAcceleration = 3.2f;
    [SerializeField, Min(0f)] private float yawDamping = 0.8f;

    [Header("Stopped detection")]
    [SerializeField, Min(0.001f)] private float stoppedSpeed = 0.025f;
    [SerializeField, Min(0f)] private float stoppedAngularSpeed = 3f;

    private Rigidbody2D body;
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
            || float.IsNaN(brakeStrengthMultiplier) || float.IsInfinity(brakeStrengthMultiplier) || brakeStrengthMultiplier < 1f)
        {
            Debug.LogError("구급차에는 simulated Dynamic Rigidbody2D, 활성 비트리거 BoxCollider2D와 1 이상의 제동 강도 배율이 필요합니다.", this);
            enabled = false;
            return;
        }

        ClearInput();
    }

    private void OnDisable()
    {
        ClearInput();
    }

    private void FixedUpdate()
    {
        // 정지/미준비 상태에서도 카메라에 이전 스텝의 제동을 남기지 않는다.
        brakingDeceleration = 0f;
        if (!IsReady)
        {
            return;
        }

        if (isGearChangePending)
        {
            isGearChangePending = false;
            if (IsStopped)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                isReverse = !isReverse;
                appliedThrottle = 0f;
            }
        }

        float dt = Time.fixedDeltaTime;
        appliedThrottle = Mathf.MoveTowards(appliedThrottle, brake > 0f ? 0f : throttle, throttleResponse * dt);
        // 제동력 상승만 완화하고 페달을 놓으면 기존 속도로 풀어 조작 지연을 줄인다.
        var brakeRampRate = appliedBrake < brake
            ? Mathf.Min(brakeResponse, 1f / MinimumBrakeApplicationSeconds) : brakeResponse;
        appliedBrake = Mathf.MoveTowards(appliedBrake, brake, brakeRampRate * dt);

        if ((brake > 0f || throttle <= 0f) && IsStopped)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            appliedThrottle = 0f;
            return;
        }

        // Positive steering is clockwise; the sprite's local up is the vehicle's front.
        Vector2 forward = Quaternion.Euler(0f, 0f, body.rotation) * Vector2.up;
        float direction = isReverse ? -1f : 1f;
        float topSpeed = isReverse ? reverseTopSpeed : forwardTopSpeed;
        float driveSpeed = Vector2.Dot(body.linearVelocity, forward) * direction;
        float engineAcceleration = isReverse ? reverseAcceleration : acceleration;
        float engineFalloff = Mathf.Clamp01(1f - Mathf.Pow(Mathf.Max(0f, driveSpeed) / topSpeed, 4f));

        // Brake priority applies immediately, even while pedal pressure is building up.
        if (brake <= 0f && appliedBrake <= 0f)
        {
            body.AddForce(forward * (direction * appliedThrottle * engineAcceleration * engineFalloff * body.mass));
        }

        float speed = body.linearVelocity.magnitude;
        // 제동은 페달 압력과 기존 감속도 배율로 계산하며 조향의 횡방향 접지력과 독립적으로 적용한다.
        var requestedBrake = appliedBrake * brakeDeceleration * brakeStrengthMultiplier;
        if (speed > 0f)
        {
            float resistance = rollingResistance + airResistance * speed * speed + requestedBrake;
            // 정지 스텝에서는 소모할 운동량까지만 반대 힘을 적용해 브레이크로 역주행하지 않게 한다.
            float deceleration = Mathf.Min(resistance, speed / dt);
            brakingDeceleration = Mathf.Min(requestedBrake, deceleration);
            body.AddForce(-body.linearVelocity / speed * (deceleration * body.mass));
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

    /// <summary>A discrete request expires at the next physics step; it never queues a shift for later stopping.</summary>
    public void RequestGearChange()
    {
        if (IsReady && Time.timeScale > 0f && IsStopped)
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
}
