using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>기존 도시 Prefab의 시각 자료와 보행 위치만 3단계 규격으로 설정한다</summary>
public static class MapAssetStage3
{
    private const string images = "Assets/Images/Maps/";
    private const string library = "Assets/Prefabs/MapAssets/";
    private static readonly Color yellow = new Color(252f / 253f, 217f / 253f, 10f / 253f);
    private const float roadHalf = 2.04f;
    private const float walkWidth = 0.6f;
    private const float walkCenter = roadHalf + walkWidth / 2f;
    private static readonly Dictionary<string, float> stems = new Dictionary<string, float>();

    // 기존 도로와 생성기를 보존하며 재사용 Prefab과 도시 표시를 저장한다
    [MenuItem("CodeBlue Rush/Map Assets/Apply Stage 3")]
    public static void Apply()
    {
        stems.Clear();
        Import();
        EnsureFolder("Assets/Prefabs", "MapAssets");
        EnsureFolder("Assets/Prefabs/MapAssets", "RoadMarking");
        EnsureFolder("Assets/Prefabs/MapAssets", "Sidewalk");
        CreateLibrary();
        foreach (string name in new[] { "Straight", "PassingStraight", "Corner", "TJunction", "Intersection" })
            ApplyRoad(name);
        ApplyGround();
        AssetDatabase.SaveAssets();
        Debug.Log("STAGE3_APPLIED: 기존 City Prefab 5종 및 재사용 Overlay와 보도 저장 완료");
    }

