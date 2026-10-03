using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>최소 식생 이미지를 재사용 Prefab으로 조립하고 편집용 라이브러리에 저장한다</summary>
public static class MapAssetStage5
{
    private const string images = "Assets/Images/Maps/Nature/";
    private const string library = "Assets/Prefabs/MapAssets/Nature/";
    private static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
    private static readonly Color pink = new Color(1f, 0.42f, 0.74f);
    private static readonly Color yellow = new Color(1f, 0.88f, 0.25f);

    // 저장되지 않은 지형 연결을 먼저 적용하고 식생 세트를 한 번에 구성한다
    [MenuItem("CodeBlue Rush/Map Assets/Apply Stage 5")]
    public static void Apply()
    {
        prefabs.Clear();
        MapAssetStage4.Apply();
        foreach (string folder in new[] { "Tree", "Bush", "Flower", "Planter" }) Folder(library + folder);
        Import();
        CreateTrees();
        CreateShrubs();
        CreateFlowers();
        CreateBeds();
        UpdateTree();
        AssetDatabase.SaveAssets();
        Debug.Log("STAGE5_APPLIED: 식생 13종 재사용 Prefab과 자산 작성 완료");
    }

    // 새 PNG 네 개만 최소 해상도와 투명 Sprite 및 WebGL 압축으로 설정한다
    private static void Import()
    {
        foreach (string name in new[] { "Bush/Bush_Round_01", "Bush/Hedge_Short_01", "Flower/FlowerPatch_01", "Planter/Planter_01" })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(images + name + ".png");
            if (!importer) throw new InvalidOperationException("식생 이미지 누락: " + name);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 64f;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.Tight;
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
            importer.maxTextureSize = 128;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            foreach (string platform in new[] { "Standalone", "WebGL" })
            {
                var target = importer.GetPlatformTextureSettings(platform);
                target.name = platform;
                target.overridden = true;
                target.maxTextureSize = 128;
                target.format = TextureImporterFormat.DXT5;
                target.textureCompression = TextureImporterCompression.CompressedHQ;
                target.compressionQuality = 100;
                target.crunchedCompression = false;
                importer.SetPlatformTextureSettings(target);
            }
            importer.SaveAndReimport();
        }
    }

    // 승인된 Medium PNG의 Import와 디자인을 보존하고 크기만 변형한다
    private static void CreateTrees()
    {
        foreach (var entry in new[] { ("Tree_Small_01", 1.5f), ("Tree_Medium_01", 2.4f), ("Tree_Large_01", 3.6f) })
        {
            GameObject root = Root(entry.Item1, VegetationItem.Kind.Tree);
            Visual(root, "Canopy", "Tree/Tree_Medium_01", entry.Item2, Color.white, -2, Vector2.zero);
            Save("Tree", root);
        }
    }

    // 낮은 수풀은 Scale로 긴 생울타리는 짧은 세그먼트의 반복으로 구성한다
    private static void CreateShrubs()
    {
        GameObject root = Root("Bush_Round_01", VegetationItem.Kind.Bush);
        Visual(root, "Foliage", "Bush/Bush_Round_01", 1.7f, Color.white, -2, Vector2.zero);
        Save("Bush", root);
        root = Root("Bush_Low_01", VegetationItem.Kind.Bush);
        SpriteRenderer low = Visual(root, "Foliage", "Bush/Bush_Round_01", 1.25f, Color.white, -2, Vector2.zero);
        low.transform.localScale = Vector3.Scale(low.transform.localScale, new Vector3(1f, 0.72f, 1f));
        Save("Bush", root);
        root = Root("Hedge_Short_01", VegetationItem.Kind.Hedge);
        Visual(root, "Hedge", "Bush/Hedge_Short_01", 2f, Color.white, -2, Vector2.zero);
        Save("Bush", root);
        root = Root("Hedge_Long_01", VegetationItem.Kind.Hedge);
        for (int i = -1; i <= 1; i++) Visual(root, "Hedge_" + (i + 1), "Bush/Hedge_Short_01", 2f, Color.white, -2, new Vector2(i * 1.8f, 0f));
        Save("Bush", root);
    }

    // 중립 꽃잎의 Tint만 변경하고 잎은 승인 초록 색상을 유지한다
    private static void CreateFlowers()
    {
        foreach (var entry in new[] { ("FlowerPatch_01", pink), ("FlowerPatch_02", yellow) })
        {
            GameObject root = Root(entry.Item1, VegetationItem.Kind.Flower);
            Flowers(root, 1.45f, entry.Item2);
            Save("Flower", root);
        }
        GameObject bush = Root("FlowerBush_01", VegetationItem.Kind.Flower);
        Visual(bush, "Foliage", "Bush/Bush_Round_01", 1.7f, Color.white, -2, Vector2.zero);
        Visual(bush, "Blossoms", "Flower/FlowerPatch_01", 1.2f, pink, -1, new Vector2(-0.04f, 0.12f));
        Save("Flower", bush);
    }

    // 동일한 석재 화단 베이스를 크기와 식재 조합으로 재사용한다
    private static void CreateBeds()
    {
        foreach (var entry in new[] { ("FlowerBed_Small_01", 1.6f, pink), ("FlowerBed_Medium_01", 2.4f, yellow), ("Planter_01", 1.25f, pink) })
        {
            GameObject root = Root(entry.Item1, entry.Item1.StartsWith("Planter", StringComparison.Ordinal) ? VegetationItem.Kind.Planter : VegetationItem.Kind.FlowerBed);
            Visual(root, "Stone", "Planter/Planter_01", entry.Item2, Color.white, -3, Vector2.zero);
            GameObject planting = new GameObject("Planting");
            planting.transform.SetParent(root.transform, false);
            planting.transform.localPosition = new Vector2(-entry.Item2 * 0.04f, entry.Item2 * 0.035f);
            Flowers(planting, entry.Item2 * 0.57f, entry.Item3);
            Save("Planter", root);
        }
    }

    // 꽃 아래의 잎을 별도 Sprite로 두어 꽃 색상이 잎에 영향을 주지 않게 한다
    private static void Flowers(GameObject root, float width, Color tint)
    {
        SpriteRenderer foliage = Visual(root, "Foliage", "Bush/Bush_Round_01", width, Color.white, -2, Vector2.zero);
        foliage.transform.localScale = Vector3.Scale(foliage.transform.localScale, new Vector3(1f, 0.85f, 1f));
        Visual(root, "Blossoms", "Flower/FlowerPatch_01", width * 0.78f, tint, -1, new Vector2(-width * 0.03f, width * 0.07f));
    }

    // 기존 EnvironmentSlot이 사용하는 Tree Prefab의 Placeholder 표시만 교체한다
    private static void UpdateTree()
    {
        const string path = "Assets/Prefabs/Environment/Tree.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            SpriteRenderer visual = root.GetComponentInChildren<SpriteRenderer>(true);
            Sprite sprite = Sprite("Tree/Tree_Medium_01");
            visual.sprite = sprite;
            visual.color = Color.white;
            visual.transform.localScale = Vector3.one * (2.4f / sprite.bounds.size.x);
            visual.transform.localScale = new Vector3(visual.transform.localScale.x, visual.transform.localScale.y, 1f);
            if (!root.GetComponent<VegetationItem>()) root.AddComponent<VegetationItem>();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // 식생 종류와 고정 광원 방향을 가진 독립 루트를 작성한다
    private static GameObject Root(string name, VegetationItem.Kind kind)
    {
        var root = new GameObject(name);
        var data = new SerializedObject(root.AddComponent<VegetationItem>());
        data.FindProperty("kind").enumValueIndex = (int)kind;
        data.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    // PPU를 바꾸지 않고 목표 월드 폭으로 Sprite를 조절한다
    private static SpriteRenderer Visual(GameObject root, string name, string source, float width, Color tint, int order, Vector2 point)
    {
        var child = new GameObject(name);
        child.transform.SetParent(root.transform, false);
        child.transform.localPosition = point;
        var renderer = child.AddComponent<SpriteRenderer>();
        renderer.sprite = Sprite(source);
        renderer.color = tint;
        renderer.sortingOrder = order;
        float scale = width / renderer.sprite.bounds.size.x;
        child.transform.localScale = new Vector3(scale, scale, 1f);
        return renderer;
    }

    // 승인 이미지 또는 생성한 이미지의 Single Sprite를 조회한다
    private static Sprite Sprite(string name)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(images + name + ".png");
        if (!sprite) throw new InvalidOperationException("식생 Sprite 누락: " + name);
        return sprite;
    }

    // 같은 경로를 재사용해 Scene과 Prefab 참조를 유지한다
    private static void Save(string folder, GameObject root)
    {
        prefabs[root.name] = PrefabUtility.SaveAsPrefabAsset(root, library + folder + "/" + root.name + ".prefab");
        Object.DestroyImmediate(root);
    }

    // 필요한 폴더만 순서대로 생성한다
    private static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/');
        Folder(path.Substring(0, split));
        AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
    }
}
