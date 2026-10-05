using UnityEngine;

/// <summary>현재 도시의 보도 점을 걷고 신호에 따라 횡단한다</summary>
[DisallowMultipleComponent]
public sealed class CitizenAI : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float speed = 1.5f;
    [SerializeField, Min(1f)] private float jaywalkWait = 6f;
    [SerializeField] private SpriteRenderer skin;
    private CitizenSpawner owner;
    private SidewalkPath path;
    private SidewalkPath across;
    private VehicleStopZone crosswalk;
    private Vector3 target;
    private Vector3 joinPoint;
    private int pointIndex;
    private int step;
    private int stage;
    private bool canJaywalk;
    private float waited;
    internal int Slot { get; set; } = -1;
    internal SidewalkPath Path => path;
    internal bool IsCrossing => stage >= 3;

    // 풀에서 다시 생성될 때도 같은 Renderer의 외형만 교체한다
    internal void SetSprite(Sprite sprite)
    {
        if (skin && sprite)
            skin.sprite = sprite;
    }

    // 풀에서 꺼낸 시민의 이전 경로와 횡단 상태를 초기화한다
    internal void Place(CitizenSpawner source, SidewalkPath route, Vector3 position, int segment, int direction, bool jaywalk)
    {
        owner = source;
        path = route;
        transform.position = position;
        step = direction;
        pointIndex = direction > 0 ? segment + 1 : segment;
        stage = 0;
        across = null;
        crosswalk = null;
        canJaywalk = jaywalk;
        waited = 0f;
    }

    // 풀 반환 시 도시와 횡단 참조를 모두 비운다
    internal void Clear()
    {
        owner = null;
        path = null;
        across = null;
        crosswalk = null;
        stage = 0;
        waited = 0f;
        Slot = -1;
    }

    // 외부 비활성화도 소유자에게 알린다
    private void OnDisable()
    {
        if (owner)
            owner.Release(this);
    }

    // 보도와 횡단 위치 사이를 연속적으로 이동한다
    private void Update()
    {
        if (!owner || !owner.Map || !owner.Map.ContainsSidewalkPath(path) || (stage > 0 && (!owner.Map.ContainsSidewalkPath(across) || !crosswalk || !crosswalk.Signal || !crosswalk.CrosswalkStart || !crosswalk.CrosswalkEnd || !crosswalk.PedestrianWaitPointA || !crosswalk.PedestrianWaitPointB)))
        {
            if (owner) owner.Release(this);
            return;
        }
        if (stage == 0)
        {
            if (pointIndex >= 0 && pointIndex < path.PointCount)
            {
                if (Move(path.GetPoint(pointIndex))) pointIndex += step;
                return;
            }
            AtEnd();
            return;
        }
        if (stage == 1)
        {
            if (Move(target)) stage = 2;
            return;
        }
        if (stage == 2)
        {
            if (owner.SirenNearby(transform.position) || (!crosswalk.Signal.CanPedestrianCross(crosswalk) && !(canJaywalk && (waited += Time.deltaTime) >= jaywalkWait)) || !owner.CrosswalkClear(crosswalk))
                return;
            stage = 3;
            target = owner.NearCrosswalkEnd(crosswalk, transform.position, true);
            return;
        }
        if (Move(target))
        {
            if (stage == 3)
            {
                stage = 4;
                target = owner.NearCrosswalkEnd(crosswalk, transform.position, false);
            }
            else if (stage == 4)
            {
                stage = 5;
                target = owner.NearWaitPoint(crosswalk, joinPoint);
            }
            else if (stage == 5)
            {
                stage = 6;
                target = joinPoint;
            }
            else
                JoinAcross();
        }
    }

    // 한 프레임에 목표를 지나치지 않도록 이동한다
    private bool Move(Vector3 point)
    {
        transform.position = Vector3.MoveTowards(transform.position, point, speed * Time.deltaTime);
        return (transform.position - point).sqrMagnitude < 0.0001f;
    }

    // 실제 보도 끝점에서만 연결 경로 또는 횡단보도를 선택한다
    private void AtEnd()
    {
        Vector3 end = path.GetPoint(step > 0 ? path.PointCount - 1 : 0);
        VehicleStopZone zone = path.Crosswalk;
        SidewalkPath opposite = path.Across;
        bool canCross = zone && zone.Signal && zone.CrosswalkStart && zone.CrosswalkEnd && zone.PedestrianWaitPointA && zone.PedestrianWaitPointB && opposite && owner.Map.ContainsSidewalkPath(opposite) && opposite.PointCount >= 2;
        if (canCross)
        {
            Vector3 wait = owner.NearWaitPoint(zone, end);
            canCross = (wait - end).sqrMagnitude < 4f;
            if (canCross && (path.NextCount == 0 || owner.Roll(0.4f)))
            {
                crosswalk = zone;
                across = opposite;
                joinPoint = Vector3.SqrMagnitude(opposite.GetPoint(0) - owner.FarWaitPoint(zone, wait)) < Vector3.SqrMagnitude(opposite.GetPoint(opposite.PointCount - 1) - owner.FarWaitPoint(zone, wait)) ? opposite.GetPoint(0) : opposite.GetPoint(opposite.PointCount - 1);
                target = wait;
                waited = 0f;
                stage = 1;
                return;
            }
        }
        int count = path.NextCount;
        for (int attempt = 0; attempt < count; attempt++)
        {
            SidewalkPath next = path.GetNext((owner.Next(count) + attempt) % count);
            if (!owner.Map.ContainsSidewalkPath(next) || next.PointCount < 2)
                continue;
            if ((next.GetPoint(0) - end).sqrMagnitude < 0.01f)
            {
                path = next;
                step = 1;
                pointIndex = 1;
                return;
            }
            if ((next.GetPoint(next.PointCount - 1) - end).sqrMagnitude < 0.01f)
            {
                path = next;
                step = -1;
                pointIndex = next.PointCount - 2;
                return;
            }
        }
        step = -step;
        pointIndex = step > 0 ? 1 : path.PointCount - 2;
    }

    // 반대편 보도 끝점에 도착한 뒤 같은 경로를 계속 걷는다
    private void JoinAcross()
    {
        path = across;
        step = (path.GetPoint(0) - joinPoint).sqrMagnitude < 0.01f ? 1 : -1;
        pointIndex = step > 0 ? 1 : path.PointCount - 2;
        across = null;
        crosswalk = null;
        stage = 0;
    }
}
