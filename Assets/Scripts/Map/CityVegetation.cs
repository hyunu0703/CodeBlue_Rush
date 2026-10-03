using UnityEngine;

/// <summary>완성된 지형에 재사용 식생만 배치하고 도시 교체 시 생성한 표시를 정리한다</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CityTerrain))]
public sealed class CityVegetation : MonoBehaviour
{
    [SerializeField] private CityMap map;
    [SerializeField] private CityTerrain terrain;
    [SerializeField] private GameObject treeSmall;
    [SerializeField] private GameObject treeMedium;
    [SerializeField] private GameObject treeLarge;
    [SerializeField] private GameObject bushRound;
    [SerializeField] private GameObject bushLow;
    [SerializeField] private GameObject hedgeShort;
    [SerializeField] private GameObject hedgeLong;
    [SerializeField] private GameObject flowerPatchPink;
    [SerializeField] private GameObject flowerPatchYellow;
    [SerializeField] private GameObject flowerBush;
    [SerializeField] private GameObject flowerBedSmall;
    [SerializeField] private GameObject flowerBedMedium;
    [SerializeField] private GameObject planter;
    private GameObject root;
    private int seed;
    public int PlantCount { get; private set; }
    public int ParkCount { get; private set; }
    public int StreetscapeCount { get; private set; }

    // 지형의 완료 이벤트를 구독해 컴포넌트 초기화 순서에 의존하지 않는다
    private void OnEnable()
    {
        if (!map || !terrain) return;
        terrain.Rebuilt += Rebuild;
        if (root) root.SetActive(true);
        if (terrain.IsReady && (!root || seed != map.Seed)) Rebuild(terrain);
    }

    // 비활성 상태에서 이벤트와 소유한 표시를 함께 해제한다
    private void OnDisable()
    {
        if (terrain) terrain.Rebuilt -= Rebuild;
        if (root) root.SetActive(false);
    }

    // 교통이나 지형의 난수를 변경하지 않고 식생 표시만 다시 구성한다
    public void Rebuild(CityTerrain source)
    {
        if (!isActiveAndEnabled || source != terrain || !map || !terrain.IsReady) return;
        if (!treeSmall || !treeMedium || !treeLarge || !bushRound || !bushLow || !hedgeShort || !hedgeLong || !flowerPatchPink || !flowerPatchYellow || !flowerBush || !flowerBedSmall || !flowerBedMedium || !planter)
        {
            Debug.LogError("CityVegetation 식생 Prefab 참조가 누락되었습니다", this);
            return;
        }
        Release();
        root = new GameObject("Vegetation");
        root.transform.SetParent(map.transform, false);
        seed = map.Seed;
        PlantCount = ParkCount = StreetscapeCount = 0;
        uint state = unchecked((uint)seed) ^ 0xC41A6E5u;
        for (int y = 1; y < map.Layout.Height - 1; y++)
        {
            for (int x = 1; x < map.Layout.Width - 1; x++)
            {
                if (!map.GetRoad(x, y)) AddPark(x, y, ref state);
            }
        }
        AddStreetscape(ref state);
    }

    // 연못과 흙길 및 바위의 위치를 피하고 빈 셀 안쪽에 공원 식생을 배치한다
    private void AddPark(int x, int y, ref uint state)
    {
        Vector2 center = new Vector2(x, y) * map.CellSize;
        float unit = map.CellSize / 20f;
        bool water = terrain.IsPond(x, y);
        Place(treeSmall, center + new Vector2(-7f, -7f) * unit, 0.95f + Next(ref state, 10) * 0.01f);
        Place(treeMedium, center + new Vector2(7f, -7f) * unit, 1f);
        Place(treeLarge, center + new Vector2(7f, 7f) * unit, 0.95f);
        Place(bushRound, center + new Vector2(-7f, 0f) * unit, 1f);
        Place(bushLow, center + new Vector2(7f, 0f) * unit, 1f);
        Place(flowerPatchPink, center + new Vector2(water ? -7f : -5f, water ? 3f : -5f) * unit, 1f);
        Place(flowerPatchYellow, center + new Vector2(water ? 7f : 5f, water ? -3f : 5f) * unit, 1f);
        Place(flowerBush, center + new Vector2(-7f, 7f) * unit, 1f);
        Place(flowerBedSmall, center + new Vector2(water ? -7.6f : -6.5f, -3f) * unit, 1f);
        Place(flowerBedMedium, center + new Vector2(water ? 7.8f : 6.5f, 3f) * unit, 1f);
        ParkCount++;
    }

