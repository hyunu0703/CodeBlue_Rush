using System.Collections.Generic;
using UnityEngine;

/// <summary>도로 끝의 진입 및 진출 차선을 검증하고 두 도로의 연결을 소유한다</summary>
[DisallowMultipleComponent]
public sealed class RoadConnection : MonoBehaviour
{
    [Tooltip("도로 안으로 들어오는 차선을 중앙선부터 바깥 순서로 지정")]
    [SerializeField] private TrafficLane[] incoming = new TrafficLane[0];
    [Tooltip("도로 밖으로 나가는 차선을 중앙선부터 바깥 순서로 지정")]
    [SerializeField] private TrafficLane[] outgoing = new TrafficLane[0];
    [Tooltip("씬에서 연결할 반대편 도로 지점. 양쪽 중 한쪽에만 지정해도 연결")]
    [SerializeField] private RoadConnection target;

    private RoadConnection connectedTo;
    public RoadConnection ConnectedTo => connectedTo;
    public Vector3 Position => transform.position;
    public Vector3 Outward => transform.up;
    public int IncomingCount => incoming == null ? 0 : incoming.Length;
    public int OutgoingCount => outgoing == null ? 0 : outgoing.Length;
    internal const float PositionTolerance = 0.05f;
    private const float DirectionDot = 0.98f;

    // 명시적으로 설정된 씬 연결을 모든 Awake 이후에 적용한다
    private void Start()
    {
        if (target && !TryConnect(target, out string error))
            Debug.LogError(name + ": " + error, this);
    }

    // 지점이 비활성화되면 반대편과 다음 차선 참조를 함께 해제한다
    private void OnDisable()
    {
        Disconnect();
    }

    // 비활성화 후 다시 켜진 지점의 명시적 연결을 복구한다
    private void OnEnable()
    {
        if (target && target.isActiveAndEnabled)
            TryConnect(target, out _);
    }

    // 도로 지점의 차선과 끝점 방향을 검사한다
    public bool Validate(out string error)
    {
        error = null;
        HashSet<TrafficLane> used = new HashSet<TrafficLane>();
        if (IncomingCount + OutgoingCount == 0 || !ValidateLanes(incoming, true, used) || !ValidateLanes(outgoing, false, used))
            error = "차선 누락/중복, 외부 차선, 끝점 평면 또는 진입·진출 방향이 잘못되었습니다.";
        return error == null;
    }

    // 배열의 각 차선이 해당 도로 끝에 속하는지 검사한다
    private bool ValidateLanes(TrafficLane[] lanes, bool entering, HashSet<TrafficLane> used)
    {
        if (lanes == null)
            return false;
        RoadChunk owner = GetComponentInParent<RoadChunk>();
        if (!owner)
            return false;
        foreach (TrafficLane lane in lanes)
        {
            if (!lane || !used.Add(lane) || lane.GetComponentInParent<RoadChunk>() != owner || !lane.Validate(out _))
                return false;
            Vector3 point = entering ? lane.StartPoint : lane.EndPoint;
            Vector3 direction = entering ? -lane.StartDirection : lane.EndDirection;
            if (Mathf.Abs(Vector3.Dot(point - Position, Outward)) > PositionTolerance || Mathf.Abs(point.z - Position.z) > PositionTolerance)
                return false;
            if (Vector3.Dot(direction, Outward) < DirectionDot)
                return false;
        }
        return true;
    }

    // 모든 조건이 맞을 때만 양방향 도로와 다음 차선을 한 번에 연결한다
    public bool TryConnect(RoadConnection other, out string error)
    {
        error = null;
        if (!other || other == this || !isActiveAndEnabled || !other.isActiveAndEnabled)
            error = "활성 상태의 서로 다른 연결 지점이 필요합니다.";
        else if ((connectedTo && connectedTo != other) || (other.connectedTo && other.connectedTo != this))
            error = "이미 다른 도로에 연결되어 있습니다. 먼저 Disconnect를 호출하세요.";
        else if ((target && target != other) || (other.target && other.target != this))
            error = "Inspector Target과 요청한 연결이 충돌합니다.";
        else if (!Validate(out error) || !other.Validate(out error))
            return false;
        else if (!GetComponentInParent<RoadChunk>().Validate(out error) || !other.GetComponentInParent<RoadChunk>().Validate(out error))
            return false;
        else if (GetComponentInParent<RoadChunk>() == other.GetComponentInParent<RoadChunk>())
            error = "같은 도로 내부의 연결 지점을 서로 연결할 수 없습니다.";
        else if (Vector3.Distance(Position, other.Position) > PositionTolerance || Vector3.Dot(Outward, other.Outward) > -DirectionDot)
            error = "도로 지점의 위치가 일치하고 바깥 방향이 서로 반대여야 합니다.";
        else if (!Match(outgoing, other.incoming) || !Match(other.outgoing, incoming))
            error = "차선 수, 순서, 끝점 또는 진행 방향이 일치하지 않습니다.";

        if (error != null)
            return false;

        connectedTo = other;
        other.connectedTo = this;
        Link(outgoing, other.incoming);
        Link(other.outgoing, incoming);
        return true;
    }

    // 같은 인덱스의 차선 끝점과 방향을 직접 비교한다
    private static bool Match(TrafficLane[] exits, TrafficLane[] entries)
    {
        if (exits.Length != entries.Length)
            return false;
        for (int i = 0; i < exits.Length; i++)
        {
            if (Vector3.Distance(exits[i].EndPoint, entries[i].StartPoint) > PositionTolerance || Vector3.Dot(exits[i].EndDirection, entries[i].StartDirection) < DirectionDot)
                return false;
            if (exits[i].NextLane && exits[i].NextLane != entries[i])
                return false;
        }
        return true;
    }

    // 검증된 차선 배열의 다음 차선 참조를 저장한다
    private static void Link(TrafficLane[] exits, TrafficLane[] entries)
    {
        for (int i = 0; i < exits.Length; i++)
            exits[i].SetNext(entries[i]);
    }

    // 연결 양쪽의 참조를 해제하며 다른 지점의 연결은 보존한다
    public void Disconnect()
    {
        RoadConnection other = connectedTo;
        connectedTo = null;
        ClearExits();
        if (other && other.connectedTo == this)
        {
            other.connectedTo = null;
            other.ClearExits();
        }
    }

    // 이 지점이 소유한 진출 차선의 연결을 초기화한다
    private void ClearExits()
    {
        if (outgoing == null)
            return;
        foreach (TrafficLane lane in outgoing)
        {
            if (lane)
                lane.SetNext(null);
        }
    }

    // 도로 끝 위치와 바깥 방향을 Scene에 표시한다
    private void OnDrawGizmos()
    {
        Gizmos.color = connectedTo ? Color.green : Color.yellow;
        Gizmos.DrawWireSphere(Position, 0.35f);
        Gizmos.DrawLine(Position, Position + Outward);
    }

    // 도로 전체 검증에 사용할 차선 끝 참조를 반환한다
    internal TrafficLane GetLane(int index, bool entering)
    {
        return entering ? incoming[index] : outgoing[index];
    }
}
