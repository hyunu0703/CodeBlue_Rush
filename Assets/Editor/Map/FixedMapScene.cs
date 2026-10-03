using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>저장된 고정 World의 Scene 참조를 편집 시 연결하고 검증용 복제본을 제공한다</summary>
public static class FixedMapScene
{
    public const string WorldPath = "Assets/Prefabs/SeoulWorld.prefab";

    // 완성된 World Prefab을 편집 또는 검증 Scene에 배치한다
    internal static void AttachWorld(CityMap map)
    {
        Transform world = map.transform.Find("World");
        if (!world)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorldPath);
            if (!prefab) throw new InvalidOperationException("고정 World Prefab이 없습니다.");
            world = (Application.isPlaying ? Object.Instantiate(prefab, map.transform) : (GameObject)PrefabUtility.InstantiatePrefab(prefab, map.transform)).transform;
            world.name = "World";
        }
        BindWorld(map, world);
    }

    // 저장된 격자 도로와 직접 연결 참조를 캐시에 등록한다
    private static void BindWorld(CityMap map, Transform world)
    {
        var roads = new RoadChunk[map.Width * map.Height];
        foreach (RoadChunk road in world.GetComponentsInChildren<RoadChunk>(true))
        {
            Vector3 point = map.transform.InverseTransformPoint(road.transform.position);
            int x = Mathf.RoundToInt(point.x / map.CellSize);
            int y = Mathf.RoundToInt(point.y / map.CellSize);
            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height || roads[y * map.Width + x])
                throw new InvalidOperationException("고정 도로 격자 중복 또는 범위 오류: " + road.name);
            roads[y * map.Width + x] = road;
        }
        SetArray(map, "fixedRoads", roads);
        SetArray(map, "fixedLanes", world.GetComponentsInChildren<TrafficLane>(true));
        SetArray(map, "fixedSidewalks", world.GetComponentsInChildren<SidewalkPath>(true));
        SetArray(map, "fixedTurnZones", world.GetComponentsInChildren<CameraTurnZone>(true));
        Transform points = map.transform.Find("PatientSpawnPoints");
        SetArray(map, "patientPoints", points ? points.GetComponentsInChildren<EnvironmentSlot>() :
            world.GetComponentsInChildren<EnvironmentSlot>().Where(s => (s.Uses & EnvironmentSlot.Usage.Incident) != 0).ToArray());
    }

    // 레거시 검증에서 파괴한 고정 데이터만 같은 저장본으로 다시 읽는다
    internal static bool ReloadFixture(CityMap map, out string error)
    {
        map.enabled = false;
        Transform world = map.transform.Find("World");
        if (world) Object.DestroyImmediate(world.gameObject);
        AttachWorld(map);
        map.enabled = true;
        return map.Initialize(out error);
    }

    // 병원은 편집 시 선택한 접근 Slot을 직접 참조한다
    internal static void PlaceHospital(HospitalArrivalZone hospital, CityMap map, int index = 0)
    {
        EnvironmentSlot[] points = map.GetComponentsInChildren<EnvironmentSlot>()
            .Where(s => (s.Uses & EnvironmentSlot.Usage.Hospital) != 0).ToArray();
        if (points.Length == 0) throw new InvalidOperationException("고정 병원 접근 지점이 없습니다.");
        EnvironmentSlot slot = points[Mathf.Clamp(index, 0, points.Length - 1)];
        slot.Lane.TrySample(slot.Distance, out _, out Vector3 direction);
        hospital.transform.SetPositionAndRotation(slot.transform.position, Quaternion.FromToRotation(Vector3.up, direction));
        hospital.Configure(map, slot);
        PrefabUtility.RecordPrefabInstancePropertyModifications(hospital);
        PrefabUtility.RecordPrefabInstancePropertyModifications(hospital.transform);
        Transform transfer = hospital.transform.Find("HospitalTransferPoint");
        if (!transfer)
        {
            transfer = new GameObject("HospitalTransferPoint").transform;
            transfer.SetParent(hospital.transform, false);
        }
    }

    // 기존 도로변의 소방서 출입 위치를 고정 Spawn으로 연결한다
    internal static void ConnectSpawn(GameFlow flow, CityMap map, AmbulanceController ambulance, AmbulanceCamera camera)
    {
        Transform station = GameObject.Find("FireStation")?.transform;
        if (!station)
        {
            station = new GameObject("FireStation").transform;
            RoadChunk road = map.GetRoad(0, 1);
            if (!road) throw new InvalidOperationException("소방서 진입 도로가 없습니다.");
            TrafficLane lane = road.GetLane(0);
            lane.TrySample(lane.Length * 0.5f, out Vector3 position, out Vector3 direction);
            station.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, direction));
            Transform spawn = new GameObject("AmbulanceSpawnPoint").transform;
            spawn.SetParent(station, false);
            // 기존 건물 표현을 사용하며 삭제된 지형 Prefab은 복구하지 않는다
            GameObject building = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Building_Medium.prefab");
            if (building)
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(building, station);
                visual.name = "FireStationBuilding";
                visual.transform.localPosition = new Vector3(-8f, 0f, 0f);
            }
        }
        Transform point = station.Find("AmbulanceSpawnPoint");
        Set(flow, "ambulanceSpawnPoint", point);
        Set(flow, "ambulanceCamera", camera);
        ambulance.transform.SetPositionAndRotation(point.position, point.rotation);
        PrefabUtility.RecordPrefabInstancePropertyModifications(ambulance.transform);
    }

    // 같은 Scene 환자 후보를 명시적인 편집 목록으로 작성한다
    private static void CreatePatientPoints(CityMap map, HospitalArrivalZone hospital)
    {
        Transform existing = map.transform.Find("PatientSpawnPoints");
        if (existing)
        {
            if (existing.GetComponentsInChildren<EnvironmentSlot>().All(point => point.IsValidFor(map, EnvironmentSlot.Usage.Incident))) return;
            Object.DestroyImmediate(existing.gameObject);
        }
        Transform root = new GameObject("PatientSpawnPoints").transform;
        root.SetParent(map.transform, false);
        EnvironmentSlot[] sources = map.transform.Find("World").GetComponentsInChildren<EnvironmentSlot>()
            .Where(s => (s.Uses & EnvironmentSlot.Usage.Incident) != 0 && Vector3.Distance(s.transform.position, hospital.HospitalPoint) > 10f).ToArray();
        int count = Mathf.Min(12, sources.Length);
        for (int i = 0; i < count; i++)
        {
            EnvironmentSlot source = sources[i * sources.Length / count];
            EnvironmentSlot point = new GameObject("Point_" + (i + 1).ToString("00")).AddComponent<EnvironmentSlot>();
            point.transform.SetParent(root, false);
            point.transform.position = source.transform.position;
            EditorUtility.CopySerialized(source, point);
        }
    }

    // 삭제된 Prefab을 재생성하지 않고 남아 있는 재질로 고정 표시를 복구한다
    private static void RepairWorldMaterials()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(WorldPath);
        try
        {
            AddPassingRoads(root);
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.sharedMaterial) continue;
                string name = renderer.transform.parent ? renderer.transform.parent.name + renderer.name : renderer.name;
                string material = name.Contains("Water") ? "Water_Sea" : name.Contains("Coast") || name.Contains("Shore") ? "Coast" :
                    name.Contains("Dirt") ? "Dirt" : name.Contains("Park") ? "Grass_Park" : "Grass_Default";
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/MapAssets/Terrain/" + material + ".mat");
                if (!renderer.sharedMaterial) throw new InvalidOperationException("남아 있는 지형 재질이 없습니다: " + renderer.name);
            }
            ClearMissingPalette(root);
            PrefabUtility.SaveAsPrefabAsset(root, WorldPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // 고정 도로 두 구간에 기존 차선 변경 Prefab을 연결하고 편집 시 저장한다
    private static void AddPassingRoads(GameObject root)
    {
        if (root.GetComponentsInChildren<TrafficLane>().Any(lane => lane.LeftLane || lane.RightLane)) return;
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/City/PassingStraight.prefab");
        RoadChunk[] roads = root.GetComponentsInChildren<RoadChunk>().Where(road =>
            road.transform.localPosition.x > 150f && road.ConnectionCount == 2 &&
            Vector3.Dot(road.GetConnection(0).Outward, road.GetConnection(1).Outward) < -0.99f).Take(2).ToArray();
        foreach (RoadChunk road in roads)
        {
            Vector3 position = road.transform.position;
            Quaternion rotation = road.transform.rotation;
            Transform parent = road.transform.parent;
            string name = road.name;
            foreach (RoadConnection port in road.GetComponentsInChildren<RoadConnection>())
                port.Disconnect();
            Object.DestroyImmediate(road.gameObject);
            var replacement = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            replacement.name = name + "_Passing";
            replacement.transform.SetPositionAndRotation(position, rotation);
            PrefabUtility.UnpackPrefabInstance(replacement, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }
        RoadConnection[] ports = root.GetComponentsInChildren<RoadConnection>();
        foreach (RoadConnection port in ports)
        {
            if (port.ConnectedTo) continue;
            RoadConnection other = ports.FirstOrDefault(candidate => candidate != port &&
                Vector3.Distance(candidate.Position, port.Position) < 0.05f &&
                Vector3.Dot(candidate.Outward, port.Outward) < -0.99f);
            if (!other || !port.TryConnect(other, out string error))
                throw new InvalidOperationException("고정 복수 차선 연결 실패: " + port.name);
        }
        SidewalkPath[] walks = root.GetComponentsInChildren<SidewalkPath>();
        foreach (SidewalkPath walk in walks)
        {
            var links = new List<SidewalkPath>();
            foreach (SidewalkPath other in walks)
            {
                if (other == walk) continue;
                bool shared = false;
                for (int a = 0; a < walk.PointCount && !shared; a++)
                    for (int b = 0; b < other.PointCount && !shared; b++)
                        shared = Vector3.Distance(walk.GetPoint(a), other.GetPoint(b)) < 0.01f;
                if (shared) links.Add(other);
            }
            SetArray(walk, "next", links.ToArray());
        }
    }

    // 삭제된 편집용 배치 후보의 참조만 제거한다
    private static void ClearMissingPalette(GameObject root)
    {
        foreach (EnvironmentSlot slot in root.GetComponentsInChildren<EnvironmentSlot>(true))
        {
            var data = new SerializedObject(slot);
            var variants = data.FindProperty("variants");
            for (int i = variants.arraySize - 1; i >= 0; i--)
                if (!variants.GetArrayElementAtIndex(i).objectReferenceValue)
                    variants.DeleteArrayElementAtIndex(i);
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    // 기존 Gameplay와 보조 Scene에 같은 고정 World를 연결한다
    [MenuItem("CodeBlue Rush/Gameplay/Connect Fixed Map")]
    public static void ConnectScenes()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveOpenScenes())
            throw new InvalidOperationException("현재 Scene을 저장하지 못했습니다.");
        RepairWorldMaterials();
        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var scene = EditorSceneManager.OpenScene(path);
            CityMap map = Object.FindFirstObjectByType<CityMap>();
            if (map)
            {
                map.enabled = false;
                AttachWorld(map);
                map.enabled = true;
                foreach (HospitalArrivalZone hospital in Object.FindObjectsByType<HospitalArrivalZone>(FindObjectsSortMode.None))
                    PlaceHospital(hospital, map);
                GameFlow flow = Object.FindFirstObjectByType<GameFlow>();
                if (flow)
                {
                    HospitalArrivalZone hospital = Object.FindFirstObjectByType<HospitalArrivalZone>();
                    CreatePatientPoints(map, hospital);
                    BindWorld(map, map.transform.Find("World"));
                    ConnectSpawn(flow, map, Object.FindFirstObjectByType<AmbulanceController>(), Object.FindFirstObjectByType<AmbulanceCamera>());
                }
                map.BindCamera(Object.FindFirstObjectByType<AmbulanceCamera>());
            }
            else
            {
                // 기존 RoadDemo도 저장된 Target의 Next를 편집 시 확정한다
                foreach (RoadConnection port in Object.FindObjectsByType<RoadConnection>(FindObjectsSortMode.None))
                    if (port.ConnectedTo && !port.TryConnect(port.ConnectedTo, out string error))
                        throw new InvalidOperationException(error);
            }
            EditorSceneManager.SaveScene(scene);
        }
        EditorSceneManager.OpenScene(GameplayScene.Path);
        AssetDatabase.SaveAssets();
        System.IO.File.WriteAllText("Logs/FixedMapMigration/connected.txt", "PASS: fixed scene references saved");
    }

    // 직렬화 참조 하나를 편집한다
    internal static void Set(Object target, string field, Object value)
    {
        var data = new SerializedObject(target);
        data.FindProperty(field).objectReferenceValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    // 직렬화된 직접 참조 배열을 저장한다
    private static void SetArray(Object target, string field, Object[] values)
    {
        var data = new SerializedObject(target);
        var array = data.FindProperty(field);
        array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        data.ApplyModifiedPropertiesWithoutUndo();
    }
}

