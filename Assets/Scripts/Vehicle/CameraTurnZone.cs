using UnityEngine;

/// <summary>교차로 안에서만 카메라가 구급차 방향을 따라가도록 제어한다.</summary>
[RequireComponent(typeof(Collider2D))]
public class CameraTurnZone : MonoBehaviour
{
    [SerializeField] private AmbulanceCamera cameraController;

    // 구급차 진입 시 차량 방향 추적을 시작한다
    private void OnTriggerEnter2D(Collider2D other)
    {
        Rigidbody2D body = other.attachedRigidbody;

        if (!body || !body.CompareTag("Ambulance")) return;

        cameraController.EnterTurnZone(this);
    }

    // 구급차 탈출 방향에 맞춰 카메라를 고정한다
    private void OnTriggerExit2D(Collider2D other)
    {
        Rigidbody2D body = other.attachedRigidbody;

        if (!body || !body.CompareTag("Ambulance")) return;

        cameraController.ExitTurnZone(this);
    }
}