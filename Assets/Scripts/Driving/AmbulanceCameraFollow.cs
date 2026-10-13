using UnityEngine;

[DisallowMultipleComponent]
public sealed class AmbulanceCameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset;
    [SerializeField, Min(0.01f)] private float smoothTime = 0.15f;
    // 교차로의 차량 방향과 출구 도로 방향 사이를 이어주는 회전 완화 시간이다. 단위는 초다.
    [SerializeField, Min(0.01f)] private float rotationSmoothTime = 0.15f;
    // 미지정이면 같은 씬의 단일 맵 생성기를 활성화 시 조회한다. 다른 씬의 교차로는 사용하지 않는다.
    [SerializeField] private MissionMapGenerator mapGenerator;
    // 제동 카메라의 최대 전방 이동량은 월드 유닛, 전체 왕복 시간은 초 기준이다.
    [SerializeField, Min(0f)] private float maximumBrakeOffset = 0.45f;
    [SerializeField, Min(0.1f)] private float brakeMotionDuration = 2.2f;
    // 전체 시간의 30%에 전방 정점, 70%에 작은 후방 정점에 도달한다.
    private const float ForwardPeakTime = 0.3f;
    private const float BackwardPeakTime = 0.7f;
    private const float BackwardOffsetRatio = 0.2f;
    // 한 페달 입력에 한 번 왕복한다. 진행 중 재입력은 중첩하지 않으며 코루틴/프레임별 할당이 없다.
    private AmbulanceDriveController brakeVehicle;
    private Transform cachedBrakeTarget;
    private bool canStartBrakeEffect = true;
    private float brakeDisplacement;
    private float brakeMotionElapsed;
    private float brakeMotionAmplitude;
    private Vector3 brakeDirection;
    private Vector3 previousTargetPosition;

    private Vector3 smoothVelocity;
    // 차량과 카메라 사이의 기준 오프셋이다. 차량 이동을 지연시키지 않고 Inspector 오프셋 변경만 완화한다.
    private Vector3 smoothedOffset;
    // 씬 기준 회전에 대한 현재/목표 Z 회전과 각속도다. 구역 경계에서도 연속으로 유지한다.
    private Quaternion uprightRotation;
    private float intersectionAngle;
    private float desiredAngle;
    private float rotationVelocity;

    /// <summary>씬에 저장된 원래 화면 방향을 인스턴스 수명 동안 보관한다.</summary>
    private void Awake()
    {
        uprightRotation = transform.rotation;
    }

    private void OnEnable()
    {
        // 잘못 저장된 이동량/시간으로 화면 위치를 오염시키지 않는다.
        if (float.IsNaN(maximumBrakeOffset) || float.IsInfinity(maximumBrakeOffset) || maximumBrakeOffset < 0f
            || float.IsNaN(brakeMotionDuration) || float.IsInfinity(brakeMotionDuration) || brakeMotionDuration < 0.1f)
        {
            Debug.LogError("제동 카메라의 최대 이동량은 0 이상, 왕복 시간은 0.1초 이상이어야 합니다.", this);
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

        // 회전과 위치를 같은 프레임에 계산한다. 차량 중심이 기존 화면 좌표를 벗어나도록 하는 위치 추적 지연을 제거한다.
        smoothedOffset = Vector3.SmoothDamp(smoothedOffset, offset,
            ref smoothVelocity, smoothTime, Mathf.Infinity, Time.deltaTime);
        // 기준 방향 대비 카메라 회전만큼 오프셋도 회전시켜 차량을 중심으로 카메라를 돌린다.
        var relativeRotation = transform.rotation * Quaternion.Inverse(uprightRotation);
        transform.position = target.position + relativeRotation * smoothedOffset + brakeDirection * brakeDisplacement;
    }

    /// <summary>제동 입력에 전방 이동→작은 후방 이동→원위치 복귀를 한 번 실행한다. 각 정점에서 속도/가속도는 0이다.</summary>
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
            brakeMotionElapsed = brakeMotionDuration;
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
            if (brakeMotionElapsed >= brakeMotionDuration)
            {
                brakeDirection = brakeVehicle.Velocity.normalized;
                brakeMotionAmplitude = maximumBrakeOffset * brakeVehicle.NormalizedSpeed * brakeVehicle.BrakePressure;
                brakeMotionElapsed = 0f;
            }
        }
        // 긴 프레임도 정규화 시간으로 처리한다. 일시정지는 LateUpdate에서 시간 진행을 중단한다.
        brakeMotionElapsed = Mathf.Min(brakeMotionDuration, brakeMotionElapsed + Time.deltaTime);
        brakeDisplacement = brakeMotionAmplitude * EvaluateBrakeMotion(brakeMotionElapsed / brakeMotionDuration);
    }

    /// <summary>0~1 시간에서 최대 전방 이동량에 대한 비율을 반환한다. 세 구간 모두 5차 보간으로 부드럽게 연결한다.</summary>
    internal static float EvaluateBrakeMotion(float normalizedTime)
    {
        var progress = Mathf.Clamp01(normalizedTime);
        if (progress <= ForwardPeakTime)
        {
            return SmoothBrakeProgress(progress / ForwardPeakTime);
        }
        if (progress <= BackwardPeakTime)
        {
            return Mathf.Lerp(1f, -BackwardOffsetRatio,
                SmoothBrakeProgress((progress - ForwardPeakTime) / (BackwardPeakTime - ForwardPeakTime)));
        }
        return Mathf.Lerp(-BackwardOffsetRatio, 0f,
            SmoothBrakeProgress((progress - BackwardPeakTime) / (1f - BackwardPeakTime)));
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
        brakeVehicle = target != null ? target.GetComponent<AmbulanceDriveController>() : null;
        previousTargetPosition = target != null ? target.position : Vector3.zero;
        brakeDisplacement = 0f;
        brakeMotionElapsed = brakeMotionDuration;
        brakeMotionAmplitude = 0f;
        brakeDirection = Vector3.zero;
        canStartBrakeEffect = true;
    }

    /// <summary>교차로에서는 차량 방향을 따르고, 직선에서는 도로가 세로로 보이는 방향으로 부드럽게 정렬한다.</summary>
    private void UpdateIntersectionRotation()
    {
        if (mapGenerator != null)
        {
            if (mapGenerator.IsInsideIntersection(target.position))
            {
                // 2D 차량의 로컬 위쪽이 전방이다.
                desiredAngle = Mathf.DeltaAngle(uprightRotation.eulerAngles.z, target.eulerAngles.z);
            }
            else if (mapGenerator.TryGetStraightRoadAxis(target.position, out var roadAxis))
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

        // 구역을 나가도 회전 각속도를 초기화하지 않는다. 맵 밖에서는 마지막 도로 방향을 유지한다.
        intersectionAngle = Mathf.SmoothDampAngle(intersectionAngle, desiredAngle, ref rotationVelocity,
            rotationSmoothTime, Mathf.Infinity, Time.deltaTime);
        transform.rotation = uprightRotation * Quaternion.Euler(0f, 0f, intersectionAngle);
    }

    /// <summary>원래 카메라 방향으로 복귀하고 이전 회전의 관성을 비운다.</summary>
    private void RestoreUprightRotation()
    {
        intersectionAngle = 0f;
        desiredAngle = 0f;
        rotationVelocity = 0f;
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
