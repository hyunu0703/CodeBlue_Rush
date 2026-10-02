using UnityEngine;

/// <summary>구급차의 가속, 제동, 조향과 2D 이동을 처리한다.</summary>
[RequireComponent(typeof(Rigidbody2D))]
public class AmbulanceController : MonoBehaviour
{
    [SerializeField] private float maxSpeed = 10f;
    [SerializeField] private float acceleration = 5f;
    [SerializeField] private float brakePower = 15f;
    [SerializeField] private float coastPower = 2f;
    [SerializeField] private float maxSteerAngle = 35f;
    [SerializeField] private float steerAngleSpeed = 300f;
    [SerializeField, Min(0.1f)] private float wheelBase = 5f;
    [SerializeField] private float grip = 8f;

    public float SteerRate => maxSteerAngle <= 0f ? 0f : steerAngle / maxSteerAngle;
    public bool IsSteering => Mathf.Abs(steer) > 0.01f;
    public bool IsControlLocked => Time.time < lockUntil;

    private Rigidbody2D rb;
    private float steer;
    private float throttle;
    private float brake;
    private float steerAngle;
    private float lockUntil;
    private float knockbackUntil;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }


    // 외부 입력값을 차량 제어값으로 저장한다
    public void SetInput(float steer, float throttle, float brake)
    {
        if (!isActiveAndEnabled || IsControlLocked)
            return;
        this.steer = Mathf.Clamp(steer, -1f, 1f);
        this.throttle = Mathf.Clamp01(throttle);
        this.brake = Mathf.Clamp01(brake);
    }

    // 물리 주기에 맞춰 차량 이동을 처리한다
    private void FixedUpdate()
    {
        if (IsControlLocked)
        {
            if (Time.time >= knockbackUntil)
                rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            return;
        }
        ApplySpeed();
        ApplySteer();
        ApplyGrip();
    }

    // 모든 입력 경로를 잠그고 짧은 물리 넉백을 적용한다
    internal void ApplyCollision(Vector2 velocity, float stunDuration, float knockbackDuration)
    {
        steer = throttle = brake = steerAngle = 0f;
        lockUntil = Time.time + stunDuration;
        knockbackUntil = Time.time + Mathf.Min(knockbackDuration, stunDuration);
        rb.angularVelocity = 0f;
        rb.linearVelocity = velocity;
    }

    // 비활성화 시 이전 조작과 잠금 상태를 정리한다
    private void OnDisable()
    {
        steer = throttle = brake = steerAngle = 0f;
        lockUntil = knockbackUntil = 0f;
        if (rb)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    // 가속, 브레이크, 자연 감속을 처리한다
    private void ApplySpeed()
    {
        float dt = Time.fixedDeltaTime;
        float forwardSpeed = Vector2.Dot(rb.linearVelocity, transform.up);

        if (brake > 0f)
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, Vector2.zero, brakePower * brake * dt);

        else if (throttle > 0f)
        {
            if (forwardSpeed < maxSpeed)
                rb.AddForce((Vector2)transform.up * (acceleration * throttle), ForceMode2D.Force);
        }
        else
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, Vector2.zero, coastPower * dt);

        rb.linearVelocity = Vector2.ClampMagnitude(rb.linearVelocity, maxSpeed);
    }

    // 입력에 따라 바퀴 조향각을 제한하고 차량 방향을 변경한다
    private void ApplySteer()
    {
        float dt = Time.fixedDeltaTime;
        float targetAngle = steer * maxSteerAngle;

        steerAngle = Mathf.MoveTowards(
            steerAngle,
            targetAngle,
            steerAngleSpeed * dt);

        float forwardSpeed = Vector2.Dot(rb.linearVelocity, transform.up);

        if (Mathf.Abs(forwardSpeed) < 0.1f) return;

        float turnRate = forwardSpeed / wheelBase * Mathf.Tan(steerAngle * Mathf.Deg2Rad) * Mathf.Rad2Deg;

        rb.MoveRotation(rb.rotation - turnRate * dt);
    }

    // 옆으로 미끄러지는 속도를 줄인다
    private void ApplyGrip()
    {
        Vector2 sideVelocity = (Vector2)transform.right * Vector2.Dot(rb.linearVelocity, transform.right);
        rb.linearVelocity -= sideVelocity * Mathf.Clamp01(grip * Time.fixedDeltaTime);
    }
}
