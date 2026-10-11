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
    [SerializeField, Min(0f)] private float rollingResistance = 0.08f;
    [SerializeField, Min(0f)] private float airResistance = 0.012f;
    [SerializeField, Min(0.1f)] private float throttleResponse = 2f;
    [SerializeField, Min(0.1f)] private float brakeResponse = 6f;

    [Header("Steering and tire grip")]
    [SerializeField, Min(0.01f)] private float wheelbase = 0.68f;
    [SerializeField] private Vector2 axleCenter;
    [SerializeField, Range(1f, 60f)] private float maximumRoadWheelAngle = 32f;
    [SerializeField, Range(0.05f, 1f)] private float highSpeedSteeringFraction = 0.3f;
    [SerializeField, Min(0f)] private float lateralGrip = 6f;
    [SerializeField, Min(0f)] private float maximumLateralAcceleration = 1.6f;
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

    public bool IsReverse => isReverse;
    public float Speed => body != null ? body.linearVelocity.magnitude : 0f;
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
            || bodyCollider == null || bodyCollider.isTrigger || !bodyCollider.enabled)
        {
            Debug.LogError("Ambulance requires a simulated Dynamic Rigidbody2D and an enabled, non-trigger BoxCollider2D.", this);
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
        appliedBrake = Mathf.MoveTowards(appliedBrake, brake, brakeResponse * dt);

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
        if (speed > 0f)
        {
            float resistance = rollingResistance + airResistance * speed * speed + appliedBrake * brakeDeceleration;
            // Cap opposing force so braking cannot push the vehicle into reverse in one step.
            float deceleration = Mathf.Min(resistance, speed / dt);
            body.AddForce(-body.linearVelocity / speed * (deceleration * body.mass));
        }

        float speedFraction = Mathf.Clamp01(speed / forwardTopSpeed);
        float steeringFraction = Mathf.Lerp(1f, highSpeedSteeringFraction, speedFraction * speedFraction);
        float roadWheelAngle = steering * maximumRoadWheelAngle * steeringFraction;
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
        // Each axle carries half the mass. Saturation permits inertia and slip in fast corners.
        body.AddForceAtPosition(tireRight * (lateralAcceleration * body.mass * 0.5f), position);
    }
}
