#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>지정된 11개 차량 이미지만 새 프리팹으로 만든다. 기존 씬/원본 이미지/에셋을 덮어쓰지 않는다.</summary>
public static class CarPrefabBuilder
{
    // 생성 범위와 크기 기준이다. 루트는 동일한 0.2 스케일을 사용하고 Visual 자식만 제원 비율을 보정한다.
    private const string TargetFolder = "Assets/Prefabs/Car";
    private const string SpriteFolder = TargetFolder + "/Sprites";
    private const string SourceScenePath = "Assets/Scenes/GamePlay.unity";
    private const string ReportFolder = "output/car-prefabs-20261013";
    private const float RootScale = 0.2f;
    private const float ReferenceWidthMm = 1995f;
    private const float ReferenceLengthMm = 5255f;
    private const float SizeTolerance = 0.0001f;
    // 사용자가 승인한 모델 기준과 제조사 공식 제원이다. 차량 물리/AI 컴포넌트는 추가하지 않는다.
    private static readonly Spec[] Specs =
    {
        new Spec("Ambulance", 5255, 1995), new Spec("Avante", 4710, 1825),
        new Spec("Bus", 10995, 2490), new Spec("Carnival", 5155, 1995),
        new Spec("Grandeur", 5035, 1880), new Spec("PCX", 1935, 740),
        new Spec("PorterII", 5100, 1740), new Spec("Ray", 3595, 1595),
        new Spec("SonataTaxi", 4945, 1860), new Spec("Sorento", 4815, 1900),
        new Spec("Sportage", 4685, 1865)
    };

