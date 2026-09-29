using UnityEngine;

/// <summary>차량 조향 상태에 맞춰 UI 핸들의 회전과 복귀를 처리한다.</summary>
public class SteeringWheelUI : MonoBehaviour
{
    [SerializeField] private AmbulanceController controller;
    [SerializeField] private float maxRotation = 120f;
    [SerializeField] private float rotationSpeed = 180f;
    [SerializeField] private float returnSpeed = 100f;

    private RectTransform rect;
    private float angle;

    // UI 핸들의 RectTransform을 가져온다
    private void Awake()
    {
        rect = (RectTransform)transform;
    }

    // 조향 상태에 따라 핸들을 제한된 범위 안에서 회전시킨다
    private void LateUpdate()
    {
        if (!controller) return;

        float targetAngle = -controller.SteerRate * maxRotation;
        float speed = controller.IsSteering ? rotationSpeed : returnSpeed;

        angle = Mathf.MoveTowards(angle, targetAngle, speed * Time.deltaTime);
        rect.localRotation = Quaternion.Euler(0f, 0f, angle);
    }
}