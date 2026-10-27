using System.Collections.Generic;
using UnityEngine;

/// <summary>도로 배치와 시드로 건물 계획을 만든다. 씬 생성이나 전역 난수 상태 변경은 하지 않는다.</summary>
public static class MissionBuildingLayout
{
    // 보도 여백은 도로 로컬 유닛이며 건물 간격은 구급차 충돌 폭에 비례한다.
    private const float Tolerance = 0.0001f; // 맞닿은 외곽을 겹침으로 판정하지 않는 좌표 오차다.
    private const float SideGap = 0.08f;     // 보도를 침범하지 않도록 확보하는 거리다.
    public const float MaxGapRatio = 0.999f; // 계산 오차를 감안해 직선 간격을 구급차 폭보다 작게 제한한다.

    /// <summary>계획에 필요한 도로 원본과 맵 로컬 위치 및 시계방향 90도 회전 횟수다.</summary>
    public readonly struct Road
    {
        public readonly RoadPrefab Prefab;
        public readonly Vector2 Position;
        public readonly int Rotation;

        public Road(RoadPrefab prefab, Vector2 position, int rotation)
        {
            Prefab = prefab;
            Position = position;
            Rotation = rotation;
        }
    }

    /// <summary>맵 부모 아래 복원할 건물 원본과 맵 로컬 변환 및 공유 도로 정보다. 원본 크기는 유지한다.</summary>
    public readonly struct Placement
    {
        public readonly BuildingPrefab Prefab; // 풀에서 복원할 건물 원본이다.
        public readonly Vector2 Position;      // 도로 회전과 무관한 맵 로컬 위치다.
        public readonly float Scale;           // 기존 생성기와 호환되는 배율이며 새 계획에서는 원본 크기인 1을 사용한다.
        public readonly int Rotation;          // 맵 기준 시계방향 90도 회전 횟수다.
        public readonly Vector2[] Roads;       // 건물 외곽이 걸치는 모든 도로의 맵 좌표다.
        public readonly bool FlipX;            // 오른쪽 원본을 왼쪽에 배치할 때 이미지와 충돌 영역을 함께 반전한다.

        // 맵 로컬 변환과 겹치는 도로 목록을 하나의 복원 계획으로 보관한다.
        public Placement(BuildingPrefab prefab, Vector2 position, float scale, bool flipX = false, int rotation = 0, Vector2[] roads = null)
        {
            Prefab = prefab;
            Position = position;
            Scale = scale;
            FlipX = flipX;
            Rotation = rotation;
            Roads = roads;
        }
    }

    /// <summary>각 방향에서 사용할 건물의 필수 스프라이트, 박스 및 변환을 검증한다.</summary>
    public static bool Validate(BuildingPrefab[] prefabs, out string error)
    {
        error = null;
        // 프리팹별 분류와 중복 참조를 검사한다.
        var counts = new int[2];
        var unique = new HashSet<BuildingPrefab>();
        if (prefabs == null || prefabs.Length == 0)
        {
            error = "MissionMapGenerator의 Building Prefabs에 건물 프리팹을 연결해주세요.";
            return false;
        }
        foreach (var prefab in prefabs)
        {
            if (prefab == null || !unique.Add(prefab) || prefab.Index < 0 || prefab.Index > 2)
            {
                error = "건물 프리팹은 중복 없이 Index 0, 1 또는 2로 설정되어야 합니다.";
                return false;
            }
            var box = prefab.GetComponent<BoxCollider2D>();
            var renderer = prefab.GetComponent<SpriteRenderer>();
            var scale = prefab.transform.localScale;
            if (box == null || !box.enabled || box.isTrigger || box.size.x <= 0f || box.size.y <= 0f
                || renderer == null || renderer.sprite == null || scale.x <= 0f || scale.y <= 0f
                || Mathf.Abs(scale.x - scale.y) > 0.001f || Quaternion.Angle(prefab.transform.localRotation, Quaternion.identity) > 0.001f
                || prefab.GetComponent<Rigidbody2D>() != null)
            {
                error = prefab.name + ": 스프라이트, 정적 비트리거 박스, 양수 균일 XY 스케일과 회전 0이 필요합니다.";
                return false;
            }
            if (prefab.Index == 0)
            {
                counts[0]++;
                counts[1]++;
            }
            else
            {
                counts[prefab.Index - 1]++;
            }
        }
        if (counts[0] == 0 || counts[1] == 0)
        {
            error = "양쪽 공용 건물 또는 각 방향의 건물을 연결해주세요.";
            return false;
        }
        return true;
    }

