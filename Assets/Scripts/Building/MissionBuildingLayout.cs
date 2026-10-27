using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>도로 배치와 시드로 건물 계획을 만든다. 씬 생성이나 전역 난수 상태 변경은 하지 않는다.</summary>
public static class MissionBuildingLayout
{
    // 도로 로컬 유닛 기준 보도, 구역 경계 및 인접 건물 사이의 최소 여백이다.
    private const float SideGap = 0.08f;
    private const float EndGap = 0.12f;
    private const float BuildingGap = 0.12f;

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

    /// <summary>도로 자식으로 복원할 건물 원본, 로컬 위치와 균일 XY 보정 비율이다.</summary>
    public readonly struct Placement
    {
        public readonly BuildingPrefab Prefab;
        public readonly Vector2 Position;
        public readonly float Scale;

        public Placement(BuildingPrefab prefab, Vector2 position, float scale)
        {
            Prefab = prefab;
            Position = position;
            Scale = scale;
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

    /// <summary>기존 계획을 보존하면서 새 직선 구간만 계획한다. 구간 경계에서도 동일 건물 3연속을 막는다.</summary>
    public static bool TryPlan(List<Road> roads, int firstNewRoad, int seed, BuildingPrefab[] prefabs,
        Dictionary<Vector2, List<Placement>> existing, out Dictionary<Vector2, List<Placement>> added, out string error)
    {
        // 도로 생성용 난수와 분리된 시드 난수이며 생성 순서와 풀 상태에 의존하지 않는다.
        var random = new System.Random(seed ^ 0x31B17D);
        var known = new Dictionary<Vector2, List<Placement>>(existing);
        added = new Dictionary<Vector2, List<Placement>>();
        error = null;
        for (var ordinal = firstNewRoad; ordinal < roads.Count; ordinal++)
        {
            var road = roads[ordinal];
            if (!IsStraight(road.Prefab.Index))
            {
                continue;
            }
            var layout = new List<Placement>(10);
            for (var side = 1; side <= 2; side++)
            {
                // 원본 도로의 -Y에서 +Y 순서로 계획하여 앞뒤 이웃을 동일하게 판단한다.
                var lower = GetNeighbour(roads, road, -1, side, known);
                var upper = GetNeighbour(roads, road, 1, side, known);
                var count = random.Next(4, 6);
                if (!TryPlanSide(road, side, count, prefabs, lower, upper, random, layout)
                    && !TryPlanSide(road, side, 4, prefabs, lower, upper, random, layout))
                {
                    error = road.Prefab.name + ": 여백과 연속 제한을 만족하는 건물 4개를 배치할 수 없습니다.";
                    return false;
                }
            }
            added.Add(road.Position, layout);
            known.Add(road.Position, layout);
        }
        return true;
    }

    /// <summary>직선 원본 세 종류만 건물을 배치한다.</summary>
    private static bool IsStraight(RoadIndex index)
    {
        return index == RoadIndex.Straight2Lane || index == RoadIndex.Straight4Lane || index == RoadIndex.Straight6Lane;
    }

    /// <summary>같은 원본 방향을 가진 연결 구간의 건물 순서를 읽는다. 반대 방향은 좌우 분류가 달라 같은 건물이 이어지지 않는다.</summary>
    private static List<Placement> GetNeighbour(List<Road> roads, Road road, int direction, int side,
        Dictionary<Vector2, List<Placement>> known)
    {
        // 방향 벡터는 도로의 실제 시계방향 회전을 적용한다.
        var up = (Vector2)(Quaternion.Euler(0f, 0f, -90f * road.Rotation) * Vector3.up);
        foreach (var other in roads)
        {
            var target = road.Position + up * direction * ((road.Prefab.ImageSize.y + other.Prefab.ImageSize.y) * 0.5f);
            if (other.Rotation != road.Rotation || (other.Position - target).sqrMagnitude > 0.000001f
                || !known.TryGetValue(other.Position, out var placements))
            {
                continue;
            }
            var result = new List<Placement>(5);
            foreach (var placement in placements)
            {
                if ((placement.Position.x < 0f ? 1 : 2) == side)
                {
                    result.Add(placement);
                }
            }
            return result;
        }
        return new List<Placement>();
    }

    /// <summary>4~5개의 비율을 유지하며 각 이미지 박스를 보도 밖 구역 안에 정렬한다.</summary>
    private static bool TryPlanSide(Road road, int side, int count, BuildingPrefab[] prefabs,
        List<Placement> lower, List<Placement> upper, System.Random random, List<Placement> output)
    {
        // 기존 도로 이미지의 차선 84.6px, 보도 64px를 1254px 전체 폭에 비례시킨다.
        var edge = road.Prefab.ImageSize.x * (road.Prefab.GetLaneCount(RoadDirection.North, 0) * 0.5f * 84.6f + 64f) / 1254f;
        var availableWidth = road.Prefab.ImageSize.x * 0.5f - edge - SideGap * 2f;
        var availableLength = road.Prefab.ImageSize.y - EndGap * 2f;
        var candidates = new List<Placement>(8);
        var minimumHeight = float.MaxValue;
        foreach (var prefab in prefabs)
        {
            if (prefab.Index != 0 && prefab.Index != side)
            {
                continue;
            }
            var box = prefab.GetComponent<BoxCollider2D>();
            var size = Vector2.Scale(box.size, prefab.transform.localScale);
            // 가장 넓은 6차선 옆에서도 다른 도로 구역을 침범하지 않도록 필요한 만큼만 축소한다.
            var scale = Mathf.Min(1f, availableWidth / size.x);
            if (scale <= 0f)
            {
                return false;
            }
            candidates.Add(new Placement(prefab, Vector2.zero, scale));
            minimumHeight = Mathf.Min(minimumHeight, size.y * scale);
        }
        for (var attempt = 0; attempt < 32; attempt++)
        {
            // 작은 건물 5개를 선택할 때도 최소 간격과 양쪽 경계의 연속 제한을 함께 검사한다.
            var chosen = new List<Placement>(count);
            var totalHeight = 0f;
            for (var slot = 0; slot < count; slot++)
            {
                var allowed = new List<Placement>(8);
                foreach (var candidate in candidates)
                {
                    var height = Height(candidate);
                    if (totalHeight + height + (count - slot - 1) * minimumHeight + (count - 1) * BuildingGap > availableLength)
                    {
                        continue;
                    }
                    var sequence = new List<BuildingPrefab>();
                    foreach (var item in lower)
                    {
                        sequence.Add(item.Prefab);
                    }
                    foreach (var item in chosen)
                    {
                        sequence.Add(item.Prefab);
                    }
                    sequence.Add(candidate.Prefab);
                    if (slot == count - 1)
                    {
                        foreach (var item in upper)
                        {
                            sequence.Add(item.Prefab);
                        }
                    }
                    var valid = true;
                    for (var index = 2; index < sequence.Count; index++)
                    {
                        if (sequence[index] == sequence[index - 1] && sequence[index] == sequence[index - 2])
                        {
                            valid = false;
                            break;
                        }
                    }
                    if (valid)
                    {
                        allowed.Add(candidate);
                    }
                }
                if (allowed.Count == 0)
                {
                    break;
                }
                var selected = allowed[random.Next(allowed.Count)];
                chosen.Add(selected);
                totalHeight += Height(selected);
            }
            if (chosen.Count != count)
            {
                continue;
            }
            // 여분의 길이를 건물 사이와 끝에 균등 배분해 박스 전체가 자기 도로 구역 안에 있게 한다.
            var gap = (availableLength - totalHeight - (count - 1) * BuildingGap) / (count + 1);
            var y = -road.Prefab.ImageSize.y * 0.5f + EndGap + gap;
            foreach (var chosenItem in chosen)
            {
                var box = chosenItem.Prefab.GetComponent<BoxCollider2D>();
                var scale = (Vector2)chosenItem.Prefab.transform.localScale * chosenItem.Scale;
                var size = Vector2.Scale(box.size, scale);
                var center = new Vector2((side == 1 ? -1f : 1f) * (edge + SideGap + size.x * 0.5f), y + size.y * 0.5f);
                output.Add(new Placement(chosenItem.Prefab, center - Vector2.Scale(box.offset, scale), chosenItem.Scale));
                y += size.y + BuildingGap + gap;
            }
            return true;
        }
        return false;
    }

    /// <summary>콜라이더 전체에 스케일 보정을 적용한 도로 방향 길이를 반환한다.</summary>
    private static float Height(Placement placement)
    {
        return placement.Prefab.GetComponent<BoxCollider2D>().size.y * placement.Prefab.transform.localScale.y * placement.Scale;
    }
}