    // 새 이미지의 최소 해상도와 WebGL 압축 및 중심 Pivot을 설정한다
    private static void Import()
    {
        foreach (string category in new[] { "RoadMarking", "Sidewalk" })
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { images + category }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("Sidewalk_Straight_01.png", StringComparison.Ordinal)) continue;
                TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = category == "RoadMarking" ? 40f : path.Contains("Crosswalk") ? 40f : 25.6f;
                TextureImporterSettings settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spriteAlignment = 0;
                settings.spritePivot = Vector2.one * 0.5f;
                settings.spriteGenerateFallbackPhysicsShape = false;
                importer.SetTextureSettings(settings);
                importer.mipmapEnabled = false;
                importer.isReadable = false;
                importer.alphaIsTransparency = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(width, height));
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.crunchedCompression = false;
                foreach (string platform in new[] { "Standalone", "WebGL" })
                {
                    TextureImporterPlatformSettings target = importer.GetPlatformTextureSettings(platform);
                    target.name = platform;
                    target.overridden = true;
                    target.maxTextureSize = importer.maxTextureSize;
                    target.format = TextureImporterFormat.DXT5;
                    target.textureCompression = TextureImporterCompression.CompressedHQ;
                    target.compressionQuality = 100;
                    importer.SetPlatformTextureSettings(target);
                }
                importer.SaveAndReimport();
            }
        }
    }

    // 연결되는 바닥과 표시를 기존 City Prefab에 추가한다
    private static void ApplyRoad(string name)
    {
        string path = "Assets/Prefabs/City/" + name + ".prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            RoadChunk road = root.GetComponent<RoadChunk>();
            Transform old = root.transform.Find("MapVisuals");
            if (old) Object.DestroyImmediate(old.gameObject);
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>()) renderer.enabled = false;
            foreach (SidewalkPath walk in root.GetComponentsInChildren<SidewalkPath>())
                foreach (SpriteRenderer renderer in walk.GetComponentsInChildren<SpriteRenderer>()) renderer.enabled = false;
            foreach (VehicleStopZone zone in root.GetComponentsInChildren<VehicleStopZone>())
                foreach (SpriteRenderer renderer in zone.GetComponentsInChildren<SpriteRenderer>())
                    if (renderer.name != "TrafficLight") renderer.enabled = false;
            Transform visual = Node(root.transform, "MapVisuals");
            bool wide = name == "PassingStraight";
            string roadSprite = name == "Corner" ? "Road_Corner_SE" : name == "TJunction" ? "Road_TJunction_W" : name == "Intersection" ? "Road_Intersection_4Way" : wide ? "Road_Straight_4Lane" : "Road_Straight_2Lane";
            Add(visual, "RoadBase", Sprite("Road/" + roadSprite), Vector2.zero, name == "Straight" || wide ? new Vector2(wide ? 8.08f : 4.08f, 20f) : Vector2.one * 20f, 0f, -10);
            if (name == "Corner")
            {
                Add(visual, "Sidewalk", Sprite("Sidewalk/Sidewalk_Corner_01"), Vector2.zero, Vector2.one * 20f, -90f, -11);
                foreach (float offset in new[] { -1.96f, 0f, 1.96f })
                {
                    List<Vector2> points = new List<Vector2>();
                    for (int i = 0; i <= 32; i++)
                    {
                        float angle = (90f + 90f * i / 32f) * Mathf.Deg2Rad;
                        points.Add(new Vector2(10f, -10f) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (10f + offset));
                    }
                    Stroke(visual, "CurveLine", points, 0.06823f, offset == 0f ? yellow : Color.white);
                }
            }
            else if (name == "Straight" || wide)
            {
                float side = (wide ? 4.04f : roadHalf) + walkWidth / 2f;
                Add(visual, "SidewalkRight", Sprite("Sidewalk/Sidewalk_Straight_01"), new Vector2(side, 0f), new Vector2(walkWidth, 20f), 0f, -11);
                Add(visual, "SidewalkLeft", Sprite("Sidewalk/Sidewalk_Straight_01"), new Vector2(-side, 0f), new Vector2(walkWidth, 20f), 0f, -11).flipX = true;
                if (wide)
                {
                    Line(visual, new Vector2(0f, -10f), new Vector2(0f, 10f), 0.06823f, yellow);
                    foreach (float x in new[] { -3.96f, 3.96f }) Line(visual, new Vector2(x, -10f), new Vector2(x, 10f), 0.06823f, Color.white);
                    foreach (float x in new[] { -2f, 2f })
                        for (int y = -10; y < 10; y += 2) Line(visual, new Vector2(x, y), new Vector2(x, y + 1f), 0.06823f, Color.white);
                }
                foreach (float x in wide ? new[] { -3f, -1f, 1f, 3f } : new[] { -1f, 1f })
                    Glyph(visual, "Arrow_Straight", new Vector2(x, x > 0 ? -5f : 5f), x > 0 ? 0f : 180f);
            }
            else
            {
                Add(visual, "Sidewalk", Sprite("Sidewalk/" + (name == "TJunction" ? "Sidewalk_TJunction_01" : "Sidewalk_Intersection_01")), Vector2.zero, Vector2.one * 20f, 0f, -11);
                for (int i = 0; i < road.ConnectionCount; i++)
                {
                    RoadConnection port = road.GetConnection(i);
                    Vector2 axis = root.transform.InverseTransformDirection(port.Outward);
                    Vector2 right = new Vector2(axis.y, -axis.x);
                    Line(visual, axis * 4.7f, axis * 10f, 0.06823f, yellow);
                    foreach (float sign in new[] { -1f, 1f }) Line(visual, axis * 4.7f + right * sign * 1.96f, axis * 10f + right * sign * 1.96f, 0.06823f, Color.white);
                    Crosswalk(visual, axis, 5f, 4.08f);
                    Line(visual, axis * 6f - right * 1.9f, axis * 6f - right * 0.1f, 0.15f, Color.white);
                    Transform marking = Node(visual, "ApproachArrow");
                    marking.localPosition = axis * 8f - right;
                    marking.localRotation = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.up, -axis));
                    bool straight = HasPort(road, -axis);
                    bool left = HasPort(road, right);
                    bool turnRight = HasPort(road, -right);
                    if (straight && left && turnRight) Glyph(marking, "Arrow_All", Vector2.zero, 0f);
                    else if (straight && (left || turnRight)) Glyph(marking, "Arrow_Straight_Left", Vector2.zero, 0f, turnRight);
                    else if (left && turnRight)
                    {
                        Glyph(marking, "Arrow_Left", Vector2.zero, 0f);
                        Glyph(marking, "Arrow_Left", Vector2.zero, 0f, true);
                    }
                    else Glyph(marking, straight ? "Arrow_Straight" : "Arrow_Left", Vector2.zero, 0f, turnRight);
                    foreach (float sign in new[] { -1f, 1f })
                        Add(visual, "CrosswalkRamp", Sprite("Sidewalk/Sidewalk_Crosswalk_01"), axis * 5f + right * sign * walkCenter, new Vector2(walkWidth, 1.2f), Vector2.SignedAngle(Vector2.right, right), -7);
                    ConfigureZone(root.transform, road, axis, right);
                }
                if (name == "Intersection")
                {
                    GameObject hatch = AssetDatabase.LoadAssetAtPath<GameObject>(library + "RoadMarking/RoadMarking_NoStopping_01.prefab");
                    if (hatch) PrefabUtility.InstantiatePrefab(hatch, visual);
                }
            }
            ConfigureWalks(root, road, name, wide);
            AddDecorationVariants(root);
            if (!road.Validate(out string error)) throw new InvalidOperationException(name + ": " + error);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // 기존 보행 경로의 참조를 유지하고 도로 경계 밖으로 점을 정렬한다
    private static void ConfigureWalks(GameObject root, RoadChunk road, string name, bool wide)
    {
        Transform environment = root.transform.Find("Environment");
        List<Transform> obsolete = new List<Transform>();
        foreach (Transform child in environment) if (child.name.StartsWith("MapConnector", StringComparison.Ordinal)) obsolete.Add(child);
        foreach (Transform child in obsolete) Object.DestroyImmediate(child.gameObject);
        SidewalkPath[] paths = environment.GetComponentsInChildren<SidewalkPath>();
        if (name == "Corner")
        {
            for (int p = 0; p < 2; p++)
            {
                Vector2[] points = new Vector2[25];
                for (int i = 0; i < points.Length; i++)
                {
                    float a = (90f + 90f * i / 24f) * Mathf.Deg2Rad;
                    points[i] = new Vector2(10f, -10f) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (p == 0 ? 7.66f : 12.34f);
                }
                SetPoints(paths[p], points);
            }
            return;
        }
        float center = wide ? 4.34f : walkCenter;
        for (int p = 0; p < road.ConnectionCount; p++)
        {
            Vector2 axis = root.transform.InverseTransformDirection(road.GetConnection(p).Outward);
            Vector2 right = new Vector2(axis.y, -axis.x);
            for (int s = 0; s < 2; s++)
                SetPoints(paths[p * 2 + s], axis * 10f + right * (s == 0 ? -center : center), axis * 5f + right * (s == 0 ? -center : center));
        }
        int closed = road.ConnectionCount * 2;
        for (int d = 0; d < 4; d++)
        {
            Vector2 axis = CitySamples.Direction(d);
            if (HasPort(road, axis)) continue;
            Vector2 right = new Vector2(axis.y, -axis.x);
            SetPoints(paths[closed++], axis * center - right * 5f, axis * center + right * 5f);
        }
        if (road.ConnectionCount < 3) return;
        foreach (int sx in new[] { -1, 1 })
            foreach (int sy in new[] { -1, 1 })
            {
                if (!HasPort(road, Vector2.right * sx) || !HasPort(road, Vector2.up * sy)) continue;
                List<Vector2> points = new List<Vector2> { new Vector2(sx * center, sy * 5f) };
                for (int i = 0; i <= 16; i++)
                {
                    float a = (180f + 90f * i / 16f) * Mathf.Deg2Rad;
                    points.Add(new Vector2(sx * (4.04f + Mathf.Cos(a) * 1.7f), sy * (4.04f + Mathf.Sin(a) * 1.7f)));
                }
                points.Add(new Vector2(sx * 5f, sy * center));
                SidewalkPath path = Node(environment, "MapConnector_" + sx + "_" + sy).gameObject.AddComponent<SidewalkPath>();
                SerializedObject data = new SerializedObject(path);
                data.FindProperty("road").objectReferenceValue = road;
                data.ApplyModifiedPropertiesWithoutUndo();
                SetPoints(path, points.ToArray());
            }
    }

    // 횡단보도의 실제 중앙과 양쪽 보도에 기존 대기 참조를 맞춘다
    private static void ConfigureZone(Transform root, RoadChunk road, Vector2 axis, Vector2 right)
    {
        foreach (VehicleStopZone zone in road.GetComponentsInChildren<VehicleStopZone>())
        {
            if (Vector3.Dot(-zone.Lane.StartDirection, root.TransformDirection(axis)) < 0.99f) continue;
            zone.CrosswalkStart.localPosition = zone.transform.InverseTransformPoint(root.TransformPoint(axis * 5f - right * roadHalf));
            zone.CrosswalkEnd.localPosition = zone.transform.InverseTransformPoint(root.TransformPoint(axis * 5f + right * roadHalf));
            zone.PedestrianWaitPointA.localPosition = zone.transform.InverseTransformPoint(root.TransformPoint(axis * 5f - right * walkCenter));
            zone.PedestrianWaitPointB.localPosition = zone.transform.InverseTransformPoint(root.TransformPoint(axis * 5f + right * walkCenter));
            Transform light = zone.transform.Find("TrafficLight");
            if (light) light.localPosition = zone.transform.InverseTransformPoint(root.TransformPoint(axis * 6f - right * 2.8f));
        }
    }

    // 원래 장식 선택을 그대로 사용해 주차와 버스 표식을 랜덤 도시에 제공한다
    private static void AddDecorationVariants(GameObject root)
    {
        foreach (EnvironmentSlot slot in root.GetComponentsInChildren<EnvironmentSlot>())
        {
            if (slot.Uses != EnvironmentSlot.Usage.Decoration || Mathf.Abs(slot.transform.localPosition.x) < 7.9f || Mathf.Abs(slot.transform.localPosition.y) > 0.1f) continue;
            SerializedObject data = new SerializedObject(slot);
            SerializedProperty variants = data.FindProperty("variants");
            List<Object> values = new List<Object>();
            for (int i = 0; i < variants.arraySize; i++) values.Add(variants.GetArrayElementAtIndex(i).objectReferenceValue);
            foreach (string name in new[] { "ParkingBay", "BusBay" })
            {
                Object prefab = AssetDatabase.LoadAssetAtPath<GameObject>(library + "RoadMarking/RoadMarking_" + name + "_01.prefab");
                if (prefab && !values.Contains(prefab)) values.Add(prefab);
            }
            variants.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) variants.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    // PNG를 중복하지 않고 선 조합과 Flip 및 폭 재사용 Prefab을 작성한다
    private static void CreateLibrary()
    {
        foreach (string name in new[] { "CenterLine", "EdgeLine", "LaneLine_Dashed", "StopLine", "Crosswalk", "Arrow_Straight", "Arrow_Left", "Arrow_Right", "Arrow_Straight_Left", "Arrow_Straight_Right", "Arrow_All", "Arrow_UTurn", "NoStopping", "ParkingBay", "BusBay", "Parking" })
        {
            GameObject root = new GameObject("RoadMarking_" + name + "_01");
            Transform t = root.transform;
            if (name == "CenterLine" || name == "EdgeLine") Line(t, Vector2.down * 10f, Vector2.up * 10f, 0.06823f, name == "CenterLine" ? yellow : Color.white);
            else if (name == "LaneLine_Dashed") for (int y = -10; y < 10; y += 2) Line(t, Vector2.up * y, Vector2.up * (y + 1f), 0.06823f, Color.white);
            else if (name == "StopLine") Line(t, Vector2.left * 0.9f, Vector2.right * 0.9f, 0.15f, Color.white);
            else if (name == "Crosswalk") Crosswalk(t, Vector2.up, 0f, 4.08f);
            else if (name == "NoStopping")
            {
                Box(t, new Vector2(3.8f, 3.8f), yellow);
                for (int i = -4; i <= 4; i++)
                    foreach (float slope in new[] { -1f, 1f })
                    {
                        float offset = i * 0.8f;
                        float lo = Mathf.Max(-1.9f, -1.9f - offset);
                        float hi = Mathf.Min(1.9f, 1.9f - offset);
                        if (hi > lo) Line(t, new Vector2(lo, slope * (lo + offset)), new Vector2(hi, slope * (hi + offset)), 0.06823f, yellow);
                    }
            }
            else if (name == "ParkingBay" || name == "BusBay")
            {
                Add(t, "BaySurface", Sprite("Road/Road_Small_Straight_01"), Vector2.zero, new Vector2(2.2f, 4.4f), 0f, -10);
                Box(t, new Vector2(1.9f, 4.1f), name == "BusBay" ? yellow : Color.white);
                Glyph(t, name == "BusBay" ? "Bus" : "Parking", Vector2.zero, 0f);
            }
            else Glyph(t, name == "Arrow_Right" ? "Arrow_Left" : name == "Arrow_Straight_Right" ? "Arrow_Straight_Left" : name, Vector2.zero, 0f, name.EndsWith("Right", StringComparison.Ordinal));
            PrefabUtility.SaveAsPrefabAsset(root, library + "RoadMarking/" + root.name + ".prefab");
            Object.DestroyImmediate(root);
        }
        foreach (string name in new[] { "Straight", "Narrow", "Wide", "Corner", "Intersection", "TJunction", "Crosswalk", "Curb_Straight", "Curb_Corner" })
        {
            GameObject root = new GameObject("Sidewalk_" + name + "_01");
            string source = name == "Narrow" || name == "Wide" || name == "Curb_Straight" ? "Straight" : name == "Curb_Corner" ? "Corner" : name;
            Vector2 size = source == "Straight" ? new Vector2(name == "Narrow" ? 0.4f : name == "Wide" ? 1.2f : name == "Curb_Straight" ? 0.08f : 0.6f, 20f) : source == "Crosswalk" ? new Vector2(0.6f, 1.2f) : Vector2.one * 20f;
            Add(root.transform, "Visual", Sprite("Sidewalk/Sidewalk_" + source + "_01"), Vector2.zero, size, 0f, -11);
            PrefabUtility.SaveAsPrefabAsset(root, library + "Sidewalk/" + root.name + ".prefab");
            Object.DestroyImmediate(root);
        }
    }

    // 같은 페인트 선을 반복해 회전 가능한 횡단보도를 작성한다
    private static void Crosswalk(Transform parent, Vector2 axis, float distance, float width)
    {
        Vector2 right = new Vector2(axis.y, -axis.x);
        for (int i = -4; i <= 4; i++)
        {
            Vector2 center = axis * distance + right * (i * 0.45f * width / 4.08f);
            Line(parent, center - axis * 0.55f, center + axis * 0.55f, 0.24f * width / 4.08f, Color.white);
        }
    }

    // 필요한 선을 같은 텍스처로 조합해 주차 경계를 작성한다
    private static void Box(Transform parent, Vector2 size, Color color)
    {
        Vector2 h = size / 2f;
        Vector2[] points = { new Vector2(-h.x, -h.y), new Vector2(h.x, -h.y), new Vector2(h.x, h.y), new Vector2(-h.x, h.y), new Vector2(-h.x, -h.y) };
        Stroke(parent, "Outline", points, 0.06823f, color);
    }

    // 하나의 독립 화살표 Sprite를 회전 또는 Flip으로 배치한다
    private static void Glyph(Transform parent, string name, Vector2 point, float angle, bool flip = false)
    {
        Sprite sprite = Sprite("RoadMarking/RoadMarking_" + name + "_01");
        if (!sprite) return;
        if (name.StartsWith("Arrow", StringComparison.Ordinal))
        {
            if (!stems.TryGetValue(name, out float offset))
            {
                Texture2D raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                raw.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite)));
                Color32[] pixels = raw.GetPixels32();
                float weighted = 0f;
                float alpha = 0f;
                for (int y = 2; y < Mathf.Max(3, raw.height / 6); y++)
                    for (int x = 0; x < raw.width; x++)
                    {
                        weighted += (x + 0.5f) * pixels[y * raw.width + x].a;
                        alpha += pixels[y * raw.width + x].a;
                    }
                offset = alpha > 0f ? (weighted / alpha - raw.width / 2f) / 40f : 0f;
                Object.DestroyImmediate(raw);
                stems.Add(name, offset);
            }
            point -= (Vector2)(Quaternion.Euler(0f, 0f, angle) * Vector3.right) * offset * (flip ? -1f : 1f);
        }
        Add(parent, name, sprite, point, new Vector2(sprite.bounds.size.x, sprite.bounds.size.y), angle, -8).flipX = flip;
    }

    // 승인 잔디를 한 개의 Tiled Renderer로 Gameplay의 전체 셀 아래에 배치한다
    private static void ApplyGround()
    {
        const string path = "Assets/Scenes/Gameplay.unity";
        Scene scene = SceneManager.GetSceneByPath(path);
        bool added = !scene.isLoaded;
        if (added) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        CityMap city = null;
        GameObject floor = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "MapGround") floor = root;
            if (!city) city = root.GetComponentInChildren<CityMap>(true);
        }
        if (!city) throw new InvalidOperationException("Gameplay CityMap 누락");
        if (!floor)
        {
            floor = new GameObject("MapGround", typeof(SpriteRenderer));
            SceneManager.MoveGameObjectToScene(floor, scene);
        }
        SerializedObject data = new SerializedObject(city);
        int width = data.FindProperty("width").intValue;
        int height = data.FindProperty("height").intValue;
        float cell = data.FindProperty("cellSize").floatValue;
        SpriteRenderer renderer = floor.GetComponent<SpriteRenderer>();
        renderer.sprite = Sprite("Ground/Ground_Grass_Default_01");
        renderer.drawMode = SpriteDrawMode.Tiled;
        renderer.tileMode = SpriteTileMode.Continuous;
        renderer.sortingOrder = -30;
        float scale = cell / renderer.sprite.bounds.size.x;
        floor.transform.localScale = Vector3.one * scale;
        floor.transform.position = city.transform.position + new Vector3((width - 1) * cell / 2f, (height - 1) * cell / 2f, 0f);
        renderer.size = new Vector2(width * cell / scale, height * cell / scale);
        EditorSceneManager.SaveScene(scene);
        if (added) EditorSceneManager.CloseScene(scene, true);
    }

    // 곡선의 점 간격을 같은 선 Sprite로 이어 Overlay를 구성한다
    private static void Stroke(Transform parent, string name, IList<Vector2> points, float width, Color color)
    {
        Transform node = Node(parent, name);
        for (int i = 1; i < points.Count; i++) Line(node, points[i - 1], points[i], width, color);
    }

    // 페인트 직사각형을 실제 월드 길이와 폭으로 배치한다
    private static void Line(Transform parent, Vector2 a, Vector2 b, float width, Color color)
    {
        Sprite sprite = Sprite("RoadMarking/RoadMarking_Line_Solid_01");
        if (!sprite) return;
        Add(parent, "Line", sprite, (a + b) / 2f, new Vector2(width, Vector2.Distance(a, b) + 0.005f), Vector2.SignedAngle(Vector2.up, b - a), -8).color = color;
    }

    // Sprite 경계와 PPU를 기준으로 실제 미터 크기를 보존한다
    private static SpriteRenderer Add(Transform parent, string name, Sprite sprite, Vector2 point, Vector2 size, float angle, int order)
    {
        Transform node = Node(parent, name);
        SpriteRenderer renderer = node.gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = order;
        node.localPosition = point;
        node.localRotation = Quaternion.Euler(0f, 0f, angle);
        if (sprite) node.localScale = new Vector3(size.x / sprite.bounds.size.x, size.y / sprite.bounds.size.y, 1f);
        return renderer;
    }

    // 자식의 로컬 좌표계를 기본값으로 작성한다
    private static Transform Node(Transform parent, string name)
    {
        Transform node = new GameObject(name).transform;
        node.SetParent(parent, false);
        return node;
    }

    // AssetDatabase에서 기존 또는 새 Sprite를 읽는다
    private static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(images + name + ".png");

    // 원래 포트의 방향을 사용해 가능한 표시 방향을 판별한다
    private static bool HasPort(RoadChunk road, Vector2 axis)
    {
        Vector3 world = road.transform.TransformDirection(axis);
        for (int i = 0; i < road.ConnectionCount; i++) if (Vector3.Dot(road.GetConnection(i).Outward, world) > 0.99f) return true;
        return false;
    }

    // 기존 보도 소유권과 연결 참조를 변경하지 않고 점만 저장한다
    private static void SetPoints(SidewalkPath path, params Vector2[] points)
    {
        SerializedObject data = new SerializedObject(path);
        SerializedProperty array = data.FindProperty("points");
        array.arraySize = points.Length;
        for (int i = 0; i < points.Length; i++) array.GetArrayElementAtIndex(i).vector2Value = points[i];
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    // 없는 생성물 폴더만 추가한다
    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }
}