    /// <summary>도로 경계를 합친 연속 보도선과 그 안의 교차로 구간을 저장한다.</summary>
    private sealed class Row
    {
        public int Axis;                                  // 0은 북쪽 진행, 1은 동쪽 진행이다.
        public int Side;                                  // 진행 기준 왼쪽은 1, 오른쪽은 2다.
        public float Curb;                                // 진행 방향의 오른쪽 축에 투영한 건물 앞면이다.
        public float Start;                               // 맵 끝 또는 모퉁이의 시작 좌표다.
        public float End;                                 // 맵 끝 또는 모퉁이의 끝 좌표다.
        public float Depth;                               // 보도 밖에서 사용할 수 있는 최소 폭이다.
        public readonly List<Junction> Junctions = new(); // 먼저 간격 0으로 덮어야 할 교차로 구간이다.
    }

    /// <summary>교차로에서 차도와 보도를 피한 건물 줄의 필수 점유 구간이다.</summary>
    private readonly struct Junction
    {
        public readonly float Start;  // 진행 축에서 교차로 안전 영역의 시작점이다.
        public readonly float End;    // 진행 축에서 교차로 안전 영역의 끝점이다.
        public readonly int Owner;    // 교차로가 속한 도로 계획 번호다.
        public readonly bool Reverse; // 모퉁이 끝에서 역방향으로 채워야 하는지 나타낸다.
        public readonly bool FullSpan; // T자 막힌 면처럼 양쪽 직선에 걸쳐 위상을 조절할 수 있는 구간이다.

        // 모퉁이 기준점과 도로 소유 번호를 한 구간으로 묶는다.
        public Junction(float start, float end, int owner, bool reverse, bool fullSpan)
        {
            Start = start;
            End = end;
            Owner = owner;
            Reverse = reverse;
            FullSpan = fullSpan;
        }
    }

    // 교차로를 먼저 고정하고 교차로 사이 직선 전체에 잔여 길이를 균등 배분한 전체 계획을 반환한다.
    public static bool TryPlan(List<Road> roads, int firstNewRoad, int seed, BuildingPrefab[] prefabs,
        Dictionary<Vector2, List<Placement>> existing, out Dictionary<Vector2, List<Placement>> plan, out string error, float ambulanceWidth)
    {
        plan = new Dictionary<Vector2, List<Placement>>();
        if (!Validate(prefabs, out error)) return false;
        if (ambulanceWidth <= 0f || float.IsNaN(ambulanceWidth) || float.IsInfinity(ambulanceWidth))
        {
            error = "건물 간격을 계산할 구급차 충돌 폭은 유한한 양수여야 합니다.";
            return false;
        }
        var random = new System.Random(seed ^ 0x31B17D);
        var depth = 0f;
        foreach (var prefab in prefabs) depth = Mathf.Max(depth, GetFootprint(prefab).size.x * prefab.transform.localScale.x);
        var allRows = BuildRows(roads, depth);
        var rows = allRows.FindAll(row => TouchesNewRoad(row, roads, firstNewRoad));
        // T자 막힌 면의 시작 위상만 바꿔 검증한다. 교차로를 비우거나 크기를 바꾸는 대체 배치는 사용하지 않는다.
        for (var attempt = 0; attempt < 64; attempt++)
        {
            if (TryPlanRows(rows, roads, firstNewRoad, prefabs, existing, random, ambulanceWidth * MaxGapRatio,
                attempt, out plan, out error)) return true;
        }
        return false;
    }

