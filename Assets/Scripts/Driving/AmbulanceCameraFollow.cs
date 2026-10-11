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
        smoothVelocity = Vector3.zero;
        smoothedOffset = offset;
        RestoreUprightRotation();
        ResolveMapGenerator();
    }

    /// <summary>비활성화 후 다시 사용할 때 이전 교차로의 회전 상태가 남지 않게 한다.</summary>
    private void OnDisable()
    {
        RestoreUprightRotation();
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

        // 회전과 위치를 같은 프레임에 계산한다. 차량 중심이 기존 화면 좌표를 벗어나도록 하는 위치 추적 지연을 제거한다.
        smoothedOffset = Vector3.SmoothDamp(smoothedOffset, offset,
            ref smoothVelocity, smoothTime, Mathf.Infinity, Time.deltaTime);
        // 기준 방향 대비 카메라 회전만큼 오프셋도 회전시켜 차량을 중심으로 카메라를 돌린다.
        var relativeRotation = transform.rotation * Quaternion.Inverse(uprightRotation);
        transform.position = target.position + relativeRotation * smoothedOffset;
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
    }
}
