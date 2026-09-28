using UnityEngine;

/// <summary>구급차의 가속, 제동, 조향과 2D 이동을 처리한다.</summary>
[RequireComponent(typeof(Rigidbody2D))]
public class AmbulanceController : MonoBehaviour
{
    [SerializeField] private float maxSpeed = 12f;
    [SerializeField] private float acceleration = 18f;
    [SerializeField] private float brakePower = 28f;
    [SerializeField] private float coastPower = 5f;
    [SerializeField] private float steerSpeed = 150f;
    [SerializeField] private float grip = 8f;

    private Rigidbody2D rb;
    private float steer;
    private float throttle;
    private float brake;

    private void Awake() => rb = GetComponent<Rigidbody2D>();

    // 외부 입력값을 차량 제어값으로 저장한다
    public void SetInput(float steer, float throttle, float brake)
    {
        this.steer = Mathf.Clamp(steer, -1f, 1f);
        this.throttle = Mathf.Clamp01(throttle);
        this.brake = Mathf.Clamp01(brake);
    }

    // 물리 주기에 맞춰 차량 이동을 처리한다
    private void FixedUpdate()
    {
        ApplySpeed();
        ApplySteer();
        ApplyGrip();
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

    // 현재 속도에 비례해 좌우 회전을 처리한다
    private void ApplySteer()
    {
        float speedRate = Mathf.Clamp01(rb.linearVelocity.magnitude / 1.5f);
        rb.MoveRotation(rb.rotation - steer * steerSpeed * speedRate * Time.fixedDeltaTime);
    }

    // 옆으로 미끄러지는 속도를 줄인다
    private void ApplyGrip()
    {
        Vector2 sideVelocity = (Vector2)transform.right * Vector2.Dot(rb.linearVelocity, transform.right);
        rb.linearVelocity -= sideVelocity * Mathf.Clamp01(grip * Time.fixedDeltaTime);
    }
}