    // 입력 계획을 수정하지 않고 한 교차로 위상 후보와 그에 맞는 직선 전체를 원자적으로 계산한다.
    private static bool TryPlanRows(List<Row> rows, List<Road> roads, int firstNewRoad, BuildingPrefab[] prefabs,
        Dictionary<Vector2, List<Placement>> existing, System.Random random, float maxGap, int attempt,
        out Dictionary<Vector2, List<Placement>> plan, out string error)
    {
        plan = new Dictionary<Vector2, List<Placement>>();
        error = null;
        var occupied = new List<Placement>();
        foreach (var pair in existing)
        {
            foreach (var item in pair.Value)
            {
                // 후송으로 길어진 직선만 다시 계산하며 기존 교차로와 무관한 줄은 그대로 보존한다.
                if (!TouchesIntersection(item, roads) && rows.Exists(row => IsOnRow(item, row))) continue;
                StorePlacement(item, roads, occupied, plan);
            }
        }
        foreach (var row in rows)
        {
            foreach (var junction in row.Junctions)
            {
                if (junction.Owner < firstNewRoad) continue;
                if (!PackJunction(row, junction, roads, prefabs, occupied, random, plan, maxGap, attempt, out error))
                {
                    plan.Clear();
                    return false;
                }
            }
        }
        // 모든 교차로를 확정한 뒤에만 직선을 채운다. 직선 계산 결과로 교차로 위치를 이동하지 않는다.
        foreach (var row in rows)
        {
            if (!PlanStraight(row, roads, prefabs, occupied, random, plan, maxGap, out error))
            {
                plan.Clear();
                return false;
            }
        }
        return true;
    }

    // 교차로 여부는 이름이 아닌 실제 열린 방향의 수로 판단한다.
    public static bool IsIntersection(Road road)
    {
        var count = 0;
        for (var direction = 0; direction < 4; direction++)
        {
            if (road.Prefab.GetLaneCount((RoadDirection)direction, road.Rotation) > 0) count++;
        }
        return count > 2;
    }
    // 보도선을 합치고 모퉁이에서는 우선 배치하는 축의 깊이만큼 다른 축을 물린다.
    private static List<Row> BuildRows(List<Road> roads, float depth)
    {
        var segments = new List<Row>();
        for (var ordinal = 0; ordinal < roads.Count; ordinal++)
        {
            var road = roads[ordinal];
            var vertical = Mathf.Max(road.Prefab.GetLaneCount(RoadDirection.North, road.Rotation), road.Prefab.GetLaneCount(RoadDirection.South, road.Rotation));
            var horizontal = Mathf.Max(road.Prefab.GetLaneCount(RoadDirection.East, road.Rotation), road.Prefab.GetLaneCount(RoadDirection.West, road.Rotation));
            var alternate = (vertical == 6) != (horizontal == 6);
            for (var axis = 0; axis < 2; axis++)
            {
                var forward = axis == 0 ? RoadDirection.North : RoadDirection.East;
                var backward = axis == 0 ? RoadDirection.South : RoadDirection.West;
                var forwardLanes = road.Prefab.GetLaneCount(forward, road.Rotation);
                var backwardLanes = road.Prefab.GetLaneCount(backward, road.Rotation);
                if (forwardLanes == 0 && backwardLanes == 0)
                {
                    continue;
                }
                var along = Vector2.Dot(road.Position, Forward(axis));
                var across = Vector2.Dot(road.Position, Right(axis));
                var halfLength = (axis == 0 ? road.Prefab.ImageSize.y : road.Prefab.ImageSize.x) * 0.5f;
                var halfWidth = (axis == 0 ? road.Prefab.ImageSize.x : road.Prefab.ImageSize.y) * 0.5f;
                var edge = Edge(road, Mathf.Max(forwardLanes, backwardLanes));
                for (var side = 1; side <= 2; side++)
                {
                    var outward = axis == 0
                        ? (side == 1 ? RoadDirection.West : RoadDirection.East)
                        : (side == 1 ? RoadDirection.North : RoadDirection.South);
                    var crossingLanes = road.Prefab.GetLaneCount(outward, road.Rotation);
                    var curb = across + (side == 1 ? -edge : edge);
                    if (crossingLanes == 0)
                    {
                        AddSegment(segments, axis, side, curb, along - halfLength, along + halfLength, halfWidth - edge, IsIntersection(road), ordinal, false, true);
                        continue;
                    }
                    // 모퉁이마다 담당 축을 번갈아 배정해 짧은 직선의 양 끝에 잔여 길이가 몰리지 않게 한다.
                    var lowerPriority = (side == 1) != alternate;
                    var upperPriority = (side == 2) != alternate;
                    if (backwardLanes > 0)
                    {
                        var corner = Edge(road, crossingLanes) + (lowerPriority ? 0f : depth);
                        AddSegment(segments, axis, side, curb, along - halfLength, along - corner, halfWidth - edge, true, ordinal, true);
                    }
                    if (forwardLanes > 0)
                    {
                        var corner = Edge(road, crossingLanes) + (upperPriority ? 0f : depth);
                        AddSegment(segments, axis, side, curb, along + corner, along + halfLength, halfWidth - edge, true, ordinal, false);
                    }
                }
            }
        }
        segments.Sort((a, b) =>
        {
            var result = a.Axis.CompareTo(b.Axis);
            if (result == 0) result = a.Side.CompareTo(b.Side);
            if (result == 0 && Mathf.Abs(a.Curb - b.Curb) >= Tolerance) result = a.Curb.CompareTo(b.Curb);
            if (result == 0) result = a.Start.CompareTo(b.Start);
            return result;
        });
        var rows = new List<Row>();
        foreach (var segment in segments)
        {
            var previous = rows.Count > 0 ? rows[rows.Count - 1] : null;
            if (previous != null && previous.Axis == segment.Axis && previous.Side == segment.Side
                && Mathf.Abs(previous.Curb - segment.Curb) < Tolerance && Mathf.Abs(previous.End - segment.Start) < Tolerance)
            {
                previous.End = segment.End;
                previous.Junctions.AddRange(segment.Junctions);
                previous.Depth = Mathf.Min(previous.Depth, segment.Depth);
            }
            else
            {
                rows.Add(segment);
            }
        }
        // 경계가 이어지는 한 줄 안에서도 교차로 쪽 끝을 기준으로 건물 선택 순서를 정한다.
        rows.Sort((a, b) =>
        {
            var result = b.Junctions.Count.CompareTo(a.Junctions.Count);
            if (result == 0) result = a.Axis.CompareTo(b.Axis);
            if (result == 0) result = a.Side.CompareTo(b.Side);
            if (result == 0 && Mathf.Abs(a.Curb - b.Curb) >= Tolerance) result = a.Curb.CompareTo(b.Curb);
            if (result == 0) result = a.Start.CompareTo(b.Start);
            return result;
        });
        return rows;
    }

