using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>지형 이미지의 WebGL Import와 재사용 Mesh 및 Prefab과 Gameplay 연결을 작성한다</summary>
public static class MapAssetStage4
{
    private const string images = "Assets/Images/Maps/";
    private const string library = "Assets/Prefabs/MapAssets/";
    private const string resources = "Assets/MapAssets/Terrain/";
    private const float shoreWidth = 2.5f;
    private static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();

    // 승인된 이미지를 보존하고 지형 Prefab과 Gameplay 연결만 저장한다
    [MenuItem("CodeBlue Rush/Map Assets/Apply Stage 4")]
    public static void Apply()
    {
        prefabs.Clear();
        Folder("Assets/MapAssets");
        Folder(resources.TrimEnd('/'));
        foreach (string folder in new[] { "Ground", "Water", "Coast", "Nature" }) Folder(library + folder);
        Import();
        CreateGround();
        CreateWater();
        CreateCoast();
        CreateRocks();
        ConnectScene();
        AssetDatabase.SaveAssets();
        Debug.Log("STAGE4_APPLIED: 지형 재사용 Prefab 및 Gameplay CityTerrain 연결 완료");
    }

    // 새 이미지에만 PPU와 최소 해상도 및 WebGL 압축을 적용한다
    private static void Import()
    {
        foreach (string name in new[] { "Ground/Ground_Dirt_Default_01", "Ground/Ground_Grass_Park_01", "Water/Water_Sea_01", "Coast/Coast_Straight_01", "Nature/Rock_Medium_01", "Nature/Rock_Coastal_01" })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(images + name + ".png");
            if (!importer) throw new InvalidOperationException("지형 이미지 누락: " + name);
            bool rock = name.StartsWith("Nature", StringComparison.Ordinal);
            bool coast = name.StartsWith("Coast", StringComparison.Ordinal);
            importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = coast ? 25.6f : name.Contains("Coastal") ? sourceWidth / 4.5f : rock ? sourceWidth / 2f : 12.8f;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = 0;
            settings.spritePivot = Vector2.one * 0.5f;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.alphaIsTransparency = rock || coast;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(sourceWidth, sourceHeight));
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.crunchedCompression = false;
            foreach (string platform in new[] { "Standalone", "WebGL" })
            {
                var target = importer.GetPlatformTextureSettings(platform);
                target.name = platform;
                target.overridden = true;
                target.maxTextureSize = importer.maxTextureSize;
                target.format = rock || coast ? TextureImporterFormat.DXT5 : TextureImporterFormat.DXT1;
                target.textureCompression = TextureImporterCompression.CompressedHQ;
                target.compressionQuality = 100;
                importer.SetPlatformTextureSettings(target);
            }
            importer.SaveAndReimport();
        }
    }

    // 승인 잔디의 색상 Variant와 공원 및 흙길을 별도 PNG 없이 조립한다
    private static void CreateGround()
    {
        Mesh tile = Polygon("Tile_20", new[] { new Vector2(-10,-10), new Vector2(10,-10), new Vector2(10,10), new Vector2(-10,10) }, Vector2.zero);
        Mesh patch = Patch();
        Material grass = Material("Grass_Default", "Ground/Ground_Grass_Default_01", Color.white);
        Material bright = Material("Grass_Bright", "Ground/Ground_Grass_Default_01", new Color(1.04f, 1.025f, 1f));
        Material dark = Material("Grass_Dark", "Ground/Ground_Grass_Default_01", new Color(0.88f, 0.94f, 0.92f));
        Material park = Material("Grass_Park", "Ground/Ground_Grass_Park_01", Color.white);
        Material dirt = Material("Dirt", "Ground/Ground_Dirt_Default_01", Color.white);
        SaveSurface("Ground", "Ground_Grass_Default_01", tile, grass, -30);
        SaveSurface("Ground", "Ground_Grass_Bright_01", patch, bright, -28);
        SaveSurface("Ground", "Ground_Grass_Dark_01", patch, dark, -28);
        SaveSurface("Ground", "Ground_Grass_Park_01", patch, park, -28);
        SaveSurface("Ground", "Ground_Dirt_Default_01", tile, dirt, -28);
        var points = new List<Vector2>();
        for (int i = 0; i <= 32; i++)
        {
            float t = i / 32f;
            points.Add(new Vector2(Mathf.Sin(t * Mathf.PI * 2) * 1.2f, Mathf.Lerp(-6.5f, 6.5f, t)));
        }
        SaveSurface("Ground", "Ground_Dirt_Path_01", Ribbon("Dirt_Path", points, 1.6f, false), dirt, -26);
    }

    // 같은 수면을 색상과 연속 깊이 전이로 재사용한다
    private static void CreateWater()
    {
        Mesh tile = AssetDatabase.LoadAssetAtPath<Mesh>(resources + "Tile_20.asset");
        Material sea = Material("Water_Sea", "Water/Water_Sea_01", new Color(1f, 0.9f, 0.96f));
        sea.SetFloat("_Water", 1f);
        Material shallow = Material("Water_Shallow", "Water/Water_Sea_01", Color.white);
        Material pond = Material("Water_Pond", "Water/Water_Sea_01", new Color(0.8f, 0.96f, 0.86f));
        Material harbor = Material("Water_Harbor", "Water/Water_Sea_01", new Color(0.88f, 0.82f, 0.92f));
        SaveSurface("Water", "Water_Sea_01", tile, sea, -25);
        SaveSurface("Water", "Water_Shallow_01", tile, shallow, -25);
        SaveSurface("Water", "Water_Harbor_01", tile, harbor, -25);
        var circle = new List<Vector2>();
        for (int i = 0; i < 64; i++)
        {
            float angle = i * Mathf.PI * 2 / 64f;
            circle.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 5f);
        }
        SaveSurface("Water", "Water_Pond_01", Polygon("Pond_5", circle, Vector2.zero), pond, -25);
    }

    // 모든 해안의 끝점과 폭을 같은 수학적 규격으로 작성한다
    private static void CreateCoast()
    {
        Material coast = Material("Coast", "Coast/Coast_Straight_01", Color.white);
        coast.SetFloat("_WorldUV", 0f);
        coast.SetFloat("_Join", 0.02f);
        Material water = AssetDatabase.LoadAssetAtPath<Material>(resources + "Water_Sea.mat");
        var straight = new List<Vector2> { new Vector2(0,-10), new Vector2(0,10) };
        var curve = new List<Vector2>();
        for (int i = 0; i <= 32; i++)
        {
            float t = i / 32f;
            curve.Add(new Vector2(2f * Mathf.Sin(t * Mathf.PI * 2) * Mathf.Sin(t * Mathf.PI), Mathf.Lerp(-10f, 10f, t)));
        }
        SaveCoast("Coast_Straight_01", Ribbon("Coast_Straight", straight, shoreWidth, false), WaterStrip("Water_Straight", straight), coast, water);
        SaveCoast("Coast_Curve_01", Ribbon("Coast_Curve", curve, shoreWidth, false), WaterStrip("Water_Curve", curve), coast, water);
        List<Vector2> arc = Arc(new Vector2(-10,-10), 10f, 0f);
        SaveCoast("Coast_InnerCorner_01", Ribbon("Coast_Inner", arc, shoreWidth, true), Polygon("Water_Inner", arc, new Vector2(-10,-10)), coast, water);
        var outside = new List<Vector2> { new Vector2(10,-10) };
        outside.AddRange(arc);
        outside.Add(new Vector2(-10,10));
        SaveCoast("Coast_OuterCorner_01", Ribbon("Coast_Outer", arc, shoreWidth, false), Polygon("Water_Outer", outside, new Vector2(10,10)), coast, water);
        string path = library + "Water/Water_Pond_01.prefab";
        GameObject pond = PrefabUtility.LoadPrefabContents(path);
        try
        {
            for (int i = 0; i < 4; i++)
                Surface(pond.transform, "Shore" + i, Ribbon("Pond_Shore_" + i, Arc(Vector2.zero, 5f, i * 90f), shoreWidth, true), coast, -23);
            prefabs["Water_Pond_01"] = PrefabUtility.SaveAsPrefabAsset(pond, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(pond); }
    }

    // 소형은 중형 Sprite를 축소하고 해안용 큰 바위만 별도 실루엣을 사용한다
    private static void CreateRocks()
    {
        foreach (string name in new[] { "Rock_Small_01", "Rock_Medium_01", "Rock_Coastal_01" })
        {
            GameObject root = new GameObject(name);
            try
            {
                SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
                renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(images + "Nature/" + (name.Contains("Small") ? "Rock_Medium_01" : name) + ".png");
                renderer.sortingOrder = -2;
                if (name.Contains("Small")) root.transform.localScale = Vector3.one * 0.5f;
                prefabs[name] = PrefabUtility.SaveAsPrefabAsset(root, library + "Nature/" + name + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }

    // 기존 Scene의 생성기를 그대로 참조하는 표시 컴포넌트 하나만 추가한다
    private static void ConnectScene()
    {
        const string path = "Assets/Scenes/Gameplay.unity";
        Scene scene = SceneManager.GetSceneByPath(path);
        bool added = !scene.isLoaded;
        if (added) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        CityMap city = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (!city) city = root.GetComponentInChildren<CityMap>(true);
            if (root.name == "MapGround" && root.TryGetComponent(out SpriteRenderer renderer)) renderer.sortingOrder = -40;
        }
        if (!city) throw new InvalidOperationException("Gameplay CityMap 누락");
        if (!city.TryGetComponent(out CityTerrain terrain)) terrain = city.gameObject.AddComponent<CityTerrain>();
        var data = new SerializedObject(terrain);
        data.FindProperty("map").objectReferenceValue = city;
        foreach (var pair in new Dictionary<string, string> {
            { "ground", "Ground_Grass_Default_01" }, { "grassBright", "Ground_Grass_Bright_01" }, { "grassDark", "Ground_Grass_Dark_01" },
            { "grassPark", "Ground_Grass_Park_01" }, { "dirtPath", "Ground_Dirt_Path_01" }, { "sea", "Water_Sea_01" },
            { "shallow", "Water_Shallow_01" }, { "pond", "Water_Pond_01" }, { "harbor", "Water_Harbor_01" },
            { "coastStraight", "Coast_Straight_01" }, { "coastCurve", "Coast_Curve_01" }, { "rockSmall", "Rock_Small_01" },
            { "rockMedium", "Rock_Medium_01" }, { "rockCoastal", "Rock_Coastal_01" } })
            data.FindProperty(pair.Key).objectReferenceValue = prefabs[pair.Value];
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.SaveScene(scene);
        if (added) EditorSceneManager.CloseScene(scene, true);
    }

    // 사분원 끝점을 중간 변의 같은 위치에 작성한다
    private static List<Vector2> Arc(Vector2 center, float radius, float angle)
    {
        var points = new List<Vector2>();
        for (int i = 0; i <= 32; i++)
        {
            float radians = (angle + i * 90f / 32f) * Mathf.Deg2Rad;
            points.Add(center + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius);
        }
        return points;
    }

    // 같은 해안 표면의 양쪽을 법선 방향으로 펼쳐 모든 연결 폭을 유지한다
    private static Mesh Ribbon(string name, IList<Vector2> points, float width, bool reverse)
    {
        var vertices = new Vector3[points.Count * 2];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[(points.Count - 1) * 6];
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 tangent = (points[Mathf.Min(i + 1, points.Count - 1)] - points[Mathf.Max(i - 1, 0)]).normalized;
            Vector2 normal = new Vector2(tangent.y, -tangent.x) * (reverse ? -1f : 1f);
            if (name.StartsWith("Pond_Shore", StringComparison.Ordinal)) normal = -points[i].normalized;
            if (name.StartsWith("Coast_", StringComparison.Ordinal) && (i == 0 || i == points.Count - 1))
                normal = name.Contains("Inner") || name.Contains("Outer") ? i == 0 ? Vector2.right : Vector2.up : Vector2.right;
            if (reverse && name.StartsWith("Coast_", StringComparison.Ordinal) && (i == 0 || i == points.Count - 1)) normal = -normal;
            vertices[i * 2] = points[i] - normal * width * 0.5f;
            vertices[i * 2 + 1] = points[i] + normal * width * 0.5f;
            uv[i * 2] = new Vector2(0, i / (float)(points.Count - 1));
            uv[i * 2 + 1] = new Vector2(1, i / (float)(points.Count - 1));
            if (i == points.Count - 1) continue;
            int index = i * 6;
            triangles[index] = i * 2; triangles[index + 1] = i * 2 + 1; triangles[index + 2] = i * 2 + 2;
            triangles[index + 3] = i * 2 + 1; triangles[index + 4] = i * 2 + 3; triangles[index + 5] = i * 2 + 2;
        }
        return Mesh(name, vertices, uv, triangles, null);
    }

    // 곡선의 물 쪽을 같은 변까지 채워 인접 수면과 틈 없이 연결한다
    private static Mesh WaterStrip(string name, IList<Vector2> points)
    {
        var vertices = new Vector3[points.Count * 2];
        var triangles = new int[(points.Count - 1) * 6];
        for (int i = 0; i < points.Count; i++)
        {
            vertices[i * 2] = points[i]; vertices[i * 2 + 1] = new Vector2(10, points[i].y);
            if (i == points.Count - 1) continue;
            int index = i * 6;
            triangles[index] = i * 2; triangles[index + 1] = i * 2 + 1; triangles[index + 2] = i * 2 + 2;
            triangles[index + 3] = i * 2 + 1; triangles[index + 4] = i * 2 + 3; triangles[index + 5] = i * 2 + 2;
        }
        return Mesh(name, vertices, new Vector2[vertices.Length], triangles, null);
    }

    // 중심에서 보이는 경계 다각형을 삼각형 부채꼴로 채운다
    private static Mesh Polygon(string name, IList<Vector2> points, Vector2 center)
    {
        var vertices = new Vector3[points.Count + 1];
        var triangles = new int[points.Count * 3];
        vertices[0] = center;
        for (int i = 0; i < points.Count; i++)
        {
            vertices[i + 1] = points[i];
            triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 1; triangles[i * 3 + 2] = (i + 1) % points.Count + 1;
        }
        return Mesh(name, vertices, new Vector2[vertices.Length], triangles, null);
    }

    // 공원 가장자리의 Alpha를 완만히 줄여 기존 잔디에 자연스럽게 섞는다
    private static Mesh Patch()
    {
        var vertices = new Vector3[65];
        var colors = new Color[vertices.Length];
        var triangles = new int[32 * 9];
        colors[0] = Color.white;
        for (int i = 0; i < 32; i++)
        {
            float radians = i * Mathf.PI * 2 / 32f;
            Vector2 axis = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            Vector2 point = axis * (7f / Mathf.Max(Mathf.Abs(axis.x), Mathf.Abs(axis.y)));
            vertices[1 + i] = point * 0.88f; vertices[33 + i] = point;
            colors[1 + i] = Color.white; colors[33 + i] = new Color(1,1,1,0);
            int n = (i + 1) % 32;
            int index = i * 9;
            triangles[index] = 0; triangles[index + 1] = 1 + i; triangles[index + 2] = 1 + n;
            triangles[index + 3] = 1 + i; triangles[index + 4] = 33 + i; triangles[index + 5] = 1 + n;
            triangles[index + 6] = 33 + i; triangles[index + 7] = 33 + n; triangles[index + 8] = 1 + n;
        }
        return Mesh("Grass_Patch_14", vertices, new Vector2[vertices.Length], triangles, colors);
    }

    // 기존 Mesh GUID를 유지하며 생성 기하 자료를 저장한다
    private static Mesh Mesh(string name, Vector3[] vertices, Vector2[] uv, int[] triangles, Color[] colors)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(resources + name + ".asset");
        bool added = !mesh;
        if (added) mesh = new Mesh { name = name }; else mesh.Clear();
        if (colors == null) { colors = new Color[vertices.Length]; for (int i = 0; i < colors.Length; i++) colors[i] = Color.white; }
        mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles; mesh.colors = colors;
        mesh.RecalculateBounds();
        if (added) AssetDatabase.CreateAsset(mesh, resources + name + ".asset"); else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    // 표면 이미지를 공유하는 가벼운 URP 2D 재질을 저장한다
    private static Material Material(string name, string image, Color color)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(resources + name + ".mat");
        bool added = !material;
        if (added) material = new Material(Shader.Find("CodeBlueRush/MapTerrain")) { name = name };
        material.shader = Shader.Find("CodeBlueRush/MapTerrain");
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(images + image + ".png");
        material.SetColor("_Color", color);
        if (added) AssetDatabase.CreateAsset(material, resources + name + ".mat"); else EditorUtility.SetDirty(material);
        return material;
    }

    // 수면과 해안 표시를 분리한 연결용 Prefab을 저장한다
    private static void SaveCoast(string name, Mesh shore, Mesh water, Material coast, Material surface)
    {
        GameObject root = new GameObject(name);
        try
        {
            Surface(root.transform, "Water", water, surface, -25);
            Surface(root.transform, "Shore", shore, coast, -23);
            prefabs[name] = PrefabUtility.SaveAsPrefabAsset(root, library + "Coast/" + name + ".prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // 바닥을 실제 미터 규격의 독립 Prefab으로 저장한다
    private static void SaveSurface(string category, string name, Mesh mesh, Material material, int order)
    {
        GameObject root = new GameObject(name);
        try
        {
            Surface(null, root.name, mesh, material, order, root);
            prefabs[name] = PrefabUtility.SaveAsPrefabAsset(root, library + category + "/" + name + ".prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // Collider 없는 표시 전용 Renderer를 구성한다
    private static void Surface(Transform parent, string name, Mesh mesh, Material material, int order, GameObject existing = null)
    {
        GameObject node = existing ? existing : new GameObject(name);
        if (parent) node.transform.SetParent(parent, false);
        node.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = node.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingOrder = order;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // 새 폴더에만 Unity Meta를 생성한다
    private static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        Folder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
