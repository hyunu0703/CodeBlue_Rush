using System.Collections.Generic;
using UnityEngine;

/// <summary>도로 이미지 규격에서 인도 중심선과 횡단보도를 만들고 도로 경계의 같은 위치를 연결한다.</summary>
public sealed class RoadSidewalkNetwork
{
    public readonly List<Vector2> Points = new List<Vector2>();                                // 맵 로컬 기준 보행 경로의 끝점이다.
    public readonly List<Edge> Edges = new List<Edge>();                                       // 양방향으로 이용하는 인도 또는 횡단보도다.
    public readonly List<List<int>> Connections = new List<List<int>>();                       // 끝점별 연결 간선 번호다.
    private readonly Dictionary<Vector2Int, int> pointIds = new Dictionary<Vector2Int, int>(); // 경계점 병합용 양자화 좌표다.

    /// <summary>한 도로가 소유하는 양방향 보행 구간과 횡단보도 신호 식별자다.</summary>
    public readonly struct Edge
    {
        public readonly int A;              // 첫 번째 끝점 번호다.
        public readonly int B;              // 두 번째 끝점 번호다.
        public readonly int Road;           // 스트리밍 활성 여부를 확인할 도로 번호다.
        public readonly RoadDirection Side; // 회전을 적용한 횡단보도의 가장자리 방향이다.
        public readonly bool IsCrossing;    // 차도 위에서 신호와 점유 보호가 필요한 구간인지 나타낸다.

        // 도로 번호와 신호 식별자를 경로에 함께 저장한다.
        public Edge(int a, int b, int road, RoadDirection side, bool crossing)
        {
            A = a;
            B = b;
            Road = road;
            Side = side;
            IsCrossing = crossing;
        }
    }

    // 도로 순서대로 구성해 후송 확장 시 기존 끝점과 간선 번호를 보존한다.
    public RoadSidewalkNetwork(RoadLaneNetwork roads)
    {
        for (var road = 0; road < roads.Roads.Count; road++)
        {
            BuildRoad(roads.Roads[road], road);
        }
    }

    // 열린 도로 양쪽 인도와 교차로 모서리를 연결한다. 닫힌 T자 방향은 차도가 없는 인도로 잇는다.
    private void BuildRoad(RoadLanePlacement placement, int road)
    {
        var layout = placement.Layout;
        var width = layout.ImageSize.x;
        var sidewalk = width * 32f / 1254f;
        var crossingOffset = width * (36.8f + 28f) / 1254f;
        var ends = new int[4, 2];
        for (var sideIndex = 0; sideIndex < 4; sideIndex++)
        {
            var side = (RoadDirection)sideIndex;
            var lanes = layout.GetLaneCount(side);
            var perpendicular = Mathf.Max(layout.GetLaneCount(RoadLaneMath.Rotate(side, 1)),
                layout.GetLaneCount(RoadLaneMath.Rotate(side, 3)));
            if (!layout.IsIntersection && lanes == 0)
            {
                continue;
            }
            // T자 막힌 쪽의 두 모서리는 반대쪽 도로의 폭을 기준으로 배치한다.
            var across = Mathf.Max(lanes, layout.GetLaneCount(RoadLaneMath.Rotate(side, 2))) * layout.LaneWidth * 0.5f + sidewalk;
            var radial = layout.IsIntersection
                ? perpendicular * layout.LaneWidth * 0.5f + (lanes > 0 ? crossingOffset : sidewalk) : 0f;
            var outward = RoadLaneMath.Outward(side);
            var right = RoadLaneMath.Right(outward);
            for (var bank = 0; bank < 2; bank++)
            {
                var offset = right * (across * (bank == 0 ? -1f : 1f));
                ends[sideIndex, bank] = AddPoint(placement, outward * radial + offset);
                if (lanes > 0)
                {
                    var boundary = AddPoint(placement, outward * (width * 0.5f) + offset);
                    if ((int)RoadLaneMath.Rotate(side, placement.ClockwiseQuarterTurns) == placement.ConstructionBarrierSide)
                    {
                        // 펜스 두께의 절반과 최대 시민 반폭을 비워 양쪽 접근 모두 차체가 닿기 전에 돌아선다.
                        var barrierDistance = width * 0.5f - layout.LaneWidth;
                        var clearance = layout.LaneWidth * (0.35f * 0.5f + 0.25f);
                        var inner = AddPoint(placement, outward * (barrierDistance - clearance) + offset);
                        var outer = AddPoint(placement, outward * (barrierDistance + clearance) + offset);
                        AddEdge(ends[sideIndex, bank], inner, road, side, false);
                        AddEdge(outer, boundary, road, side, false);
                    }
                    else
                    {
                        AddEdge(ends[sideIndex, bank], boundary, road, side, false);
                    }
                }
            }
            if (layout.IsIntersection)
            {
                AddEdge(ends[sideIndex, 0], ends[sideIndex, 1], road,
                    RoadLaneMath.Rotate(side, placement.ClockwiseQuarterTurns), lanes > 0);
            }
        }
        if (layout.IsIntersection)
        {
            for (var side = 0; side < 4; side++)
            {
                AddEdge(ends[side, 1], ends[(side + 1) % 4, 0], road, RoadDirection.North, false);
            }
        }
        // 직선의 양 끝은 중심의 같은 점으로 병합되어 각 인도가 끊김 없이 이어진다.
    }

    // 회전과 이동을 적용한 경계점을 0.0001유닛 격자로 병합한다.
    private int AddPoint(RoadLanePlacement placement, Vector2 local)
    {
        var point = placement.Position + RoadLaneMath.Rotate(local, placement.ClockwiseQuarterTurns);
        var key = new Vector2Int(Mathf.RoundToInt(point.x * 10000f), Mathf.RoundToInt(point.y * 10000f));
        if (pointIds.TryGetValue(key, out var id))
        {
            return id;
        }
        id = Points.Count;
        pointIds.Add(key, id);
        Points.Add(point);
        Connections.Add(new List<int>());
        return id;
    }

    // 길이가 있는 구간만 등록해 같은 경계점 사이의 중복 이동을 피한다.
    private void AddEdge(int a, int b, int road, RoadDirection side, bool crossing)
    {
        if (a == b)
        {
            return;
        }
        var id = Edges.Count;
        Edges.Add(new Edge(a, b, road, side, crossing));
        Connections[a].Add(id);
        Connections[b].Add(id);
    }
}