    // 도로 한 구역의 안전한 보도선과 교차로의 필수 점유 구간을 기록한다.
    private static void AddSegment(List<Row> rows, int axis, int side, float curb, float start, float end,
        float depth, bool intersection, int owner, bool reverse, bool fullSpan = false)
    {
        if (end - start <= Tolerance || depth <= Tolerance) return;
        var row = new Row { Axis = axis, Side = side, Curb = curb, Start = start, End = end, Depth = depth };
        if (intersection) row.Junctions.Add(new Junction(start, end, owner, reverse, fullSpan));
        rows.Add(row);
    }

    // 새 도로가 닿는 보도선만 선택해 후송과 무관한 기존 건물 줄은 보존한다.
    private static bool TouchesNewRoad(Row row, List<Road> roads, int firstNewRoad)
    {
        var bounds = RowBounds(row, row.Start, row.End);
        for (var index = firstNewRoad; index < roads.Count; index++)
        {
            if (Overlaps(bounds, TileBounds(roads[index]))) return true;
        }
        return false;
    }

    // 원본의 앞면과 진행 축으로 판단해 같은 보도선의 건물만 선택한다.
    private static bool IsOnRow(Placement placement, Row row)
    {
        if (placement.Rotation != row.Axis) return false;
        var bounds = GetBounds(placement);
        var width = row.Axis == 0 ? bounds.size.x : bounds.size.y;
        var front = Vector2.Dot(bounds.center, Right(row.Axis)) + (row.Side == 1 ? width : -width) * 0.5f;
        return Mathf.Abs(front - row.Curb) < Tolerance && Overlaps(bounds, RowBounds(row, row.Start, row.End));
    }

    // 외곽 일부라도 교차로에 걸친 건물은 교차로 고정 배치로 취급한다.
    private static bool TouchesIntersection(Placement placement, List<Road> roads)
    {
        var bounds = GetBounds(placement);
        foreach (var road in roads)
        {
            if (IsIntersection(road) && Overlaps(bounds, TileBounds(road))) return true;
        }
        return false;
    }

