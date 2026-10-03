using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>완성된 도시의 도로 외부에 지형 시각 자료만 배치하고 생성 수명을 소유한다</summary>
[DisallowMultipleComponent]
public sealed class CityTerrain : MonoBehaviour
{
    [SerializeField] private CityMap map;
    [SerializeField] private GameObject ground;
    [SerializeField] private GameObject grassBright;
    [SerializeField] private GameObject grassDark;
    [SerializeField] private GameObject grassPark;
    [SerializeField] private GameObject dirtPath;
    [SerializeField] private GameObject sea;
    [SerializeField] private GameObject shallow;
    [SerializeField] private GameObject pond;
    [SerializeField] private GameObject harbor;
    [SerializeField] private GameObject coastStraight;
    [SerializeField] private GameObject coastCurve;
    [SerializeField] private GameObject rockSmall;
    [SerializeField] private GameObject rockMedium;
    [SerializeField] private GameObject rockCoastal;
    private GameObject root;
    private int seed;
    private MaterialPropertyBlock surface;
    private readonly HashSet<Vector2Int> ponds = new HashSet<Vector2Int>();
    public bool IsReady => root && map && map.IsReady && seed == map.Seed;
    public event Action<CityTerrain> Rebuilt;
    public int ParkCount { get; private set; }
    public int PondCount { get; private set; }
    public int RockCount { get; private set; }

    // 이미 준비된 도시도 처리하고 이후 도시 교체를 구독한다
    private void OnEnable()
    {
        if (!map) return;
        map.Generated += Rebuild;
        if (root) root.SetActive(true);
        if (map.IsReady && (!root || seed != map.Seed)) Rebuild(map);
    }

    // 컴포넌트 비활성화에서는 구독과 소유한 표시를 함께 해제한다
    private void OnDisable()
    {
        if (map) map.Generated -= Rebuild;
        if (root) root.SetActive(false);
    }

    // 생성 완료 이벤트 또는 에디터 미리보기에서 기존 표시를 교체한다
    public void Rebuild(CityMap city)
    {
        if (!isActiveAndEnabled || !city || city != map || !city.IsReady) return;
        if (!ground || !grassBright || !grassDark || !grassPark || !dirtPath || !sea || !shallow || !pond || !harbor || !coastStraight || !coastCurve || !rockSmall || !rockMedium || !rockCoastal)
        {
            Debug.LogError("CityTerrain 지형 Prefab 참조가 누락되었습니다", this);
            return;
        }
        if (root)
        {
            root.SetActive(false);
            if (Application.isPlaying) Destroy(root); else DestroyImmediate(root);
        }
        root = new GameObject("Terrain");
        root.transform.SetParent(map.transform, false);
        seed = city.Seed;
        ParkCount = PondCount = RockCount = 0;
        ponds.Clear();
        surface = new MaterialPropertyBlock();
        Matrix4x4 inverse = map.transform.worldToLocalMatrix;
        surface.SetVector("_MapRowX", inverse.GetRow(0));
        surface.SetVector("_MapRowY", inverse.GetRow(1));
        surface.SetFloat("_CellSize", city.CellSize);
        surface.SetFloat("_ShoreY", -0.7f * city.CellSize);
        int harborX = 1 + (int)((unchecked((uint)city.Seed) >> 8) % (uint)(city.Layout.Width - 2));
        surface.SetFloat("_HarborX", harborX * city.CellSize);
        surface.SetColor("_NearColor", shallow.GetComponent<MeshRenderer>().sharedMaterial.GetColor("_Color"));
        surface.SetColor("_HarborColor", harbor.GetComponent<MeshRenderer>().sharedMaterial.GetColor("_Color"));
        float cell = city.CellSize;
        Vector2 center = new Vector2((city.Layout.Width - 1) * cell * 0.5f, (city.Layout.Height - 1) * cell * 0.5f);
        GameObject floor = Place(ground, center, 0f);
        floor.transform.localScale = new Vector3(city.Layout.Width + 4f, city.Layout.Height + 6f, 1f) * (cell / 20f);
        floor.transform.localScale = new Vector3(floor.transform.localScale.x, floor.transform.localScale.y, 1f);
        AddSea(city);
        uint state = unchecked((uint)city.Seed) ^ 0x54E22A1u;
        for (int y = 1; y < city.Layout.Height - 1; y++)
        {
            for (int x = 1; x < city.Layout.Width - 1; x++)
            {
                if (city.GetRoad(x, y)) continue;
                AddPark(new Vector2(x * cell, y * cell), ref state);
            }
        }
        Rebuilt?.Invoke(this);
    }