    /// <summary>메뉴 또는 명시적인 executeMethod로 생성한다. 기존 목적지 파일이 하나라도 있으면 쓰기 전에 중단한다.</summary>
    [MenuItem("Tools/CodeBlue Rush/Create Car Prefabs")]
    public static void CreateAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("차량 프리팹은 Edit Mode에서 생성해주세요.");
        }
        // 사용자 씬의 활성 상태를 복원하며 이번 작업이 연 임시 씬만 닫는다.
        var previousActiveScene = SceneManager.GetActiveScene();
        var openedSourceScene = false;
        var sourceScene = SceneManager.GetSceneByPath(SourceScenePath);
        var workScene = default(Scene);
        try
        {
            foreach (var spec in Specs)
            {
                if (File.Exists(ProjectFile(PrefabPath(spec))) || File.Exists(ProjectFile(SpritePath(spec))))
                {
                    throw new InvalidOperationException($"{spec.Name}: 대상 파일이 이미 있습니다. 기존 프리팹은 자동으로 덮어쓰지 않습니다.");
                }
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(ImagePath(spec)) == null)
                {
                    throw new InvalidOperationException($"{ImagePath(spec)}: 원본 텍스처가 없습니다.");
                }
            }
            if (!sourceScene.IsValid() || !sourceScene.isLoaded)
            {
                sourceScene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Additive);
                openedSourceScene = true;
            }
            // 구급차의 실제 Sprite 크기, 머티리얼과 표시 순서를 읽는다. 기존 씬을 저장하거나 변경하지 않는다.
            var reference = FindAmbulance(sourceScene);
            var scale = reference.transform.lossyScale;
            if ((scale - Vector3.one * RootScale).sqrMagnitude > SizeTolerance * SizeTolerance)
            {
                throw new InvalidOperationException("구급차의 월드 Scale이 (0.2,0.2,0.2)가 아닙니다. 기준을 먼저 확인해주세요.");
            }
            var baseline = (Vector2)reference.sprite.bounds.size;
            // 배치 모드의 초기 빈 씬을 닫아도 유지되는 에셋 참조와 표시 값을 먼저 복사한다.
            var material = reference.sharedMaterial;
            var sortingLayerId = reference.sortingLayerID;
            var sortingOrder = reference.sortingOrder;
            var crops = new Rect[Specs.Length];
            for (var i = 0; i < Specs.Length; i++)
            {
                crops[i] = ReadAlphaRect(ImagePath(Specs[i]));
            }
            // 새 임시 씬을 활성화해 작업용 GameObject가 사용자의 열린 씬을 dirty로 만들지 않게 한다.
            // 배치 모드는 작업 소유 프로세스의 저장 안 된 초기 씬 때문에 Additive 생성이 제한될 수 있다.
            workScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(workScene);
            EnsureFolder(TargetFolder);
            EnsureFolder(SpriteFolder);
            var report = new Report();
            foreach (var spec in Specs)
            {
                // 해당 원본 PNG 전체의 알파 경계로 Sprite를 생성해 잘못된 자동 슬라이싱을 우회한다.
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ImagePath(spec));
                var crop = crops[Array.IndexOf(Specs, spec)];
                var sprite = Sprite.Create(texture, crop, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                sprite.name = spec.Name + "_Full";
                try
                {
                    AssetDatabase.CreateAsset(sprite, SpritePath(spec));
                    AssetDatabase.SaveAssetIfDirty(sprite);
                }
                catch
                {
                    // 저장 전 실패한 네이티브 Sprite만 해제한다. 저장된 에셋과 생성된 메타데이터는 보존한다.
                    if (sprite != null && !EditorUtility.IsPersistent(sprite))
                    {
                        UnityEngine.Object.DestroyImmediate(sprite);
                    }
                    throw;
                }
                var localSize = new Vector2(baseline.x * spec.WidthMm / ReferenceWidthMm,
                    baseline.y * spec.LengthMm / ReferenceLengthMm);
                var root = new GameObject(spec.Name);
                try
                {
                    root.transform.localScale = Vector3.one * RootScale;
                    var visual = new GameObject("Visual");
                    visual.transform.SetParent(root.transform, false);
                    visual.transform.localScale = new Vector3(localSize.x / sprite.bounds.size.x,
                        localSize.y / sprite.bounds.size.y, 1f);
                    var renderer = visual.AddComponent<SpriteRenderer>();
                    renderer.sprite = sprite;
                    renderer.sharedMaterial = material;
                    renderer.sortingLayerID = sortingLayerId;
                    renderer.sortingOrder = sortingOrder;
                    renderer.color = Color.white;
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(spec), out var saved);
                    if (!saved)
                    {
                        throw new InvalidOperationException($"{spec.Name}: 프리팹 저장에 실패했습니다. 생성된 파일을 보존했습니다.");
                    }
                    report.Vehicles.Add(ValidatePrefab(spec, localSize * RootScale));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
            Directory.CreateDirectory(ProjectFile(ReportFolder));
            File.WriteAllText(ProjectFile(ReportFolder + "/prefab-verification.json"), JsonUtility.ToJson(report, true));
            Debug.Log($"차량 프리팹 {report.Vehicles.Count}개 생성 및 크기 검증 완료: {TargetFolder}");
        }
        finally
        {
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(previousActiveScene);
            }
            if (workScene.IsValid() && workScene.isLoaded)
            {
                EditorSceneManager.CloseScene(workScene, true);
            }
            if (openedSourceScene && sourceScene.IsValid() && sourceScene.isLoaded)
            {
                EditorSceneManager.CloseScene(sourceScene, true);
            }
        }
    }

    /// <summary>기존 GamePlay 씬의 루트 구급차 Renderer를 읽기 전용으로 찾는다.</summary>
    private static SpriteRenderer FindAmbulance(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == "Ambulance" && root.TryGetComponent<SpriteRenderer>(out var renderer)
                && renderer.sprite != null && renderer.sharedMaterial != null)
            {
                return renderer;
            }
        }
        throw new InvalidOperationException("GamePlay 씬의 Ambulance 루트에 Sprite와 머티리얼이 필요합니다.");
    }

    /// <summary>원본 파일의 알파 픽셀 경계를 계산한다. TextureImporter나 원본 .meta는 변경하지 않는다.</summary>
    private static Rect ReadAlphaRect(string imagePath)
    {
        // CPU용 복사 텍스처는 이 함수가 소유하며 성공/실패 모두 즉시 파괴한다.
        var copy = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(copy, File.ReadAllBytes(ProjectFile(imagePath)), false))
            {
                throw new InvalidOperationException($"{imagePath}: PNG를 읽을 수 없습니다.");
            }
            var source = AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath);
            if (source == null || source.width != copy.width || source.height != copy.height)
            {
                throw new InvalidOperationException($"{imagePath}: import 해상도가 원본과 다릅니다. 자동으로 import 설정을 바꾸지 않습니다.");
            }
            // LoadImage의 픽셀 배열은 Unity의 아래쪽 원점 좌표이므로 Sprite.Create에 그대로 전달할 수 있다.
            var pixels = copy.GetPixels32();
            var minX = copy.width;
            var minY = copy.height;
            var maxX = -1;
            var maxY = -1;
            for (var y = 0; y < copy.height; y++)
            {
                for (var x = 0; x < copy.width; x++)
                {
                    if (pixels[y * copy.width + x].a == 0)
                    {
                        continue;
                    }
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }
            if (maxX < minX || maxY < minY)
            {
                throw new InvalidOperationException($"{imagePath}: 차량 픽셀이 없습니다.");
            }
            return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(copy);
        }
    }

    /// <summary>Unity가 저장한 실제 프리팹을 다시 열어 Scale/표시 크기/렌더러 수를 검증한다.</summary>
    private static VehicleResult ValidatePrefab(Spec spec, Vector2 expectedWorldSize)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath(spec));
        try
        {
            var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
            if ((root.transform.localScale - Vector3.one * RootScale).sqrMagnitude > SizeTolerance * SizeTolerance
                || renderers.Length != 1 || renderers[0].sprite == null || !renderers[0].enabled)
            {
                throw new InvalidOperationException($"{spec.Name}: 저장한 Prefab의 Scale/SpriteRenderer가 올바르지 않습니다.");
            }
            var actualSize = (Vector2)renderers[0].bounds.size;
            if ((actualSize - expectedWorldSize).sqrMagnitude > SizeTolerance * SizeTolerance)
            {
                throw new InvalidOperationException($"{spec.Name}: 저장된 표시 크기 {actualSize}가 목표 {expectedWorldSize}와 다릅니다.");
            }
            return new VehicleResult { Name = spec.Name, LengthMm = spec.LengthMm, WidthMm = spec.WidthMm,
                RootScale = root.transform.localScale, WorldSize = actualSize, PrefabPath = PrefabPath(spec) };
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>지정된 폴더만 Unity API로 생성한다. .meta는 Unity가 생성한다.</summary>
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))))
        {
            throw new InvalidOperationException($"{path}: 대상 폴더를 생성하지 못했습니다.");
        }
    }

    /// <summary>대상 차량의 원본 이미지 경로다.</summary>
    private static string ImagePath(Spec spec) => "Assets/Images/Car/" + spec.Name + ".png";
    /// <summary>자동 생성할 전체 Sprite 에셋의 지정 경로다.</summary>
    private static string SpritePath(Spec spec) => SpriteFolder + "/" + spec.Name + ".asset";
    /// <summary>요청된 차량 프리팹 경로다.</summary>
    private static string PrefabPath(Spec spec) => TargetFolder + "/" + spec.Name + ".prefab";
    /// <summary>파일 입출력에는 Unity가 연 프로젝트의 절대 경로를 사용한다.</summary>
    private static string ProjectFile(string relativePath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", relativePath));

    /// <summary>차량의 고정 크기 제원이다. 단위는 mm다.</summary>
    private sealed class Spec
    {
        public readonly string Name;
        public readonly int LengthMm;
        public readonly int WidthMm;
        public Spec(string name, int lengthMm, int widthMm)
        {
            Name = name;
            LengthMm = lengthMm;
            WidthMm = widthMm;
        }
    }
    // 검증 결과 파일용 단순 직렬화 데이터이며 런타임/프로젝트 에셋으로 사용하지 않는다.
    [Serializable]
    private sealed class Report
    {
        public List<VehicleResult> Vehicles = new List<VehicleResult>();
    }
    [Serializable]
    private sealed class VehicleResult
    {
        public string Name;
        public int LengthMm;
        public int WidthMm;
        public Vector3 RootScale;
        public Vector2 WorldSize;
        public string PrefabPath;
    }
}
#endif
