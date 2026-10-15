using UnityEngine;

/// <summary>교통 컨트롤러가 생성한 차체의 충돌 수명을 소유한다. 풀 재사용 시 명시적으로 초기화한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public sealed class RoadTrafficCollision : MonoBehaviour
{
    private const float ImpactDamping = 3f;
    private const float RecoveryDelay = 1f;
    private const float RecoveryAcceleration = 4f;
    private Rigidbody2D body;
    private float elapsed;
    private Vector2 returnPosition;
    private float returnRotation;
    public bool IsReacting { get; private set; }
    public bool IsWrecked { get; private set; }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
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
        elapsed = 0f;
        body.bodyType = RigidbodyType2D.Kinematic;
        body.linearDamping = 0f;
        body.angularDamping = 0f;
        body.mass = 1500f;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    /// <summary>충돌 순간에만 AI 제어를 물리 제어로 넘긴다. 사망은 같은 활성 수명 동안 취소하지 않는다.</summary>
    internal void BeginImpact(bool fatal)
    {
        if (!isActiveAndEnabled || body == null || !body.simulated)
        {
            return;
        }
        if (!IsReacting)
        {
            returnPosition = body.position;
            returnRotation = body.rotation;
        }
        IsReacting = true;
        IsWrecked |= fatal;
        elapsed = 0f;
        body.bodyType = RigidbodyType2D.Dynamic;
        body.linearDamping = ImpactDamping;
        body.angularDamping = ImpactDamping;
        body.WakeUp();
    }

    /// <summary>컨트롤러의 고정 스텝에서 호출한다. 사망 차량은 충격이 가라앉은 위치에서 고정한다.</summary>
    internal void Tick(float seconds)
    {
        if (!IsReacting || body == null || !body.simulated)
        {
            return;
        }
        elapsed += seconds;
        if (IsWrecked)
        {
            if (elapsed >= RecoveryDelay && body.linearVelocity.sqrMagnitude < 0.0025f
                && Mathf.Abs(body.angularVelocity) < 1f)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.bodyType = RigidbodyType2D.Static;
            }
            return;
        }
        if (elapsed < RecoveryDelay)
        {
            return;
        }
        // 원래 차로까지 물리적으로 복귀한다. 벽에 막히면 순간이동하지 않고 대기한다.
        var offset = returnPosition - body.position;
        var angle = Mathf.DeltaAngle(body.rotation, returnRotation);
        body.AddForce(Vector2.ClampMagnitude(offset * 10f - body.linearVelocity * 5f,
            RecoveryAcceleration) * body.mass);
        body.AddTorque((angle * Mathf.Deg2Rad * 10f - body.angularVelocity * Mathf.Deg2Rad * 5f) * body.inertia);
        if (offset.sqrMagnitude < 0.0004f && Mathf.Abs(angle) < 1f && body.linearVelocity.sqrMagnitude < 0.0025f)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.bodyType = RigidbodyType2D.Kinematic;
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            IsReacting = false;
        }
    }
}