    // 식생 배치에서 수면을 피할 수 있도록 연못 셀만 조회한다
    public bool IsPond(int x, int y)
    {
        return ponds.Contains(new Vector2Int(x, y));
    }

    // 남쪽 외곽에 동일한 폭의 해안과 연속 수면을 조립한다
    private void AddSea(CityMap city)
    {
        float cell = city.CellSize;
        float shore = -0.7f * cell;
        float center = (city.Layout.Width - 1) * cell * 0.5f;
        GameObject water = Place(sea, new Vector2(center, shore - 2f * cell), 0f);
        water.transform.localScale = new Vector3((city.Layout.Width + 4f) * cell / 20f, 3f * cell / 20f, 1f);
        for (int x = -2; x < city.Layout.Width + 2; x++)
        {
            bool curved = x == city.Layout.Width / 2;
            GameObject coast = Place(curved ? coastCurve : coastStraight, new Vector2(x * cell, shore), -90f);
            foreach (MeshRenderer renderer in coast.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.name != "Water") continue;
                MeshRenderer source = sea.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = source.sharedMaterial;
            }
            if (x < 0 || x >= city.Layout.Width) continue;
            GameObject stone = Place(rockCoastal, new Vector2(x * cell + cell * 0.3f, shore - cell * 0.12f), 0f);
            stone.transform.localScale *= 0.75f;
            RockCount++;
        }
    }

    // 빈 셀의 중앙만 사용해 도로변 건물과 보행 경로를 보존한다
    private void AddPark(Vector2 point, ref uint state)
    {
        int kind = Next(ref state, 5);
        Place(kind == 0 ? grassDark : kind == 1 ? grassBright : grassPark, point, 0f);
        ParkCount++;
        if (PondCount < 2 && (kind == 2 || ParkCount == 1))
        {
            Place(pond, point, 0f);
            ponds.Add(new Vector2Int(Mathf.RoundToInt(point.x / map.CellSize), Mathf.RoundToInt(point.y / map.CellSize)));
            PondCount++;
        }
        else
        {
            Place(dirtPath, point, Next(ref state, 2) * 90f);
            Place(rockSmall, point + new Vector2(-4.5f, 3f) * (map.CellSize / 20f), 0f);
            Place(rockMedium, point + new Vector2(4.5f, -3f) * (map.CellSize / 20f), 0f);
            RockCount += 2;
        }
    }

    // 같은 시각 Prefab을 셀 규격으로 배치하고 반복 UV 좌표계를 공유한다
    private GameObject Place(GameObject prefab, Vector2 point, float angle)
    {
        GameObject instance = Instantiate(prefab, root.transform, false);
        instance.name = prefab.name;
        instance.transform.localPosition = point;
        instance.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        instance.transform.localScale *= map.CellSize / 20f;
        foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>()) renderer.SetPropertyBlock(surface);
        return instance;
    }

    // 지형만의 난수 상태를 사용해 도로와 교통 난수를 변경하지 않는다
    private static int Next(ref uint state, int limit)
    {
        state = unchecked(state * 1664525u + 1013904223u);
        return (int)((state >> 8) % (uint)limit);
    }

    // CityMap과 별개로 컴포넌트만 제거해도 생성한 자식을 정리한다
    private void OnDestroy()
    {
        if (!root) return;
        if (Application.isPlaying) Destroy(root); else DestroyImmediate(root);
    }
}
