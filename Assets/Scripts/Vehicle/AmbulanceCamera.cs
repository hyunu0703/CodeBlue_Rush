using UnityEngine;

/// <summary>구급차를 추적하고 교차로에서만 차량 방향을 따라 회전한다.</summary>
public class AmbulanceCamera : MonoBehaviour
{
    [SerializeField] private Rigidbody2D target;
    [SerializeField] private float maxSpeed = 12f;
    [SerializeField] private float maxOffset = 3f;
    [SerializeField] private float moveSmoothTime = 0.2f;
    [SerializeField] private float rotateSmoothTime = 0.3f;
    [SerializeField] private float baseOffset = 1.5f;
    [SerializeField] private CityMap map;

    private CameraTurnZone activeZone;
    private Vector3 moveVelocity;
    private float rotateVelocity;
    private float targetAngle;
    private bool followRotation;

    // 현재 카메라 각도를 초기 고정 방향으로 저장한다
    private void Awake()
    {
        targetAngle = transform.eulerAngles.z;
    }

    // 지정된 맵 또는 주 카메라의 생성 등록에 참여한다
    private void OnEnable()
    {
        if (map)
            map.BindCamera(this);
    }

    // 카메라 해제 시 현재 교차로 추적을 종료한다
    private void OnDisable()
    {
        if (map)
            map.BindCamera(null);
        activeZone = null;
        followRotation = false;
    }

    // 차량 이동 후 위치와 방향을 갱신한다
    private void LateUpdate()
    {
        if (!target) return;

        FollowPosition();
        FollowRotation();
    }

    // 속도에 따라 차량 앞쪽을 부드럽게 추적한다
    private void FollowPosition()
    {
        float speedRate = Mathf.Clamp01(target.linearVelocity.magnitude / maxSpeed);
        float offset = baseOffset + maxOffset * speedRate;

        Vector3 targetPos = target.transform.position + transform.up * offset;

        targetPos.z = transform.position.z;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPos,
            ref moveVelocity,
            moveSmoothTime);
    }

    // 교차로에서는 차량 방향을 따라가고 평소에는 지정 각도를 유지한다
    private void FollowRotation()
    {
        float angle = followRotation ? target.rotation : targetAngle;

        angle = Mathf.SmoothDampAngle(
            transform.eulerAngles.z,
            angle,
            ref rotateVelocity,
            rotateSmoothTime);

        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    // 교차로 진입 시 차량 방향 추적을 시작한다
    public void EnterTurnZone(CameraTurnZone zone)
    {
        activeZone = zone;
        followRotation = true;
    }

    // 교차로 탈출 시 차량 방향과 가장 가까운 90도로 카메라를 고정한다
    public void ExitTurnZone(CameraTurnZone zone)
    {
        if (activeZone != zone) return;

        targetAngle = Mathf.Round((target ? target.rotation : transform.eulerAngles.z) / 90f) * 90f;
        followRotation = false;
        activeZone = null;
    }
}
