using UnityEngine;

/// <summary>도로 방향에 맞춰 구급차를 추적하고 제동 속도에 따른 약한 카메라 반동을 적용한다.</summary>
[DisallowMultipleComponent]
public sealed class AmbulanceCameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset;
    [SerializeField, Min(0.01f)] private float smoothTime = 0.15f;
    [SerializeField, Min(0.01f)] private float rotationSmoothTime = 0.5f; // 교차로의 차량 방향과 출구 도로 방향 사이를 이어주는 회전 완화 시간이다. 단위는 초다.
    [SerializeField, Min(0.01f)] private float maximumRotationSpeed = 75f; // 급조향 시 화면 회전을 제한하는 최대 속도다. 단위는 도/초다.
    // 미지정이면 같은 씬의 단일 맵 생성기를 활성화 시 조회한다. 다른 씬의 교차로는 사용하지 않는다.
    [SerializeField] private MissionMapGenerator mapGenerator;
    [SerializeField, Min(0f)] private float maximumBrakeOffset = 0.16f;        // 최고 속도에서 화면 위쪽으로 이동하는 최대 거리다. 단위는 월드 유닛이다.
    [SerializeField, Min(0.1f)] private float brakeMotionDuration = 2.2f;      // 최고 속도에서 원위치로 복귀하기까지의 시간이다. 단위는 초다.
    [SerializeField, Min(0.1f)] private float minimumBrakeMotionDuration = 0.6f; // 저속 제동의 왕복 시간 하한이다. 단위는 초다.
    private const float ForwardPeakTime = 0.35f; // 전체 시간의 35%에 위쪽 정점에 도달하고 나머지 65% 동안 복귀한다.
    // 한 페달 입력에 한 번 왕복한다. 진행 중 재입력은 중첩하지 않으며 코루틴/프레임별 할당이 없다.
    private AmbulanceDriveController brakeVehicle;
    private Transform cachedBrakeTarget;
    private bool canStartBrakeEffect = true;
    private float brakeDisplacement;
    private float brakeMotionElapsed;
    private float brakeMotionAmplitude;
    private float activeBrakeMotionDuration; // 연출 시작 속도로 결정한 왕복 시간이다. 감속 중에도 유지한다.
    private Vector3 previousTargetPosition;

    private Vector3 smoothVelocity;
    // 차량과 카메라 사이의 기준 오프셋이다. 차량 이동을 지연시키지 않고 Inspector 오프셋 변경만 완화한다.
    private Vector3 smoothedOffset;
    // 씬 기준 회전에 대한 현재/목표 Z 회전과 각속도다. 구역 경계에서도 연속으로 유지한다.
    private Quaternion uprightRotation;
    private float intersectionAngle;
    private float desiredAngle;
    private float rotationVelocity;
    private BoxCollider2D targetBody; // 차체 중심이 중앙선을 넘었는지 판단하는 구급차 충돌 영역이다.
    private bool centerLineRotation;  // 직선에서 중앙선을 넘은 뒤 차량 방향을 따라가는 상태다.

    /// <summary>씬에 저장된 원래 화면 방향을 인스턴스 수명 동안 보관한다.</summary>
    private void Awake()
    {
        uprightRotation = transform.rotation;
    }

    private void OnEnable()
    {
        // 잘못 저장된 이동량/시간으로 화면 위치를 오염시키지 않는다.
        if (float.IsNaN(maximumBrakeOffset) || float.IsInfinity(maximumBrakeOffset) || maximumBrakeOffset < 0f
            || float.IsNaN(brakeMotionDuration) || float.IsInfinity(brakeMotionDuration) || brakeMotionDuration < 0.1f
            || float.IsNaN(minimumBrakeMotionDuration) || float.IsInfinity(minimumBrakeMotionDuration)
            || minimumBrakeMotionDuration < 0.1f || minimumBrakeMotionDuration > brakeMotionDuration)
        {
            Debug.LogError("제동 카메라의 최대 이동량은 0 이상, 최소 왕복 시간은 0.1초 이상이며 최대 왕복 시간 이하여야 합니다.", this);
            enabled = false;
            return;
        }
        smoothVelocity = Vector3.zero;
        smoothedOffset = offset;
        RestoreUprightRotation();
        ResolveMapGenerator();
        ResetBrakeEffect();
    }

    /// <summary>비활성화 후 다시 사용할 때 이전 교차로의 회전 상태가 남지 않게 한다.</summary>
    private void OnDisable()
    {
        RestoreUprightRotation();
        ResetBrakeEffect();
        if (target != null)
        {
            transform.position = target.position + smoothedOffset;
        }
    }

    private void Start()
    {
        if (target == null)
        {
            Debug.LogError("AmbulanceCameraFollow requires its Ambulance target.", this);
            enabled = false;
        }
    }

    private void LateUpdate()
    {
        // 추적 대상이 파괴되면 마지막 교차로의 카메라 방향을 남기지 않는다.
        if (target == null)
        {
            RestoreUprightRotation();
            return;
        }
        if (Time.timeScale <= 0f)
        {
            return;
        }

        UpdateIntersectionRotation();
        UpdateBrakeEffect();

        // 기본 추적 위치와 별도로 위쪽 이동량을 더해 제동 중 차량의 화면 위치가 달라지게 한다.
        smoothedOffset = Vector3.SmoothDamp(smoothedOffset, offset,
            ref smoothVelocity, smoothTime, Mathf.Infinity, Time.deltaTime);
        // 기준 방향 대비 카메라 회전만큼 오프셋도 회전시켜 차량을 중심으로 카메라를 돌린다.
        var relativeRotation = transform.rotation * Quaternion.Inverse(uprightRotation);
        // 주행 방향이나 후진 여부와 관계없이 현재 화면 위쪽으로 이동한다.
        var brakeOffset = transform.up * brakeDisplacement;
        transform.position = target.position + relativeRotation * smoothedOffset + brakeOffset;
    }

    // 제동 입력마다 화면 위쪽 이동과 원래 추적 위치로의 복귀를 한 번 실행한다.
    private void UpdateBrakeEffect()
    {
        if (cachedBrakeTarget != target || Vector3.Distance(previousTargetPosition, target.position) > 3f)
        {
            // 미션 순간이동/대상 교체 때 이전 주행의 카메라 관성을 가져가지 않는다.
            ResetBrakeEffect();
        }
        previousTargetPosition = target.position;
        if (brakeVehicle == null || !brakeVehicle.IsReady)
        {
            brakeDisplacement = 0f;
            brakeMotionElapsed = activeBrakeMotionDuration;
            brakeMotionAmplitude = 0f;
            canStartBrakeEffect = true;
            return;
        }
        if (brakeVehicle.BrakePressure < 0.1f)
        {
            canStartBrakeEffect = true;
        }
        if (canStartBrakeEffect && brakeVehicle.BrakePressure >= 0.2f && brakeVehicle.BrakingDeceleration > 0.5f
            && brakeVehicle.NormalizedSpeed > 0.1f)
        {
            // 진행 중 재입력은 소비만 하여 이동 방향/위치가 갑자기 바뀌거나 나중에 재실행되지 않게 한다.
            canStartBrakeEffect = false;
            if (brakeMotionElapsed >= activeBrakeMotionDuration)
            {
                // 감속 중 시간을 다시 계산하면 복귀가 급해지므로 시작 속도를 고정해 사용한다.
                var speedRatio = brakeVehicle.NormalizedSpeed;
                brakeMotionAmplitude = maximumBrakeOffset * speedRatio * brakeVehicle.BrakePressure;
                activeBrakeMotionDuration = Mathf.Lerp(minimumBrakeMotionDuration, brakeMotionDuration, speedRatio);
                brakeMotionElapsed = 0f;
            }
        }
        // 긴 프레임도 정규화 시간으로 처리한다. 일시정지는 LateUpdate에서 시간 진행을 중단한다.
        brakeMotionElapsed = Mathf.Min(activeBrakeMotionDuration, brakeMotionElapsed + Time.deltaTime);
        brakeDisplacement = brakeMotionAmplitude * EvaluateBrakeMotion(brakeMotionElapsed / activeBrakeMotionDuration);
    }

    // 위쪽 정점에서 천천히 방향을 바꾸고 원래 위치를 지나치지 않는 왕복 곡선을 계산한다.
    internal static float EvaluateBrakeMotion(float normalizedTime)
    {
        var progress = Mathf.Clamp01(normalizedTime);
        if (progress <= ForwardPeakTime)
        {
            return SmoothBrakeProgress(progress / ForwardPeakTime);
        }
        return 1f - SmoothBrakeProgress((progress - ForwardPeakTime) / (1f - ForwardPeakTime));
    }

    /// <summary>양 끝의 속도와 가속도를 0으로 만드는 5차 곡선으로 시작/방향 전환/정지의 급격한 움직임을 줄인다.</summary>
    private static float SmoothBrakeProgress(float progress)
    {
        return progress * progress * progress * (10f + progress * (-15f + 6f * progress));
    }

    /// <summary>활성 수명/대상 교체/순간이동마다 참조와 반동을 초기화한다. 타깃의 주행 컴포넌트는 선택적이다.</summary>
    private void ResetBrakeEffect()
    {
        cachedBrakeTarget = target;
        targetBody = target != null ? target.GetComponent<BoxCollider2D>() : null;
        centerLineRotation = false;
        brakeVehicle = target != null ? target.GetComponent<AmbulanceDriveController>() : null;
        previousTargetPosition = target != null ? target.position : Vector3.zero;
        brakeDisplacement = 0f;
        activeBrakeMotionDuration = brakeMotionDuration;
        brakeMotionElapsed = activeBrakeMotionDuration;
        brakeMotionAmplitude = 0f;
        canStartBrakeEffect = true;
    }

    /// <summary>교차로·바리케이드 앞과 중앙선을 절반 넘은 직선에서는 차량 방향을 따르고 정상 차선에서는 도로에 정렬한다.</summary>
    private void UpdateIntersectionRotation()
    {
        if (mapGenerator != null)
        {
            if (mapGenerator.IsInsideCameraTurnArea(target.position))
            {
                centerLineRotation = false;
                // 2D 차량의 로컬 위쪽이 전방이다.
                desiredAngle = Mathf.DeltaAngle(uprightRotation.eulerAngles.z, target.eulerAngles.z);
            }
            else if (mapGenerator.TryGetStraightRoadAxis(
                targetBody != null ? targetBody.transform.TransformPoint(targetBody.offset) : target.position,
                out var roadAxis, out var lateralPosition))
            {
                centerLineRotation = ShouldFollowAcrossCenterLine(lateralPosition,
                    Vector3.Dot(target.up, roadAxis), centerLineRotation);
                if (centerLineRotation)
                {
                    desiredAngle = Mathf.DeltaAngle(uprightRotation.eulerAngles.z, target.eulerAngles.z);
                }
                else
                {
                    var roadAngle = Mathf.Atan2(-roadAxis.x, roadAxis.y) * Mathf.Rad2Deg;
                    var relativeRoadAngle = Mathf.DeltaAngle(uprightRotation.eulerAngles.z, roadAngle);
                    // 양방향 도로에서 직전 시점과 가까운 방향을 유지한다. 직선에서 조향해도 화면이 뒤집히지 않는다.
                    if (Mathf.Abs(Mathf.DeltaAngle(desiredAngle, relativeRoadAngle)) > 90f)
                    {
                        relativeRoadAngle = Mathf.DeltaAngle(0f, relativeRoadAngle + 180f);
                    }
                    desiredAngle = relativeRoadAngle;
                }
            }
            else centerLineRotation = false;
        }

        // 구역을 나가도 회전 각속도를 초기화하지 않는다. 맵 밖에서는 마지막 도로 방향을 유지한다.
        intersectionAngle = Mathf.SmoothDampAngle(intersectionAngle, desiredAngle, ref rotationVelocity,
            rotationSmoothTime, maximumRotationSpeed, Time.deltaTime);
        transform.rotation = uprightRotation * Quaternion.Euler(0f, 0f, intersectionAngle);
    }

    // 차체 중심이 진행 방향의 반대 차선에 있으면 추적한다. 도로와 수직에 가까울 때는 직전 상태를 유지한다.
    internal static bool ShouldFollowAcrossCenterLine(float lateralPosition, float forwardAlongRoad, bool wasFollowing)
    {
        if (Mathf.Abs(forwardAlongRoad) < 0.1f) return wasFollowing;
        return lateralPosition * forwardAlongRoad < 0f;
    }

    /// <summary>원래 카메라 방향으로 복귀하고 이전 회전의 관성을 비운다.</summary>
    private void RestoreUprightRotation()
    {
        intersectionAngle = 0f;
        desiredAngle = 0f;
        rotationVelocity = 0f;
        centerLineRotation = false;
        transform.rotation = uprightRotation;
    }

    /// <summary>같은 씬에 있는 단일 맵 생성기를 캐시한다. 프레임마다 씬 오브젝트를 검색하지 않는다.</summary>
    private void ResolveMapGenerator()
    {
        if (mapGenerator != null && mapGenerator.gameObject.scene == gameObject.scene)
        {
            return;
        }
        mapGenerator = null;
        foreach (var root in gameObject.scene.GetRootGameObjects())
        {
            foreach (var candidate in root.GetComponentsInChildren<MissionMapGenerator>(true))
            {
                if (mapGenerator != null)
                {
                    Debug.LogError("같은 씬에 MissionMapGenerator가 여러 개입니다. 카메라의 Map Generator를 하나로 지정해주세요.", this);
                    mapGenerator = null;
                    return;
                }
                mapGenerator = candidate;
            }
        }
    }

    public void Configure(Transform target)
    {
        this.target = target;
        offset = transform.position - target.position;
        smoothVelocity = Vector3.zero;
        smoothedOffset = offset;
        ResetBrakeEffect();
    }
}
