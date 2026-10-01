using System.Collections.Generic;
using UnityEngine;

/// <summary>도로 Prefab의 차선 및 연결 지점 목록을 제공하고 구성을 검증한다</summary>
[DisallowMultipleComponent]
public sealed class RoadChunk : MonoBehaviour
{
    [SerializeField] private TrafficLane[] lanes = new TrafficLane[0];
    [SerializeField] private RoadConnection[] connections = new RoadConnection[0];

    public int LaneCount => lanes == null ? 0 : lanes.Length;
    public int ConnectionCount => connections == null ? 0 : connections.Length;

    // 도로가 소유한 차선을 인덱스로 조회한다
    public TrafficLane GetLane(int index)
    {
        return index >= 0 && index < LaneCount ? lanes[index] : null;
    }

    // 도로가 소유한 연결 지점을 인덱스로 조회한다
    public RoadConnection GetConnection(int index)
    {
        return index >= 0 && index < ConnectionCount ? connections[index] : null;
    }

    // 차선 소유권과 모든 시작 및 끝 지점의 단일 등록을 검사한다
    public bool Validate(out string error)
    {
        error = null;
        if (LaneCount < 2 || ConnectionCount < 2)
        {
            error = "차선과 도로 연결 지점이 각각 두 개 이상 필요합니다.";
            return false;
        }

        HashSet<TrafficLane> owned = new HashSet<TrafficLane>();
        foreach (TrafficLane lane in lanes)
        {
            if (!lane || !owned.Add(lane) || lane.GetComponentInParent<RoadChunk>() != this || !lane.Validate(out error))
            {
                error = error ?? "차선 참조 누락, 중복 또는 소유권 오류입니다.";
                return false;
            }
        }
        foreach (TrafficLane lane in lanes)
        {
            if ((lane.LeftLane && !owned.Contains(lane.LeftLane)) || (lane.RightLane && !owned.Contains(lane.RightLane)))
            {
                error = "인접 차선도 같은 도로 목록에 등록해야 합니다.";
                return false;
            }
        }

        HashSet<RoadConnection> ports = new HashSet<RoadConnection>();
        HashSet<TrafficLane> starts = new HashSet<TrafficLane>();
        HashSet<TrafficLane> ends = new HashSet<TrafficLane>();
        foreach (RoadConnection connection in connections)
        {
            if (!connection || !ports.Add(connection) || connection.GetComponentInParent<RoadChunk>() != this || !connection.Validate(out error))
            {
                error = error ?? "도로 연결 지점 참조 누락, 중복 또는 소유권 오류입니다.";
                return false;
            }
            for (int i = 0; i < connection.IncomingCount; i++)
            {
                TrafficLane lane = connection.GetLane(i, true);
                if (!owned.Contains(lane) || !starts.Add(lane))
                {
                    error = "차선 시작점은 도로 지점 한 곳에만 등록해야 합니다.";
                    return false;
                }
            }
            for (int i = 0; i < connection.OutgoingCount; i++)
            {
                TrafficLane lane = connection.GetLane(i, false);
                if (!owned.Contains(lane) || !ends.Add(lane))
                {
                    error = "차선 끝점은 도로 지점 한 곳에만 등록해야 합니다.";
                    return false;
                }
            }
        }
        if (starts.Count != LaneCount || ends.Count != LaneCount)
            error = "모든 차선의 시작점과 끝점을 도로 연결 지점에 등록해야 합니다.";
        return error == null;
    }

    // Inspector 메뉴에서 Prefab 구성 오류를 확인한다
    [ContextMenu("Validate Road")]
    private void ValidateRoad()
    {
        if (!Validate(out string error))
            Debug.LogError(name + ": " + error, this);
        else
            Debug.Log(name + ": 도로 구성이 유효합니다.", this);
    }
}
