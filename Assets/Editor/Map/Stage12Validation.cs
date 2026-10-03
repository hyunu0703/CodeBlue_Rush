using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>12단계 Prefab 생성과 기존 도시 데이터의 연결을 검증한다</summary>
public static class Stage12Validation
{
    private const string CitizenPath = "Assets/Resources/Citizen.prefab";
    private const string ButtonPath = "Assets/Prefabs/SirenButton.prefab";
    private const string PlayKey = "CodeBlueRush.Stage12PlayValidation";
    private static CityMap playMap;
    private static CitizenSpawner playCitizens;
    private static TrafficSpawner playTraffic;
    private static double playStart;
    private static readonly Dictionary<CitizenAI, Vector3> positions = new Dictionary<CitizenAI, Vector3>();
    private static readonly Dictionary<VehicleAI, Vector3> vehiclePositions = new Dictionary<VehicleAI, Vector3>();
    private static int playStep;

    // 씬을 변경하지 않고 재사용할 시민과 버튼 자산을 만든다
    [MenuItem("CodeBlue Rush/Stage 12/Create Prefabs")]
    public static void CreatePrefabs()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(CitizenPath))
        {
            GameObject root = new GameObject("Citizen", typeof(SpriteRenderer), typeof(CitizenAI));
            SpriteRenderer renderer = root.GetComponent<SpriteRenderer>();
            renderer.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            renderer.color = new Color(0.2f, 0.75f, 0.85f);
            renderer.sortingOrder = 7;
            root.transform.localScale = Vector3.one * 0.55f;
            PrefabUtility.SaveAsPrefabAsset(root, CitizenPath);
            Object.DestroyImmediate(root);
        }
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPath))
        {
            GameObject root = new GameObject("SirenButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(SirenButtonUI));
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(160f, 50f);
            root.GetComponent<Image>().color = new Color(0.15f, 0.3f, 0.5f);
            GameObject text = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            text.transform.SetParent(root.transform, false);
            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Text label = text.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = "SIREN OFF";
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            SerializedObject serialized = new SerializedObject(root.GetComponent<SirenButtonUI>());
            serialized.FindProperty("label").objectReferenceValue = label;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, ButtonPath);
            Object.DestroyImmediate(root);
        }
        const string ambulancePath = "Assets/Prefabs/Ambulance.prefab";
        GameObject ambulance = PrefabUtility.LoadPrefabContents(ambulancePath);
        try
        {
            if (!ambulance.GetComponent<SirenController>())
            {
                ambulance.AddComponent<SirenController>();
                PrefabUtility.SaveAsPrefabAsset(ambulance, ambulancePath);
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(ambulance); }
        AssetDatabase.SaveAssets();
    }

    // 실제 Prefab과 새 도시의 보도 및 횡단 참조를 씬 저장 없이 검사한다
    [MenuItem("CodeBlue Rush/Stage 12/Validate Data")]
    public static void ValidateData()
    {
        int checks = 0;
        Require(AssetDatabase.LoadAssetAtPath<GameObject>(CitizenPath)?.GetComponent<CitizenAI>(), "Citizen Prefab", ref checks);
        GameObject button = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPath);
        Require(button && button.GetComponent<Button>() && button.GetComponent<SirenButtonUI>(), "Siren Button Prefab", ref checks);
        GameObject ambulance = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Ambulance.prefab");
        Require(ambulance && ambulance.GetComponent<SirenController>(), "Ambulance Siren", ref checks);
        string[] roads = { "Straight", "Corner", "TJunction", "Intersection", "PassingStraight" };
        foreach (string name in roads)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/City/" + name + ".prefab");
            Require(asset, name + " Prefab", ref checks);
            Require(asset.GetComponent<RoadChunk>().Validate(out _), name + " Road", ref checks);
            foreach (SidewalkPath path in asset.GetComponentsInChildren<SidewalkPath>())
            {
                Require(path.PointCount >= 2, name + " Sidewalk", ref checks);
                if (path.Crosswalk)
                    Require(path.Across && path.Across.Crosswalk == path.Crosswalk && path.Crosswalk.Signal && path.Crosswalk.CrosswalkStart && path.Crosswalk.CrosswalkEnd && path.Crosswalk.PedestrianWaitPointA && path.Crosswalk.PedestrianWaitPointB, name + " Crosswalk", ref checks);
            }
        }
        GameObject root = new GameObject("Stage12Validation");
        try
        {
            CityMap map = root.AddComponent<CityMap>();
            CitySamples.Configure(map);
            Require(map.Initialize(out string error), error ?? "City generation", ref checks);
            Require(map.SidewalkPathCount > 0, "City sidewalk cache", ref checks);
            SidewalkPath old = map.GetSidewalkPath(0);
            for (int i = 0; i < map.SidewalkPathCount; i++)
                Require(map.ContainsSidewalkPath(map.GetSidewalkPath(i)), "City sidewalk ownership", ref checks);
            Require(FixedMapScene.ReloadFixture(map, out error), error ?? "City replacement", ref checks);
            Require(!map.ContainsSidewalkPath(old), "Old sidewalk invalidation", ref checks);
        }
        finally { Object.DestroyImmediate(root); }
        Debug.Log("Stage12 data validation passed: " + checks + " checks");
    }

    // 배치 모드에서 기존 씬 저장 없이 12단계 런타임 연결을 확인한다
    public static void RunPlayBatch()
    {
        SessionState.SetBool(PlayKey, true);
        RegisterPlay();
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    private static void RegisterPlay()
    {
        EditorApplication.playModeStateChanged -= PlayStateChanged;
        EditorApplication.playModeStateChanged += PlayStateChanged;
    }

    private static void PlayStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PlayKey, false))
            return;
        try
        {
            GameObject root = new GameObject("Stage12 Runtime Validation");
            playMap = root.AddComponent<CityMap>();
            CitySamples.Configure(playMap);
            Require(playMap.Initialize(out string error), error ?? "Play city", ref playStep);
            GameObject vehicle = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Ambulance.prefab"));
            vehicle.transform.position = new Vector3(80f, 80f, 0f);
            SirenController siren = vehicle.GetComponent<SirenController>();
            GameObject cameraObject = new GameObject("Stage12 Camera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            camera.transform.position = new Vector3(80f, 80f, -10f);
            playTraffic = root.AddComponent<TrafficSpawner>();
            playTraffic.Configure(playMap, vehicle.transform, camera, AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/TrafficVehicle.prefab").GetComponent<VehicleAI>());
            playCitizens = root.GetComponent<CitizenSpawner>();
            Require(playCitizens, "Runtime citizen binding", ref playStep);
            TrafficSignalController signal = playMap.GetComponentInChildren<TrafficSignalController>();
            Require(signal && signal.ZoneCount > 0, "Runtime signal", ref playStep);
            VehicleStopZone zone = signal.GetZone(0);
            bool vehicleGreen = zone.IsGreen;
            signal.CanPedestrianCross(zone);
            GameObject button = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPath));
            button.GetComponent<SirenButtonUI>().Bind(siren);
            int changes = 0;
            siren.Changed += _ => changes++;
            button.GetComponent<Button>().onClick.Invoke();
            Require(siren.IsOn && changes == 1, "Siren first click", ref playStep);
            Require(zone.IsGreen == vehicleGreen, "Siren preserves signal", ref playStep);
            button.GetComponent<Button>().onClick.Invoke();
            Require(!siren.IsOn && changes == 2, "Siren second click", ref playStep);
            playStart = EditorApplication.timeSinceStartup;
            EditorApplication.update -= PlayTick;
            EditorApplication.update += PlayTick;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SessionState.SetBool(PlayKey, false);
            EditorApplication.Exit(1);
        }
    }

    private static void PlayTick()
    {
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - playStart;
            if (elapsed < 5d)
                return;
            if (positions.Count == 0)
            {
                Require(playCitizens.ActiveCount > 0, "Citizen spawn", ref playStep);
                Require(playTraffic.ActiveCount > 0, "Traffic spawn", ref playStep);
                foreach (CitizenAI citizen in playMap.GetComponentsInChildren<CitizenAI>())
                    if (citizen.isActiveAndEnabled)
                        positions.Add(citizen, citizen.transform.position);
                Require(positions.Count > 0, "Active citizen", ref playStep);
                for (int i = 0; i < playTraffic.ActiveCount; i++)
                {
                    VehicleAI vehicle = playTraffic.GetVehicle(i);
                    if (vehicle) vehiclePositions.Add(vehicle, vehicle.transform.position);
                }
                return;
            }
            if (elapsed < 7d)
                return;
            bool moved = false;
            foreach (KeyValuePair<CitizenAI, Vector3> sample in positions)
                if (sample.Key && (sample.Key.transform.position - sample.Value).sqrMagnitude > 0.01f)
                    moved = true;
            Require(moved, "Sidewalk movement", ref playStep);
            moved = false;
            foreach (KeyValuePair<VehicleAI, Vector3> sample in vehiclePositions)
                if (sample.Key && (sample.Key.transform.position - sample.Value).sqrMagnitude > 0.01f)
                    moved = true;
            Require(moved, "Traffic movement", ref playStep);
            SidewalkPath old = playMap.GetSidewalkPath(0);
            Require(FixedMapScene.ReloadFixture(playMap, out string error), error ?? "Play city replacement", ref playStep);
            Require(playCitizens.ActiveCount == 0 && !playMap.ContainsSidewalkPath(old), "Citizen city cleanup", ref playStep);
            Debug.Log("Stage12 play validation passed: " + playStep + " checks");
            SessionState.SetBool(PlayKey, false);
            EditorApplication.update -= PlayTick;
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SessionState.SetBool(PlayKey, false);
            EditorApplication.update -= PlayTick;
            EditorApplication.Exit(1);
        }
    }

    private static void Require(bool success, string message, ref int checks)
    {
        if (!success) throw new InvalidOperationException("Stage12: " + message);
        checks++;
    }
}