    // 모퉁이 기준점에서 교차로 전체를 간격 없이 채우며 마지막 건물은 직선 경계를 걸칠 수 있다.
    private static bool PackJunction(Row row, Junction junction, List<Road> roads, BuildingPrefab[] prefabs,
        List<Placement> occupied, System.Random random, Dictionary<Vector2, List<Placement>> plan, float maxGap, int attempt, out string error)
    {
        error = null;
        if (junction.FullSpan) return PackFullJunction(row, junction, roads, prefabs, occupied, random, plan, maxGap, attempt, out error);
        var cursor = junction.Reverse ? junction.End : junction.Start;
        while (junction.Reverse ? cursor > junction.Start + Tolerance : cursor < junction.End - Tolerance)
        {
            var allowed = new List<Placement>();
            foreach (var prefab in prefabs)
            {
                if (!prefab.AllowIntersection || (prefab.Index != 0 && prefab.Index != row.Side)) continue;
                var size = Vector2.Scale(GetFootprint(prefab).size, prefab.transform.localScale);
                var start = junction.Reverse ? cursor - size.y : cursor;
                if (size.x > row.Depth + Tolerance || start < row.Start - Tolerance || start + size.y > row.End + Tolerance) continue;
                var item = MakePlacement(row, prefab, start);
                if (!Fits(item, roads, occupied)) continue;
                var sequence = new List<Placement>(occupied) { item };
                if (RespectsRuns(row, sequence, maxGap)) allowed.Add(item);
            }
            if (allowed.Count == 0)
            {
                error = $"교차로 건물을 간격 0으로 배치할 수 없습니다. 축 {row.Axis}, 보도선 {row.Curb}, 위치 {cursor}를 확인해주세요.";
                return false;
            }
            var selected = allowed[random.Next(allowed.Count)];
            StorePlacement(selected, roads, occupied, plan);
            cursor = junction.Reverse ? AlongMin(GetBounds(selected), row.Axis) : AlongMax(GetBounds(selected), row.Axis);
        }
        return true;
    }

    // T자 막힌 면을 빈틈없이 덮는 줄을 만들고 양쪽 직선으로 걸치는 길이만 조절한다.
    private static bool PackFullJunction(Row row, Junction junction, List<Road> roads, BuildingPrefab[] prefabs,
        List<Placement> occupied, System.Random random, Dictionary<Vector2, List<Placement>> plan, float maxGap, int attempt, out string error)
    {
        error = null;
        var chosen = new List<BuildingPrefab>();
        var length = 0f;
        while (length < junction.End - junction.Start - Tolerance)
        {
            var allowed = new List<BuildingPrefab>();
            foreach (var prefab in prefabs)
            {
                if (!prefab.AllowIntersection || (prefab.Index != 0 && prefab.Index != row.Side)) continue;
                var size = Vector2.Scale(GetFootprint(prefab).size, prefab.transform.localScale);
                if (size.x > row.Depth + Tolerance) continue;
                if (chosen.Count > 1 && prefab == chosen[chosen.Count - 1] && prefab == chosen[chosen.Count - 2]) continue;
                allowed.Add(prefab);
            }
            if (allowed.Count == 0)
            {
                error = "T자 교차로를 빈틈없이 덮을 건물 후보가 없습니다.";
                return false;
            }
            var selected = allowed[random.Next(allowed.Count)];
            chosen.Add(selected);
            length += GetFootprint(selected).size.y * selected.transform.localScale.y;
        }
        var phase = attempt == 0 ? 0f : attempt == 1 ? 1f : attempt == 2 ? 0.5f : (float)random.NextDouble();
        var cursor = junction.Start - (length - (junction.End - junction.Start)) * phase;
        foreach (var prefab in chosen)
        {
            var item = MakePlacement(row, prefab, cursor);
            var bounds = GetBounds(item);
            var sequence = new List<Placement>(occupied) { item };
            if (AlongMin(bounds, row.Axis) < row.Start - Tolerance || AlongMax(bounds, row.Axis) > row.End + Tolerance
                || !Fits(item, roads, occupied) || !RespectsRuns(row, sequence, maxGap))
            {
                error = "T자 교차로 양쪽 직선에 건물을 걸쳐 배치할 공간이 없습니다.";
                return false;
            }
            StorePlacement(item, roads, occupied, plan);
            cursor = AlongMax(bounds, row.Axis);
        }
        return true;
    }

