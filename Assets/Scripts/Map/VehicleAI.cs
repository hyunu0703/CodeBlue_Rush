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
    [SerializeField, Min(0.5f)] private float changeDuration = 1.1f;
    [SerializeField, Min(1f)] private float changeCooldown = 6f;
    [SerializeField, Min(0.2f)] private float decisionInterval = 0.8f;
    [SerializeField, Min(2f)] private float frontGap = 4f;
    [SerializeField, Min(2f)] private float rearGap = 3f;
    [SerializeField, Range(0f, 1f)] private float cutInChance = 0.15f;
    [SerializeField, Min(5f)] private float cutInDistance = 18f;
    [SerializeField] private bool allowLaneChanges = true;
    [SerializeField, Range(0f, 1f)] private float signalViolationChance = 0.05f;
    private VehicleStopZone stopZone;
    private TrafficSignalController crossing;
    private bool violateSignal;
    private bool enteredIntersection;
    private float signalDistance = float.PositiveInfinity;
    private float changeProgress;
    private float nextChange;
    private float nextDecision;
    private float nextCutIn;
    public TrafficLane TargetLane { get; private set; }
    public bool IsChangingLane => TargetLane;
    public bool AllowLaneChanges { get => allowLaneChanges; set => allowLaneChanges = value; }
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
        TargetLane = null;
        changeProgress = 0f;
        nextChange = Time.time + 1f;
        nextCutIn = 0f;
        nextDecision = Time.time + 1f + (uint)routeChoice % 100 / 100f;
        lane.TrySample(distance, out Vector3 position, out Vector3 direction);
        float rotation = Vector2.SignedAngle(Vector2.up, direction);
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, rotation));
        body.position = position;
        body.rotation = rotation;
    }

    // 재사용 전에 이전 도시와 차선 참조를 비운다
    internal void Clear()
    {
        ClearSignal();
        TargetLane = null;
        changeProgress = 0f;
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

    // 풀 반환이나 교차로 이탈 시 이전 접근 결정을 지운다
    private void ClearSignal()
    {
        if (crossing)
            crossing.Leave(shape);
        stopZone = null;
        crossing = null;
        enteredIntersection = false;
        violateSignal = false;
        signalDistance = float.PositiveInfinity;
    }

    // 직접 연결된 짧은 경로만 따라 정지선까지 남은 거리를 제한한다
    private float SignalClearance()
    {
        if (enteredIntersection)
        {
            if (crossing && TrafficSignalController.ForLane(Lane) == crossing)
                return float.PositiveInfinity;
            ClearSignal();
        }
        TrafficLane ahead = Lane;
        float offset = -Distance;
        for (int hop = 0; ahead && hop < 8 && offset < 20f; hop++)
        {
            TrafficSignalController signal = TrafficSignalController.ForLane(ahead);
            VehicleStopZone zone = signal ? signal.Entry(ahead) : null;
            if (zone)
            {
                if (stopZone != zone)
                {
                    ClearSignal();
                    stopZone = zone;
                    crossing = signal;
                    violateSignal = owner.Roll(signalViolationChance);
                }
                float remaining = Mathf.Max(0f, offset + zone.StopDistance);
                signalDistance = remaining;
                TrafficLane route = Following(owner.Map, ahead, choice);
                bool commit = remaining <= Speed * Time.fixedDeltaTime + 0.05f && Lane == ahead;
                bool free = signal.TryEnter(this, zone, route, violateSignal, commit);
                if (free && commit)
                    enteredIntersection = true;
                return free ? float.PositiveInfinity : remaining;
            }
            offset += ahead.Length;
            ahead = Following(owner.Map, ahead, choice);
        }
        ClearSignal();
        return float.PositiveInfinity;
    }

    // 실제 인접 관계와 현재 지점의 같은 진행 방향만 허용한다
    private bool ProjectTarget(TrafficLane target, out float along, out Vector3 point)
    {
        along = 0f;
        point = default;
        if (!owner || !owner.Map.ContainsLane(Lane) || !owner.Map.ContainsLane(target) || (target != Lane.LeftLane && target != Lane.RightLane) || target.transform.parent != Lane.transform.parent)
            return false;
        if ((target == Lane.LeftLane && target.RightLane != Lane) || (target == Lane.RightLane && target.LeftLane != Lane))
            return false;
        Lane.TrySample(Distance, out Vector3 origin, out Vector3 direction);
        return target.TryProject(origin, out along, out float offset) && offset > 0.01f && offset < 36f && target.TrySample(along, out point, out Vector3 forward) && Vector3.Dot(direction, forward) > 0.98f;
    }

    // 앞뒤 공간과 남은 도로 길이를 확인한 뒤 한 번만 변경을 시작한다
    public bool TryChangeLane(TrafficLane target)
    {
        if (enteredIntersection || !allowLaneChanges || !isActiveAndEnabled || IsChangingLane || Time.time < nextChange || Speed < 0.5f || !ProjectTarget(target, out float along, out _) || !Following(owner.Map, target, choice))
            return false;
        float needed = Mathf.Max(cruiseSpeed, Speed) * changeDuration + 1f;
        if (signalDistance < needed + 2f || Lane.Length - Distance < needed || target.Length - along < needed || !owner.LaneSpaceSafe(this, target, along, frontGap, rearGap, changeDuration))
            return false;
        TargetLane = target;
        changeProgress = 0f;
        return true;
    }

    // 실제 구급차 차선과 접근 속도 및 예상 후방 간격을 확인한 후 확률을 적용한다
    public bool TryCutIn()
    {
        if (!owner || !allowLaneChanges || IsChangingLane || Time.time < nextChange || Time.time < nextCutIn)
            return false;
        nextCutIn = Time.time + decisionInterval;
        if (!owner.TryAmbulanceLane(out TrafficLane lane, out float ambulanceDistance, out float ambulanceSpeed) || ambulanceSpeed < 2f || ambulanceSpeed <= Speed + 0.2f || Speed < 0.5f || !ProjectTarget(lane, out float along, out _))
            return false;
        float gap = along - ambulanceDistance;
        if (gap <= rearGap || gap > cutInDistance || gap - (ambulanceSpeed - Speed) * changeDuration < rearGap)
            return false;
        if (!owner.LaneSpaceSafe(this, lane, along, frontGap, rearGap, changeDuration) || !owner.Roll(cutInChance))
            return false;
        return TryChangeLane(lane);
    }

    // 간헐적으로 접근 구급차 또는 앞차 정체가 있는 경우만 판단한다
    private void Decide(float clear)
    {
        if (!allowLaneChanges || IsChangingLane || Time.time < nextDecision)
            return;
        nextDecision = Time.time + decisionInterval;
        if (TryCutIn() || clear >= sensorDistance - 0.5f || clear < Speed * changeDuration + 0.5f)
            return;
        if (!TryChangeLane(Lane.LeftLane))
            TryChangeLane(Lane.RightLane);
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
        float clear = Mathf.Min(Clearance(), SignalClearance());
        Decide(clear);
        bool changing = IsChangingLane;
        float targetAlong = 0f;
        if (changing && !ProjectTarget(TargetLane, out targetAlong, out _))
        {
            owner.Release(this);
            return;
        }
        bool safe = !changing || owner.LaneSpaceSafe(this, TargetLane, targetAlong, frontGap, rearGap, Mathf.Max(0.2f, changeDuration - changeProgress));
        if (!safe)
            clear = 0f;
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
        if (changing)
        {
            if (!ProjectTarget(TargetLane, out targetAlong, out Vector3 targetPoint))
            {
                owner.Release(this);
                return;
            }
            if (safe && travel > 0f)
                changeProgress += Time.fixedDeltaTime;
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(changeProgress / changeDuration));
            point = Vector3.Lerp(point, targetPoint, blend);
            Vector3 motion = point - (Vector3)body.position;
            if (motion.sqrMagnitude > 0.000001f)
                direction = motion.normalized;
            if (blend >= 1f)
            {
                Lane = TargetLane;
                Distance = targetAlong;
                TargetLane = null;
                nextChange = Time.time + changeCooldown;
            }
        }
        body.MovePosition(point);
        float angle = Vector2.SignedAngle(Vector2.up, direction);
        body.MoveRotation(Mathf.MoveTowardsAngle(body.rotation, angle, turnSpeed * Time.fixedDeltaTime));
    }
}