    // 직선 도로의 기존 건물과 장식 사이 여유 공간만 사용한다
    private void AddStreetscape(ref uint state)
    {
        float unit = map.CellSize / 20f;
        for (int y = 0; y < map.Layout.Height; y++)
        {
            for (int x = 0; x < map.Layout.Width; x++)
            {
                RoadChunk road = map.GetRoad(x, y);
                if (!road || road.ConnectionCount != 2 || map.Layout.GetMask(x, y) != 5 && map.Layout.GetMask(x, y) != 10) continue;
                bool residential = Next(ref state, 3) == 0;
                EnvironmentSlot[] slots = road.GetComponentsInChildren<EnvironmentSlot>();
                for (int side = -1; side <= 1; side += 2)
                {
                    if (IsReserved(road, slots, side)) continue;
                    Vector3 point = road.transform.TransformPoint(new Vector3(side * 8f * unit, (residential ? 2.2f : 2.5f) * unit, 0f));
                    Place(residential ? hedgeShort : planter, map.transform.InverseTransformPoint(point), residential ? 0.7f : 0.8f);
                    point = road.transform.TransformPoint(new Vector3(side * 8f * unit, -2.5f * unit, 0f));
                    Place(bushLow, map.transform.InverseTransformPoint(point), 0.7f);
                    StreetscapeCount += 2;
                }
                // 주거지 외곽의 추가 생울타리는 도로와 건물에서 더 먼 쪽에 배치한다
                if (residential && (x == 0 || x == map.Layout.Width - 1 || y == 0 || y == map.Layout.Height - 1))
                {
                    Vector2 outward = x == 0 ? Vector2.left : x == map.Layout.Width - 1 ? Vector2.right : y == 0 ? Vector2.down : Vector2.up;
                    Place(hedgeLong, new Vector2(x, y) * map.CellSize + outward * map.CellSize * (outward.y < 0f ? 0.59f : 0.62f), 1f);
                    StreetscapeCount++;
                }
            }
        }
    }

    // 버스와 주차 구역처럼 이미 넓게 사용 중인 도로변 공간을 보존한다
    private static bool IsReserved(RoadChunk road, EnvironmentSlot[] slots, int side)
    {
        foreach (EnvironmentSlot slot in slots)
        {
            if ((slot.Uses & EnvironmentSlot.Usage.Decoration) == 0) continue;
            float x = road.transform.InverseTransformPoint(slot.transform.position).x;
            if (Mathf.Sign(x) != side) continue;
            foreach (SpriteRenderer renderer in slot.GetComponentsInChildren<SpriteRenderer>())
            {
                if (Mathf.Max(renderer.bounds.size.x, renderer.bounds.size.y) > 3f) return true;
            }
        }
        return false;
    }

    // 월드 크기와 광원 방향을 보존하며 독립 Prefab을 배치한다
    private void Place(GameObject prefab, Vector2 point, float scale)
    {
        GameObject instance = Instantiate(prefab, root.transform, false);
        instance.name = prefab.name;
        instance.transform.localPosition = point;
        instance.transform.rotation = Quaternion.identity;
        instance.transform.localScale *= scale * map.CellSize / 20f;
        PlantCount++;
    }

    // 식생만의 정수 난수 상태를 진행한다
    private static int Next(ref uint state, int limit)
    {
        state = unchecked(state * 1664525u + 1013904223u);
        return (int)((state >> 8) % (uint)limit);
    }

    // 기존 생성 루트를 숨기고 실행 상태에 맞게 제거한다
    private void Release()
    {
        if (!root) return;
        root.SetActive(false);
        if (Application.isPlaying) Destroy(root); else DestroyImmediate(root);
        root = null;
    }

    // 컴포넌트만 제거되어도 식생 인스턴스를 정리한다
    private void OnDestroy()
    {
        Release();
    }
}
