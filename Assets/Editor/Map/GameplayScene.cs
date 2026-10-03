using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>기존 Prefab을 연결한 실제 게임 Scene을 편집 시 작성한다</summary>
public static class GameplayScene
{
    public const string Path = "Assets/Scenes/Gameplay.unity";

    [MenuItem("CodeBlue Rush/Gameplay/Create Scene")]
    public static void Create()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        CityMap map = new GameObject("City").AddComponent<CityMap>();
        FixedMapScene.AttachWorld(map);
        AmbulanceController ambulance = Instance<AmbulanceController>("Ambulance");
        Rigidbody2D body = ambulance.GetComponent<Rigidbody2D>();
        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(AmbulanceCamera));
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 16f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.07f, 0.11f, 0.1f);
        camera.transform.position = new Vector3(0f, 0f, -10f);
        Light2D light = new GameObject("Global Light 2D").AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Global;
        light.intensity = 1f;
        Set(cameraObject.GetComponent<AmbulanceCamera>(), "target", body);
        Set(cameraObject.GetComponent<AmbulanceCamera>(), "map", map);

        GameObject mission = new GameObject("Mission");
        NavigationRoute navigation = mission.AddComponent<NavigationRoute>();
        navigation.Configure(map, ambulance.transform);
        Set(navigation, "snapDistance", 8f);
        PatientReport report = mission.AddComponent<PatientReport>();
        report.Configure(navigation);
        PatientPickup pickup = Instance<PatientPickup>("PatientPickup");
        pickup.Configure(report, body, ambulance.transform.Find("RearBoardingPoint"));
        PrefabUtility.RecordPrefabInstancePropertyModifications(pickup);
        PatientECG ecg = mission.AddComponent<PatientECG>();
        ecg.Configure(report);
        HospitalArrivalZone hospital = Instance<HospitalArrivalZone>("Hospital");
        FixedMapScene.PlaceHospital(hospital, map);
        PrefabUtility.RecordPrefabInstancePropertyModifications(hospital);
        HospitalTransfer transfer = mission.AddComponent<HospitalTransfer>();
        transfer.Configure(report, navigation, ecg, body, new[] { hospital });
        ambulance.GetComponent<AmbulanceCollision>().Configure(report, ecg, transfer);
        PrefabUtility.RecordPrefabInstancePropertyModifications(ambulance.GetComponent<AmbulanceCollision>());

        CitizenSpawner citizens = map.gameObject.AddComponent<CitizenSpawner>();
        citizens.Configure(map, ambulance.transform, camera, ambulance.GetComponent<SirenController>(), AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Citizen.prefab").GetComponent<CitizenAI>());
        TrafficSpawner traffic = map.gameObject.AddComponent<TrafficSpawner>();
        traffic.Configure(map, ambulance.transform, camera, Load<VehicleAI>("TrafficVehicle"));
        GameFlow flow = mission.AddComponent<GameFlow>();
        Set(flow, "map", map);
        Set(flow, "report", report);
        Set(flow, "pickup", pickup);
        Set(flow, "ecg", ecg);
        Set(flow, "transfer", transfer);
        Set(flow, "navigation", navigation);
        Set(flow, "ambulance", ambulance);
        Set(flow, "hospital", hospital);
        FixedMapScene.ConnectSpawn(flow, map, ambulance, cameraObject.GetComponent<AmbulanceCamera>());
        CreateUI(flow, report, ecg, navigation, ambulance);
        EditorSceneManager.SaveScene(scene, Path);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Path, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("Gameplay scene created with production references.");
    }

    // 기존 UI 컴포넌트를 480×854 화면의 고정 영역에 연결한다
    private static void CreateUI(GameFlow flow, PatientReport report, PatientECG ecg, NavigationRoute navigation, AmbulanceController ambulance)
    {
        var root = new GameObject("Game UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(480f, 854f);
        scaler.matchWidthOrHeight = 0.5f;
        RectTransform hud = Rect("HUD", root.transform, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(456f, 276f));
        hud.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.04f, 0.08f, 0.85f);
        Text reportLabel = Label("Report", hud, new Vector2(0f, 100f), new Vector2(440f, 54f), 20, string.Empty);
        PatientReportUI reportUI = reportLabel.gameObject.AddComponent<PatientReportUI>();
        Set(reportUI, "label", reportLabel);
        Set(reportUI, "report", report);
        ECGGraphic graphic = Rect("ECG", hud, Vector2.one * 0.5f, new Vector2(0f, 48f), new Vector2(424f, 40f)).gameObject.AddComponent<ECGGraphic>();
        graphic.Configure(ecg);
        graphic.raycastTarget = false;
        NavigationUI nav = Rect("Navigation", hud, Vector2.one * 0.5f, new Vector2(0f, -44f), new Vector2(240f, 132f)).gameObject.AddComponent<NavigationUI>();
        nav.color = Color.cyan;
        nav.Bind(navigation);
        Text status = Label("Status", hud, new Vector2(0f, -120f), new Vector2(440f, 24f), 16, string.Empty);

        RectTransform controls = Rect("Controls", root.transform, new Vector2(0.5f, 0f), new Vector2(0f, 100f), new Vector2(456f, 176f));
        KeyboardAmbulanceInput input = ambulance.GetComponent<KeyboardAmbulanceInput>();
        DriveButton(controls, "LEFT", new Vector2(-176f, -40f), input, DriveButtonUI.Control.Left);
        DriveButton(controls, "RIGHT", new Vector2(-72f, -40f), input, DriveButtonUI.Control.Right);
        DriveButton(controls, "BRAKE", new Vector2(72f, -40f), input, DriveButtonUI.Control.Brake);
        DriveButton(controls, "GAS", new Vector2(176f, -40f), input, DriveButtonUI.Control.Throttle);
        Text wheel = Label("Steering", controls, new Vector2(-124f, 46f), new Vector2(96f, 64f), 42, "⊕");
        Set(wheel.gameObject.AddComponent<SteeringWheelUI>(), "controller", ambulance);
        GameObject siren = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/SirenButton.prefab"));
        siren.transform.SetParent(controls, false);
        RectTransform sirenRect = siren.GetComponent<RectTransform>();
        sirenRect.anchorMin = sirenRect.anchorMax = Vector2.one * 0.5f;
        sirenRect.anchoredPosition = new Vector2(110f, 46f);
        sirenRect.sizeDelta = new Vector2(196f, 60f);
        siren.GetComponent<SirenButtonUI>().Bind(ambulance.GetComponent<SirenController>());
        PrefabUtility.RecordPrefabInstancePropertyModifications(siren.GetComponent<SirenButtonUI>());

        RectTransform panel = Rect("Result", root.transform, Vector2.one * 0.5f, Vector2.zero, new Vector2(440f, 300f));
        panel.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.05f, 0.09f, 0.98f);
        Text title = Label("Title", panel, new Vector2(0f, 90f), new Vector2(420f, 65f), 26, "PATIENT DELIVERED");
        Text stars = Label("Stars", panel, new Vector2(0f, 15f), new Vector2(420f, 65f), 44, "★★★");
        Button next = Button(panel, "Continue", new Vector2(0f, -90f), new Vector2(340f, 64f));
        Text nextLabel = Label("Label", next.transform, Vector2.zero, new Vector2(330f, 60f), 24, "NEXT MISSION");
        GameFlowUI ui = root.AddComponent<GameFlowUI>();
        Set(ui, "flow", flow);
        Set(ui, "panel", panel.gameObject);
        Set(ui, "controls", controls.gameObject);
        Set(ui, "title", title);
        Set(ui, "stars", stars);
        Set(ui, "buttonLabel", nextLabel);
        Set(ui, "status", status);
        Set(ui, "continueButton", next);
        panel.gameObject.SetActive(false);
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    private static void DriveButton(Transform parent, string name, Vector2 position, KeyboardAmbulanceInput input, DriveButtonUI.Control control)
    {
        Button button = Button(parent, name, position, new Vector2(92f, 72f));
        Label("Label", button.transform, Vector2.zero, new Vector2(90f, 68f), 19, name);
        DriveButtonUI drive = button.gameObject.AddComponent<DriveButtonUI>();
        Set(drive, "input", input);
        var data = new SerializedObject(drive);
        data.FindProperty("control").enumValueIndex = (int)control;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Button Button(Transform parent, string name, Vector2 position, Vector2 size)
    {
        RectTransform rect = Rect(name, parent, Vector2.one * 0.5f, position, size);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.1f, 0.28f, 0.4f);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        return button;
    }

    private static Text Label(string name, Transform parent, Vector2 position, Vector2 size, int fontSize, string text)
    {
        Text label = Rect(name, parent, Vector2.one * 0.5f, position, size).gameObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = fontSize;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.raycastTarget = false;
        label.text = text;
        return label;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static T Load<T>(string name) where T : Component
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + name + ".prefab").GetComponent<T>();
    }

    private static T Instance<T>(string name) where T : Component
    {
        return ((GameObject)PrefabUtility.InstantiatePrefab(Load<T>(name).gameObject)).GetComponent<T>();
    }

    private static void Set(Object target, string field, Object value)
    {
        var data = new SerializedObject(target);
        data.FindProperty(field).objectReferenceValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Set(Object target, string field, float value)
    {
        var data = new SerializedObject(target);
        data.FindProperty(field).floatValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }
}