    // 고정된 교차로 사이의 직선 전체를 한 구간으로 계산하고 각 구간의 간격을 동일하게 만든다.
    private static bool PlanStraight(Row row, List<Road> roads, BuildingPrefab[] prefabs, List<Placement> occupied,
        System.Random random, Dictionary<Vector2, List<Placement>> plan, float maxGap, out string error)
    {
        var fixedItems = occupied.FindAll(item => IsOnRow(item, row));
        fixedItems.Sort((a, b) => AlongMin(GetBounds(a), row.Axis).CompareTo(AlongMin(GetBounds(b), row.Axis)));
        var start = row.Start;
        var hasLeft = false;
        foreach (var item in fixedItems)
        {
            var end = AlongMin(GetBounds(item), row.Axis);
            if (!FillStraight(row, start, end, hasLeft, true, roads, prefabs, occupied, random, plan, maxGap, out error)) return false;
            start = AlongMax(GetBounds(item), row.Axis);
            hasLeft = true;
        }
        return FillStraight(row, start, row.End, hasLeft, false, roads, prefabs, occupied, random, plan, maxGap, out error);
    }

    // 건물 크기는 바꾸지 않고 양 끝의 고정 건물까지 포함한 모든 간격에 남는 길이를 균등 분배한다.
    private static bool FillStraight(Row row, float start, float end, bool hasLeft, bool hasRight,
        List<Road> roads, BuildingPrefab[] prefabs, List<Placement> occupied, System.Random random,
        Dictionary<Vector2, List<Placement>> plan, float maxGap, out string error)
    {
        error = null;
        var length = end - start;
        if (length <= Tolerance) return true;
        var minimum = float.MaxValue;
        foreach (var prefab in prefabs)
        {
            if (prefab.Index != 0 && prefab.Index != row.Side) continue;
            var size = Vector2.Scale(GetFootprint(prefab).size, prefab.transform.localScale);
            if (size.x <= row.Depth + Tolerance) minimum = Mathf.Min(minimum, size.y);
        }
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var chosen = new List<Placement>();
            var cursor = start;
            while (true)
            {
                var allowed = new List<Placement>();
                foreach (var prefab in prefabs)
                {
                    if (prefab.Index != 0 && prefab.Index != row.Side) continue;
                    var size = Vector2.Scale(GetFootprint(prefab).size, prefab.transform.localScale);
                    if (size.x > row.Depth + Tolerance || cursor + size.y > end + Tolerance
                        || (attempt == 63 && size.y > minimum + Tolerance)) continue;
                    var item = MakePlacement(row, prefab, cursor);
                    if (Fits(item, roads, occupied)) allowed.Add(item);
                }
                if (allowed.Count == 0) break;
                var selected = allowed[random.Next(allowed.Count)];
                chosen.Add(selected);
                cursor = AlongMax(GetBounds(selected), row.Axis);
            }
            var gaps = chosen.Count - 1 + (hasLeft ? 1 : 0) + (hasRight ? 1 : 0);
            var remainder = end - cursor;
            if (chosen.Count == 0)
            {
                if (hasLeft && hasRight && length <= maxGap && GapOutsideJunction(row, start, end, roads)) return true;
                continue;
            }
            var gap = gaps > 0 ? remainder / gaps : 0f;
            var outside = 0f;
            if (gap > maxGap && (!hasLeft || !hasRight))
            {
                // 다음 교차로가 없는 맵 외곽 끝만 잔여 공간을 허용한다. 교차로 사이에는 적용하지 않는다.
                gap = maxGap;
                outside = remainder - gap * gaps;
            }
            if (gap < -Tolerance || gap > maxGap || (gaps == 0 && remainder > Tolerance)) continue;
            var shifted = new List<Placement>();
            var valid = true;
            for (var index = 0; index < chosen.Count; index++)
            {
                var item = chosen[index];
                var shift = gap * (index + (hasLeft ? 1 : 0)) + (!hasLeft ? outside * (hasRight ? 1f : 0.5f) : 0f);
                var placement = new Placement(item.Prefab, item.Position + Forward(row.Axis) * shift, 1f, item.FlipX, row.Axis);
                if (!Fits(placement, roads, occupied) || TouchesIntersection(placement, roads))
                {
                    valid = false;
                    break;
                }
                shifted.Add(placement);
            }
            var sequence = new List<Placement>(occupied);
            sequence.AddRange(shifted);
            if (!valid || !RespectsRuns(row, sequence, maxGap)) continue;
            foreach (var item in shifted) StorePlacement(item, roads, occupied, plan);
            return true;
        }
        error = $"직선 전체의 간격을 구급차 폭 미만으로 맞출 수 없습니다. 축 {row.Axis}, 보도선 {row.Curb}, 길이 {length}를 확인해주세요.";
        return false;
    }

    // 간격만 남기는 경우에도 그 빈 공간이 교차로 안으로 들어가지 않아야 한다.
    private static bool GapOutsideJunction(Row row, float start, float end, List<Road> roads)
    {
        var gap = RowBounds(row, start, end);
        foreach (var road in roads)
        {
            if (IsIntersection(road) && Overlaps(gap, TileBounds(road))) return false;
        }
        return true;
    }

    // 실제로 인접한 건물 세 개만 비교해 교차로와 직선의 접점에서도 동일 원본 3연속을 막는다.
    private static bool RespectsRuns(Row row, List<Placement> items, float maxGap)
    {
        var sequence = items.FindAll(item => IsOnRow(item, row));
        sequence.Sort((a, b) => AlongMin(GetBounds(a), row.Axis).CompareTo(AlongMin(GetBounds(b), row.Axis)));
        for (var index = 2; index < sequence.Count; index++)
        {
            if (sequence[index].Prefab != sequence[index - 1].Prefab || sequence[index].Prefab != sequence[index - 2].Prefab) continue;
            var first = GetBounds(sequence[index - 2]);
            var middle = GetBounds(sequence[index - 1]);
            var last = GetBounds(sequence[index]);
            if (AlongMin(middle, row.Axis) - AlongMax(first, row.Axis) <= maxGap + Tolerance
                && AlongMin(last, row.Axis) - AlongMax(middle, row.Axis) <= maxGap + Tolerance) return false;
        }
        return true;
    }
    // 경계를 걸치는 건물도 한 번만 등록하고 반환 시 확인할 모든 도로 좌표를 보관한다.
    private static void StorePlacement(Placement placement, List<Road> roads, List<Placement> occupied,
        Dictionary<Vector2, List<Placement>> added)
    {
        var bounds = GetBounds(placement);
        var touches = new List<Vector2>();
        foreach (var road in roads)
        {
            if (Overlaps(bounds, TileBounds(road))) touches.Add(road.Position);
        }
        var stored = new Placement(placement.Prefab, placement.Position, 1f, placement.FlipX, placement.Rotation, touches.ToArray());
        if (!added.TryGetValue(touches[0], out var layout))
        {
            layout = new List<Placement>();
            added.Add(touches[0], layout);
        }
        layout.Add(stored);
        occupied.Add(stored);
    }

    // 보도선의 앞쪽 면과 직전 건물의 끝에 외곽을 정렬한다.
    private static Placement MakePlacement(Row row, BuildingPrefab prefab, float start)
    {
        var footprint = GetFootprint(prefab);
        var scale = (Vector2)prefab.transform.localScale;
        var size = Vector2.Scale(footprint.size, scale);
        var flip = row.Side == 1 && prefab.Index == 0;
        var center = Right(row.Axis) * (row.Curb + (row.Side == 1 ? -size.x : size.x) * 0.5f)
            + Forward(row.Axis) * (start + size.y * 0.5f);
        scale.x *= flip ? -1f : 1f;
        var offset = Vector2.Scale(footprint.center, scale);
        return new Placement(prefab, center - Right(row.Axis) * offset.x - Forward(row.Axis) * offset.y, 1f, flip, row.Axis);
    }

    // 아파트의 교차로 침범과 기존 건물의 외곽 겹침을 실제 사각 영역으로 검사한다.
    private static bool Fits(Placement placement, List<Road> roads, List<Placement> occupied)
    {
        var bounds = GetBounds(placement);
        var coveredArea = 0f;
        foreach (var road in roads)
        {
            var tile = TileBounds(road);
            if (!Overlaps(bounds, tile))
            {
                continue;
            }
            if (!placement.Prefab.AllowIntersection && IsIntersection(road))
            {
                return false;
            }
            var min = Vector2.Max(bounds.min, tile.min);
            var max = Vector2.Min(bounds.max, tile.max);
            coveredArea += (max.x - min.x) * (max.y - min.y);
        }
        // 먼 좌표에서 min/max를 계산할 때 생기는 반올림도 같은 기준으로 비교한다.
        var footprintArea = (bounds.max.x - bounds.min.x) * (bounds.max.y - bounds.min.y);
        if (coveredArea + Tolerance < footprintArea)
        {
            return false;
        }
        foreach (var item in occupied)
        {
            if (Overlaps(bounds, GetBounds(item)))
            {
                return false;
            }
        }
        return true;
    }

    // 맵 로컬 외곽으로 축을 맞춰 회전과 반전을 함께 복원한다.
    public static Bounds GetBounds(Placement placement)
    {
        var footprint = GetFootprint(placement.Prefab);
        var scale = (Vector2)placement.Prefab.transform.localScale * placement.Scale;
        var size = Vector2.Scale(footprint.size, scale);
        scale.x *= placement.FlipX ? -1f : 1f;
        var offset = Vector2.Scale(footprint.center, scale);
        var center = placement.Position + Right(placement.Rotation) * offset.x + Forward(placement.Rotation) * offset.y;
        return new Bounds(center, placement.Rotation == 0 ? size : new Vector2(size.y, size.x));
    }

    // 경계 계산 오차를 제외하고 면적이 있는 겹침만 검출한다.
    public static bool Overlaps(Bounds a, Bounds b)
    {
        return a.min.x < b.max.x - Tolerance && a.max.x > b.min.x + Tolerance
            && a.min.y < b.max.y - Tolerance && a.max.y > b.min.y + Tolerance;
    }

    // 도로가 차지하는 구역을 맵 좌표로 반환한다.
    public static Bounds TileBounds(Road road) => new Bounds(road.Position, road.Prefab.ImageSize);

    // 건물 줄의 대상 구간을 차지하는 외곽을 반환한다.
    private static Bounds RowBounds(Row row, float start, float end)
    {
        var center = Right(row.Axis) * (row.Curb + (row.Side == 1 ? -row.Depth : row.Depth) * 0.5f)
            + Forward(row.Axis) * ((start + end) * 0.5f);
        return new Bounds(center, row.Axis == 0 ? new Vector2(row.Depth, end - start) : new Vector2(end - start, row.Depth));
    }

    // 현재 이미지 규격에 맞춰 차선과 보도 바깥에 안전거리를 더한다.
    private static float Edge(Road road, int lanes) => road.Prefab.ImageSize.x * (lanes * 0.5f * 84.6f + 64f) / 1254f + SideGap;

    // 진행 축에 투영한 외곽 시작점을 반환한다.
    private static float AlongMin(Bounds bounds, int axis) => axis == 0 ? bounds.min.y : bounds.min.x;

    // 진행 축에 투영한 외곽 끝점을 반환한다.
    private static float AlongMax(Bounds bounds, int axis) => axis == 0 ? bounds.max.y : bounds.max.x;

    // 건물 줄이 진행하는 단위 축을 반환한다.
    private static Vector2 Forward(int axis) => axis == 0 ? Vector2.up : Vector2.right;

    // 건물 원본의 오른쪽이 향하는 단위 축을 반환한다.
    private static Vector2 Right(int axis) => axis == 0 ? Vector2.right : Vector2.down;
    // 이미지와 충돌 영역을 모두 포함하는 원본 로컬 외곽으로 시각적인 겹침도 방지한다.
    public static Bounds GetFootprint(BuildingPrefab prefab)
    {
        var renderer = prefab.GetComponent<SpriteRenderer>();
        var bounds = renderer.sprite.bounds;
        var center = bounds.center;
        center.x *= renderer.flipX ? -1f : 1f;
        center.y *= renderer.flipY ? -1f : 1f;
        bounds.center = center;
        var box = prefab.GetComponent<BoxCollider2D>();
        var colliderBounds = new Bounds(box.offset, box.size);
        bounds.Encapsulate(colliderBounds.min);
        bounds.Encapsulate(colliderBounds.max);
        return bounds;
    }
}
