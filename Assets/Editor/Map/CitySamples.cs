using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>기존 도로를 재사용하는 격자 규격 Prefab과 교차로 경로 및 도시 예제 씬을 작성한다</summary>
public static class CitySamples
{
    internal const string Folder = "Assets/Prefabs/City";

    // 원본 도로는 보존하고 없는 격자 도로 예제만 생성한다
    [MenuItem("CodeBlue Rush/City/Create Missing Prefabs")]
    public static void CreatePrefabs()
    {
        RoadSamples.CreateSamples();
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/Prefabs", "City");
        CreateAligned("Straight");
        CreateAligned("Corner");
        CreateJunction("TJunction", 7);
        CreateJunction("Intersection", 15);
        AssetDatabase.SaveAssets();
    }

    // 2단계 직선과 코너의 원점만 셀 중심 규격으로 정렬한다
    private static void CreateAligned(string name)
    {
        string path = Folder + "/" + name + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path))
            return;
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Road/" + name + "2Lane.prefab");
        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
        try
        {
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = name;
            foreach (Transform child in root.transform)
                child.localPosition += Vector3.down * 10f;
            SaveRoad(root, path);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // 진입부와 진출부 및 모든 합법적 직진 좌우회전 경로가 있는 교차로를 작성한다
    private static void CreateJunction(string name, int mask)
    {
        string path = Folder + "/" + name + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path))
            return;
        GameObject root = new GameObject(name);
        try
        {
            RoadChunk chunk = root.AddComponent<RoadChunk>();
            List<TrafficLane> lanes = new List<TrafficLane>();
            List<RoadConnection> ports = new List<RoadConnection>();
            TrafficLane[] entries = new TrafficLane[4];
            TrafficLane[] exits = new TrafficLane[4];
            for (int d = 0; d < 4; d++)
            {
                if ((mask & (1 << d)) == 0)
                    continue;
                Vector2 outward = CityLayout.Direction(d);
                Vector2 right = new Vector2(outward.y, -outward.x);
                entries[d] = CreateLane(root.transform, "Entry" + d, new[] { outward * 10f - right, outward * 4f - right });
                exits[d] = CreateLane(root.transform, "Exit" + d, new[] { outward * 4f + right, outward * 10f + right });
                lanes.Add(entries[d]);
                lanes.Add(exits[d]);
                RoadConnection port = RoadSamples.CreatePort(root.transform, "Port" + d, outward * 10f, -90f * d);
                RoadSamples.SetArray(port, "incoming", new[] { entries[d] });
                RoadSamples.SetArray(port, "outgoing", new[] { exits[d] });
                ports.Add(port);
            }
            for (int from = 0; from < 4; from++)
            {
                if (!entries[from])
                    continue;
                List<TrafficLane> choices = new List<TrafficLane>();
                for (int to = 0; to < 4; to++)
                {
                    if (from == to || !exits[to])
                        continue;
                    TrafficLane route = CreateLane(root.transform, "Route" + from + "To" + to, GetCurve(entries[from], exits[to]));
                    RoadSamples.SetArray(route, "internalNext", new[] { exits[to] });
                    lanes.Add(route);
                    choices.Add(route);
                }
                RoadSamples.SetArray(entries[from], "internalNext", choices.ToArray());
            }
            RoadSamples.SetArray(chunk, "lanes", lanes.ToArray());
            RoadSamples.SetArray(chunk, "connections", ports.ToArray());
            CreateSurface(root, mask);
            SaveRoad(root, path);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // 지정한 로컬 경로로 차선 컴포넌트를 작성한다
    private static TrafficLane CreateLane(Transform parent, string name, Vector2[] points)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        TrafficLane lane = child.AddComponent<TrafficLane>();
        SerializedObject data = new SerializedObject(lane);
        SerializedProperty array = data.FindProperty("points");
        array.arraySize = points.Length;
        for (int i = 0; i < points.Length; i++)
            array.GetArrayElementAtIndex(i).vector2Value = points[i];
        data.ApplyModifiedPropertiesWithoutUndo();
        return lane;
    }

    // 진입 및 진출 접선에 맞춘 교차로 내부 베지어 경로를 표본화한다
    private static Vector2[] GetCurve(TrafficLane entry, TrafficLane exit)
    {
        Vector2 start = entry.EndPoint;
        Vector2 end = exit.StartPoint;
        Vector2 a = start + (Vector2)entry.EndDirection * 2.5f;
        Vector2 b = end - (Vector2)exit.StartDirection * 2.5f;
        Vector2[] points = new Vector2[25];
        for (int i = 0; i < points.Length; i++)
        {
            float t = (float)i / (points.Length - 1);
            float u = 1f - t;
            points[i] = u * u * u * start + 3f * u * u * t * a + 3f * u * t * t * b + t * t * t * end;
        }
        return points;
    }

    // 신호나 횡단보도 행동 없이 도로 접속부의 표면만 작성한다
    private static void CreateSurface(GameObject root, int mask)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Color> colors = new List<Color>();
        Color asphalt = new Color(0.16f, 0.18f, 0.2f);
        RoadSamples.AddStrip(new[] { new Vector2(-4f, -4f), new Vector2(-4f, 4f) }, new[] { new Vector2(4f, -4f), new Vector2(4f, 4f) }, asphalt, 0.02f, vertices, triangles, colors);
        for (int d = 0; d < 4; d++)
        {
            if ((mask & (1 << d)) == 0)
                continue;
            Vector2 axis = CityLayout.Direction(d);
            Vector2 right = new Vector2(axis.y, -axis.x);
            Vector2[] start = { axis * 4f - right * 2f, axis * 10f - right * 2f };
            Vector2[] end = { axis * 4f + right * 2f, axis * 10f + right * 2f };
            RoadSamples.AddStrip(start, end, asphalt, 0.02f, vertices, triangles, colors);
            RoadSamples.AddStrip(new[] { axis * 4f - right * 0.05f, axis * 10f - right * 0.05f }, new[] { axis * 4f + right * 0.05f, axis * 10f + right * 0.05f }, Color.yellow, 0f, vertices, triangles, colors);
            for (int side = -1; side <= 1; side += 2)
                RoadSamples.AddStrip(new[] { axis * 4f + right * (side * 2f - 0.04f), axis * 10f + right * (side * 2f - 0.04f) }, new[] { axis * 4f + right * (side * 2f + 0.04f), axis * 10f + right * (side * 2f + 0.04f) }, Color.white, 0f, vertices, triangles, colors);
        }
        Mesh mesh = new Mesh { name = root.name + "Surface" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetColors(colors);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        string path = Folder + "/" + root.name + ".asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing)
        {
            Object.DestroyImmediate(mesh);
            mesh = existing;
        }
        else
            AssetDatabase.CreateAsset(mesh, path);
        GameObject surface = new GameObject("Surface");
        surface.transform.SetParent(root.transform, false);
        surface.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = surface.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Prefabs/Road/RoadSurface.mat");
        renderer.sortingOrder = -10;
    }

    // 구성 검증을 통과한 도로만 Prefab으로 저장한다
    private static void SaveRoad(GameObject root, string path)
    {
        if (!root.GetComponent<RoadChunk>().Validate(out string error))
            throw new InvalidOperationException(root.name + ": " + error);
        PrefabUtility.SaveAsPrefabAsset(root, path);
    }

    // 예제 Prefab 참조와 재현 가능한 초기 Seed를 설정한다
    internal static void Configure(CityMap map)
    {
        SerializedObject data = new SerializedObject(map);
        string[] fields = { "straight", "corner", "tJunction", "intersection" };
        string[] names = { "Straight", "Corner", "TJunction", "Intersection" };
        for (int i = 0; i < fields.Length; i++)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + names[i] + ".prefab");
            data.FindProperty(fields[i]).objectReferenceValue = prefab ? prefab.GetComponent<RoadChunk>() : null;
        }
        data.FindProperty("randomSeedOnStart").boolValue = false;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    // Play에서 한 번 생성되는 별도 도시 데모 씬을 만든다
    [MenuItem("CodeBlue Rush/City/Create Demo Scene")]
    public static void CreateDemo()
    {
        CreateDemoMap();
    }

    // 저장 취소 시 기존 씬을 유지하고 생성된 도시 컴포넌트를 반환한다
    internal static CityMap CreateDemoMap()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return null;
        CreatePrefabs();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        CityMap map = new GameObject("CityMap").AddComponent<CityMap>();
        Configure(map);
        Camera camera = new GameObject("City Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.orthographic = true;
        camera.orthographicSize = 170f;
        camera.transform.position = new Vector3(80f, 80f, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.12f, 0.1f);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = map.gameObject;
        return map;
    }
}
