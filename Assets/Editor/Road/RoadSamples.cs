using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>고정된 직선 및 코너 도로 예제와 수동 검증용 씬을 생성한다</summary>
public static class RoadSamples
{
    private const string Folder = "Assets/Prefabs/Road";

    // 없는 예제만 생성하여 기존 Prefab 편집 내용을 보존한다
    [MenuItem("CodeBlue Rush/Road/Create Missing Samples")]
    public static void CreateSamples()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            AssetDatabase.CreateFolder("Assets/Prefabs", "Road");
        }
        Material material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/RoadSurface.mat");
        if (!material)
        {
            material = new Material(Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(material, Folder + "/RoadSurface.mat");
        }
        CreateRoad(false, 1, material);
        CreateRoad(true, 1, material);
        CreateRoad(false, 2, material);
        CreateRoad(true, 2, material);
        AssetDatabase.SaveAssets();
    }

    // 방향당 차선 수에 맞는 독립 도로 Prefab을 생성한다
    private static void CreateRoad(bool corner, int count, Material material)
    {
        string name = (corner ? "Corner" : "Straight") + count * 2 + "Lane";
        string path = Folder + "/" + name + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path))
            return;

        GameObject root = new GameObject(name);
        try
        {
            RoadChunk chunk = root.AddComponent<RoadChunk>();
            TrafficLane[] lanes = new TrafficLane[count * 2];
            for (int i = 0; i < lanes.Length; i++)
            {
                bool reverse = i >= count;
                float offset = (i % count * 2f + 1f) * (reverse ? -1f : 1f);
                GameObject child = new GameObject("Lane" + i);
                child.transform.SetParent(root.transform, false);
                lanes[i] = child.AddComponent<TrafficLane>();
                Vector2[] points = GetPoints(corner, offset);
                if (reverse)
                    System.Array.Reverse(points);
                SerializedObject data = new SerializedObject(lanes[i]);
                SerializedProperty array = data.FindProperty("points");
                array.arraySize = points.Length;
                for (int p = 0; p < points.Length; p++)
                    array.GetArrayElementAtIndex(p).vector2Value = points[p];
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            for (int i = 0; i < lanes.Length; i++)
            {
                SerializedObject data = new SerializedObject(lanes[i]);
                data.FindProperty("leftLane").objectReferenceValue = i % count > 0 ? lanes[i - 1] : null;
                data.FindProperty("rightLane").objectReferenceValue = i % count < count - 1 ? lanes[i + 1] : null;
                data.ApplyModifiedPropertiesWithoutUndo();
            }

            RoadConnection start = CreatePort(root.transform, "Start", Vector3.zero, 180f);
            RoadConnection end = CreatePort(root.transform, "End", corner ? new Vector3(10f, 10f, 0f) : new Vector3(0f, 20f, 0f), corner ? -90f : 0f);
            TrafficLane[] forward = new TrafficLane[count];
            TrafficLane[] backward = new TrafficLane[count];
            System.Array.Copy(lanes, 0, forward, 0, count);
            System.Array.Copy(lanes, count, backward, 0, count);
            SetArray(start, "incoming", forward);
            SetArray(start, "outgoing", backward);
            SetArray(end, "incoming", backward);
            SetArray(end, "outgoing", forward);
            SetArray(chunk, "lanes", lanes);
            SetArray(chunk, "connections", new[] { start, end });
            CreateSurface(root, corner, count, material);
            if (!chunk.Validate(out string error))
                throw new System.InvalidOperationException(name + ": " + error);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // 직선 또는 90도 원호의 차선 중심점을 계산한다
    private static Vector2[] GetPoints(bool corner, float offset)
    {
        if (!corner)
            return new[] { new Vector2(offset, 0f), new Vector2(offset, 20f) };
        Vector2[] points = new Vector2[33];
        for (int i = 0; i < points.Length; i++)
        {
            float angle = i * Mathf.PI * 0.5f / (points.Length - 1);
            points[i] = new Vector2(10f - (10f - offset) * Mathf.Cos(angle), (10f - offset) * Mathf.Sin(angle));
        }
        return points;
    }

    // 바깥 방향을 가진 도로 연결 지점을 생성한다
    internal static RoadConnection CreatePort(Transform parent, string name, Vector3 position, float angle)
    {
        GameObject port = new GameObject(name);
        port.transform.SetParent(parent, false);
        port.transform.localPosition = position;
        port.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        return port.AddComponent<RoadConnection>();
    }

    // Inspector 배열에 내부 객체 참조를 저장한다
    internal static void SetArray(Object owner, string field, Object[] values)
    {
        SerializedObject data = new SerializedObject(owner);
        SerializedProperty array = data.FindProperty(field);
        array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    // 경로와 같은 치수로 도로 표면 및 차선 표시 메시를 생성한다
    private static void CreateSurface(GameObject root, bool corner, int count, Material material)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Color> colors = new List<Color>();
        AddStrip(GetPoints(corner, -count * 2f), GetPoints(corner, count * 2f), new Color(0.16f, 0.18f, 0.2f), 0.02f, vertices, triangles, colors);
        AddStrip(GetPoints(corner, -0.05f), GetPoints(corner, 0.05f), Color.yellow, 0f, vertices, triangles, colors);
        for (int i = -count; i <= count; i++)
        {
            if (i == 0)
                continue;
            AddStrip(GetPoints(corner, i * 2f - 0.04f), GetPoints(corner, i * 2f + 0.04f), Color.white, 0f, vertices, triangles, colors);
        }
        string path = Folder + "/" + root.name + ".asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!mesh)
        {
            mesh = new Mesh { name = root.name + "Surface" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetColors(colors);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            AssetDatabase.CreateAsset(mesh, path);
        }
        GameObject surface = new GameObject("Surface");
        surface.transform.SetParent(root.transform, false);
        surface.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = surface.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingOrder = -10;
    }

    // 두 경계선 사이의 띠를 정점 색상 메시로 추가한다
    internal static void AddStrip(Vector2[] left, Vector2[] right, Color color, float z, List<Vector3> vertices, List<int> triangles, List<Color> colors)
    {
        int start = vertices.Count;
        for (int i = 0; i < left.Length; i++)
        {
            vertices.Add(new Vector3(left[i].x, left[i].y, z));
            vertices.Add(new Vector3(right[i].x, right[i].y, z));
            colors.Add(color);
            colors.Add(color);
            if (i == 0)
                continue;
            int p = start + i * 2;
            triangles.Add(p - 2);
            triangles.Add(p);
            triangles.Add(p - 1);
            triangles.Add(p - 1);
            triangles.Add(p);
            triangles.Add(p + 1);
        }
    }

    // 기존 씬의 저장 여부를 확인하고 고정된 세 도로의 테스트 씬을 생성한다
    [MenuItem("CodeBlue Rush/Road/Create Demo Scene")]
    public static void CreateDemo()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        CreateSamples();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        RoadChunk a = Spawn("Straight4Lane", Vector3.zero, 0f);
        RoadChunk b = Spawn("Corner4Lane", new Vector3(0f, 20f, 0f), 0f);
        RoadChunk c = Spawn("Straight4Lane", new Vector3(10f, 30f, 0f), -90f);
        a.name = "EntryStraight";
        b.name = "Corner";
        c.name = "ExitStraight";
        SetTarget(a.GetConnection(1), b.GetConnection(0));
        SetTarget(b.GetConnection(1), c.GetConnection(0));
        Camera camera = new GameObject("Road Demo Camera").AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 34f;
        camera.transform.position = new Vector3(13f, 17f, -10f);
        camera.tag = "MainCamera";
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.12f, 0.1f);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = a.gameObject;
    }

    // 도로 예제를 지정 위치와 회전으로 배치한다
    private static RoadChunk Spawn(string name, Vector3 position, float angle)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + name + ".prefab"));
        instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));
        return instance.GetComponent<RoadChunk>();
    }

    // 씬 연결을 양쪽에 직렬화하여 재활성화 시에도 복구 가능하게 한다
    private static void SetTarget(RoadConnection a, RoadConnection b)
    {
        SerializedObject first = new SerializedObject(a);
        first.FindProperty("target").objectReferenceValue = b;
        first.ApplyModifiedPropertiesWithoutUndo();
        SerializedObject second = new SerializedObject(b);
        second.FindProperty("target").objectReferenceValue = a;
        second.ApplyModifiedPropertiesWithoutUndo();
    }
}
