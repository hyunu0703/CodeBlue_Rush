using UnityEngine;

/// <summary>기존 차선의 거리와 직접 연결을 따라 주행하고 앞 차량과 간격을 유지한다</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public sealed class VehicleAI : MonoBehaviour
{
    [SerializeField, Min(0f)] private float cruiseSpeed = 5f;
    [SerializeField, Min(0.1f)] private float acceleration = 3f;
    [SerializeField, Min(0.1f)] private float braking = 8f;
    [SerializeField, Min(1f)] private float turnSpeed = 360f;
    [SerializeField, Min(3f)] private float sensorDistance = 7f;
    private TrafficSpawner owner;
    private Rigidbody2D body;
    private CircleCollider2D shape;
    private int choice;
    internal int Slot { get; set; } = -1;
    public TrafficLane Lane { get; private set; }
    public float Distance { get; private set; }
    public float Speed { get; private set; }
    public TrafficLane NextLane => owner ? Following(owner.Map, Lane, choice) : null;
    internal Collider2D Shape => shape;

    // 차량의 물리 참조를 한 번 캐싱한다
    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        shape = GetComponent<CircleCollider2D>();
    }

    // 외부 비활성화도 생성 책임에 알려 참조를 즉시 정리한다
    private void OnDisable()
    {
        if (owner)
            owner.Release(this);
    }

    // 풀에서 꺼낸 차량을 차선 방향에 맞추어 초기화한다
    internal void Place(TrafficSpawner source, TrafficLane lane, float distance, int routeChoice)
    {
        owner = source;
        Lane = lane;
        Distance = distance;
        choice = routeChoice;
        Speed = 0f;
        lane.TrySample(distance, out Vector3 position, out Vector3 direction);
        float rotation = Vector2.SignedAngle(Vector2.up, direction);
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, rotation));
        body.position = position;
        body.rotation = rotation;
    }

    // 재사용 전에 이전 도시와 차선 참조를 비운다
    internal void Clear()
    {
        owner = null;
        Lane = null;
        Distance = 0f;
        Speed = 0f;
        Slot = -1;
    }

    // 현재 도시에 속한 정방향 연결만 반환한다
    internal static TrafficLane Following(CityMap map, TrafficLane lane, int selection)
    {
        if (!map || !map.ContainsLane(lane) || lane.NextCount == 0)
            return null;
        TrafficLane next = lane.GetNext((int)((uint)selection % (uint)lane.NextCount));
        return map.ContainsLane(next) && next.Length > 0.01f && Vector3.Distance(lane.EndPoint, next.StartPoint) < RoadConnection.PositionTolerance && Vector3.Dot(lane.EndDirection, next.StartDirection) > 0.98f ? next : null;
    }

    // 짧은 미래 경로의 위치를 기존 차선 데이터에서 직접 얻는다
    private bool Ahead(float travel, out Vector3 point, out TrafficLane lane, out float along)
    {
        lane = Lane;
        along = Distance + travel;
        point = transform.position;
        for (int step = 0; step < 8; step++)
        {
            if (!owner.Map.ContainsLane(lane))
                return false;
            if (along <= lane.Length)
                return lane.TrySample(along, out point, out _);
            along -= lane.Length;
            lane = Following(owner.Map, lane, choice);
        }
        return false;
    }

    // 전방의 짧은 경로만 물리 조회하여 안전 이동 거리를 계산한다
    private float Clearance()
    {
        float range = Mathf.Max(sensorDistance, Speed * Speed / (2f * braking) + 1f);
        range = Mathf.Min(range, 20f);
        for (float probe = 0.25f; probe <= range; probe += 0.25f)
        {
            if (!Ahead(probe, out Vector3 point, out TrafficLane lane, out _))
                return Mathf.Max(0f, probe - 0.25f);
            if (owner.HasLeaderAt(point, this, lane))
                return Mathf.Max(0f, probe - 0.25f);
        }
        return range;
    }

    // 물리 주기마다 선속도와 실제 차선상의 위치만 갱신한다
    private void FixedUpdate()
    {
        if (!owner)
            return;
        if (!owner.Map || !owner.Map.ContainsLane(Lane) || !NextLane)
        {
            owner.Release(this);
            return;
        }
        float clear = Clearance();
        float targetSpeed = Mathf.Min(cruiseSpeed, Mathf.Sqrt(2f * braking * clear));
        Speed = Mathf.MoveTowards(Speed, targetSpeed, (targetSpeed < Speed ? braking : acceleration) * Time.fixedDeltaTime);
        float travel = Mathf.Min(Speed * Time.fixedDeltaTime, clear);
        if (clear <= 0f)
            Speed = 0f;
        Distance += travel;
        int transitions = 0;
        while (Distance > Lane.Length && transitions++ < 8)
        {
            float remaining = Distance - Lane.Length;
            TrafficLane next = NextLane;
            if (!next)
            {
                owner.Release(this);
                return;
            }
            Lane = next;
            Distance = remaining;
        }
        if (Distance > Lane.Length || !Lane.TrySample(Distance, out Vector3 point, out Vector3 direction))
        {
            owner.Release(this);
            return;
        }
        body.MovePosition(point);
        float angle = Vector2.SignedAngle(Vector2.up, direction);
        body.MoveRotation(Mathf.MoveTowardsAngle(body.rotation, angle, turnSpeed * Time.fixedDeltaTime));
    }
}
