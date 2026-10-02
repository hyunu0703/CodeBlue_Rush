using UnityEngine;

/// <summary>교차로 안에서만 카메라가 구급차 방향을 따라가도록 제어한다.</summary>
[RequireComponent(typeof(Collider2D))]
public class CameraTurnZone : MonoBehaviour
{
    [SerializeField] private AmbulanceCamera cameraController;

    // 생성된 교차로에 카메라를 한 번 명시적으로 연결한다
    public void Bind(AmbulanceCamera camera)
    {
        if (cameraController)
            cameraController.ExitTurnZone(this);
        cameraController = camera;
    }

    // 도시 해제 시 카메라에 이전 영역을 남기지 않는다
    private void OnDisable()
    {
        if (cameraController)
            cameraController.ExitTurnZone(this);
    }

    // 구급차 진입 시 차량 방향 추적을 시작한다
    private void OnTriggerEnter2D(Collider2D other)
    {
        Rigidbody2D body = other.attachedRigidbody;

        if (!cameraController || !cameraController.isActiveAndEnabled || !body || !body.CompareTag("Ambulance")) return;

        cameraController.EnterTurnZone(this);
    }

    // 구급차 탈출 방향에 맞춰 카메라를 고정한다
    private void OnTriggerExit2D(Collider2D other)
    {
        Rigidbody2D body = other.attachedRigidbody;

        if (!cameraController || !cameraController.isActiveAndEnabled || !body || !body.CompareTag("Ambulance")) return;

        cameraController.ExitTurnZone(this);
    }
}
