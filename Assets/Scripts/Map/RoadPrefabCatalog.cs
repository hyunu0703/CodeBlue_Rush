using UnityEngine;

[CreateAssetMenu(menuName = "CodeBlue Rush/Road Prefab Catalog")]
public sealed class RoadPrefabCatalog : ScriptableObject
{
    // 고정 Index 순서로 저장한 도로 프리팹 참조. 런타임에서 원본 목록을 수정하지 않는다.
    [SerializeField] private RoadPrefab[] prefabs;

    /// <summary>저장된 프리팹 참조의 개수를 반환한다.</summary>
    public int Count => prefabs == null ? 0 : prefabs.Length;

    /// <summary>실제 플레이에서 생성할 수 있는 4·6차선 도로만 허용한다. 기존 식별자와 원본 에셋은 보존한다.</summary>
    public static bool IsPlayableRoad(RoadIndex index)
    {
        switch (index)
        {
            case RoadIndex.Straight4Lane:
            case RoadIndex.Straight6Lane:
            case RoadIndex.Cross4Lane:
            case RoadIndex.Cross6Lane:
            case RoadIndex.T4Lane:
            case RoadIndex.T6Lane:
            case RoadIndex.TV4LaneH6Lane:
            case RoadIndex.TV6LaneH4Lane:
                return true;
            default:
                return false;
        }
    }

    /// <summary>생성 가능한 살아 있는 프리팹만 출력한다. 금지 도로, 2차선 연결부, 누락은 false와 null을 반환한다.</summary>
    public bool TryGetPrefab(RoadIndex index, out RoadPrefab prefab)
    {
        if (prefabs != null && IsPlayableRoad(index))
        {
            // candidate는 목록의 조회 후보이며 에셋을 생성하거나 수정하지 않는다.
            foreach (var candidate in prefabs)
            {
                if (candidate != null && candidate.Index == index)
                {
                    // 허용된 식별자라도 연결부에 2차선이 있으면 주행 경로와 추가 분기 모두에서 제외한다.
                    for (var direction = 0; direction < 4; direction++)
                    {
                        if (candidate.GetLaneCount((RoadDirection)direction) == 2)
                        {
                            prefab = null;
                            return false;
                        }
                    }
                    prefab = candidate;
                    return true;
                }
            }
        }
        prefab = null;
        return false;
    }
}
