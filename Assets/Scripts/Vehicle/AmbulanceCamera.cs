using UnityEngine;

/// <summary>구급차를 추적하고 속도에 따라 진행 방향으로 카메라 위치를 이동한다.</summary>
public class AmbulanceCamera : MonoBehaviour
{
    [SerializeField] private Rigidbody2D target;
    [SerializeField] private float maxSpeed = 12f;
    [SerializeField] private float maxOffset = 3f;
    [SerializeField] private float smoothTime = 0.25f;

    private Vector3 velocity;

    // 차량 이동 후 카메라 위치를 갱신한다
    private void LateUpdate()
    {
        if (!target) return;

        float speedRate = Mathf.Clamp01(target.linearVelocity.magnitude / maxSpeed);
        Vector2 offset = (Vector2)target.transform.up * (maxOffset * speedRate);

        Vector3 targetPos = target.transform.position + (Vector3)offset;
        targetPos.z = transform.position.z;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPos,
            ref velocity,
            smoothTime);
    }
}