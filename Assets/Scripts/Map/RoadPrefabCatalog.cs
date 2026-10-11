using UnityEngine;

[CreateAssetMenu(menuName = "CodeBlue Rush/Road Prefab Catalog")]
public sealed class RoadPrefabCatalog : ScriptableObject
{
    // 고정 Index 순서로 저장한 도로 프리팹 참조. 런타임에서 원본 목록을 수정하지 않는다.
    [SerializeField] private RoadPrefab[] prefabs;

    /// <summary>저장된 프리팹 참조의 개수를 반환한다.</summary>
    public int Count => prefabs == null ? 0 : prefabs.Length;

    /// <summary>Index에 해당하는 살아 있는 프리팹을 출력한다. 누락 또는 None은 false와 null을 반환한다.</summary>
    public bool TryGetPrefab(RoadIndex index, out RoadPrefab prefab)
    {
        if (prefabs != null && index != RoadIndex.None)
        {
            // candidate는 목록의 조회 후보이며 에셋을 생성하거나 수정하지 않는다.
            foreach (var candidate in prefabs)
            {
                if (candidate != null && candidate.Index == index)
                {
                    prefab = candidate;
                    return true;
                }
            }
        }
        prefab = null;
        return false;
    }
}
