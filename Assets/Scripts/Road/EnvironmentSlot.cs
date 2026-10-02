using System;
using UnityEngine;

/// <summary>도로 Prefab이 지정한 환경 배치 위치와 차량 접근 지점을 제공한다</summary>
public sealed class EnvironmentSlot : MonoBehaviour
{
    [Flags] public enum Usage { Building = 1, Decoration = 2, Hospital = 4, Incident = 8 }
    [SerializeField] private RoadChunk road;
    [SerializeField] private Usage usage;
    [SerializeField] private TrafficLane lane;
    [SerializeField, Min(0f)] private float distance;
    [SerializeField] private GameObject[] variants = Array.Empty<GameObject>();
    public RoadChunk Road => road;
    public Usage Uses => usage;
    public TrafficLane Lane => lane;
    public float Distance => distance;

    // 현재 도시의 활성 도로변 접근 위치인지 검사한다
    public bool IsValidFor(CityMap map, Usage required)
    {
        return isActiveAndEnabled && road && road.isActiveAndEnabled && road.ConnectionCount == 2 && (usage & required) == required && map && map.ContainsLane(lane) && lane.transform.IsChildOf(road.transform) && float.IsFinite(distance) && distance >= 0f && distance <= lane.Length && lane.TrySample(distance, out Vector3 access, out _) && (access - transform.position).sqrMagnitude <= 9f;
    }

    // 도로 그래프와 독립적인 난수로 지정된 Placeholder 하나만 배치한다
    internal void Populate(ref uint state)
    {
        if (!isActiveAndEnabled || (usage & (Usage.Building | Usage.Decoration)) == 0 || variants == null || variants.Length == 0)
            return;
        state = unchecked(state * 1664525u + 1013904223u);
        GameObject prefab = variants[(state >> 8) % (uint)variants.Length];
        if (prefab)
            Instantiate(prefab, transform, false);
    }
}
