using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>시민 AI와 사이렌 시스템을 실제로 연결한 독립 12단계 Demo Scene을 작성한다</summary>
public static class Stage12DemoScene
{
    private const string ScenePath = "Assets/Scenes/Stage12Demo.unity";
    private const string CitizenPath = "Assets/Resources/Citizen.prefab";
    private const string TrafficPath = "Assets/Prefabs/TrafficVehicle.prefab";
    private const string AmbulancePath = "Assets/Prefabs/Ambulance.prefab";
    private const string ButtonPath = "Assets/Prefabs/SirenButton.prefab";
    private const string PlayKey = "CodeBlueRush.Stage12DemoPlay";
    private static double playStart;
    private static bool playChecked;

    // 기존 Scene을 열거나 저장하지 않고 새 12단계 Scene을 만든다
    [MenuItem("CodeBlue Rush/Stage 12/Create Demo Scene")]
    public static void CreateDemoScene()
    {
        EnsureAssets();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildScene(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        Debug.Log("Stage12 Demo Scene created: " + ScenePath);
    }

    // 저장된 12단계 Scene을 실제 Play로 실행해 모든 런타임 연결을 확인한다
    public static void RunPlayBatch()
    {
        if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
            CreateDemoScene();
        EditorSceneManager.OpenScene(ScenePath);
        SessionState.SetBool(PlayKey, true);
        playStart = EditorApplication.timeSinceStartup + 2d;
        playChecked = false;
        EditorApplication.update -= EnterPlay;
        EditorApplication.update += EnterPlay;
        EditorApplication.update -= CheckPlay;
        EditorApplication.update += CheckPlay;
    }

    [InitializeOnLoadMethod]
    private static void RegisterPlay()
    {
        if (!SessionState.GetBool(PlayKey, false))
            return;
        playChecked = false;
        EditorApplication.update -= CheckPlay;
        EditorApplication.update += CheckPlay;
    }

    private static void EnterPlay()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < playStart)
            return;
        EditorApplication.update -= EnterPlay;
        EditorApplication.EnterPlaymode();
    }

    private static void CheckPlay()
    {
        if (playChecked || !EditorApplication.isPlaying || Time.time < 5f)
            return;
        playChecked = true;
        try
        {
            CityMap map = Object.FindFirstObjectByType<CityMap>();
            TrafficSpawner traffic = Object.FindFirstObjectByType<TrafficSpawner>();
            CitizenSpawner citizens = Object.FindFirstObjectByType<CitizenSpawner>();
            SirenController siren = Object.FindFirstObjectByType<SirenController>();
            SirenButtonUI ui = Object.FindFirstObjectByType<SirenButtonUI>();
            if (!map || !map.IsReady || !traffic || !citizens || !siren || !ui || traffic.ActiveCount == 0 || citizens.ActiveCount == 0)
                throw new InvalidOperationException("Stage12Demo 런타임 연결 또는 생성 결과가 유효하지 않습니다.");
            Button button = ui.GetComponent<Button>();
            bool before = siren.IsOn;
            button.onClick.Invoke();
            if (siren.IsOn == before)
                throw new InvalidOperationException("SirenButton 클릭이 SirenController를 전환하지 않았습니다.");
            button.onClick.Invoke();
            if (siren.IsOn != before)
                throw new InvalidOperationException("SirenButton 두 번째 클릭이 원래 상태로 복귀하지 않았습니다.");
            Debug.Log("Stage12 Demo Play validation passed: City, traffic, citizens and siren UI");
            SessionState.SetBool(PlayKey, false);
            EditorApplication.update -= CheckPlay;
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SessionState.SetBool(PlayKey, false);
            EditorApplication.update -= CheckPlay;
            EditorApplication.Exit(1);
        }
    }

    // 필요한 12단계 자산이 없는 경우 기존 생성기를 재사용한다
    private static void EnsureAssets()
    {
        CitySamples.CreatePrefabs();
        Stage12Validation.CreatePrefabs();
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(TrafficPath))
            throw new InvalidOperationException("TrafficVehicle.prefab이 없습니다.");
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(AmbulancePath))
            throw new InvalidOperationException("Ambulance.prefab이 없습니다.");
    }

    // 도시, 플레이어, AI, 카메라와 UI를 한 Scene에 연결한다
    private static void BuildScene(Scene scene)
    {
        GameObject mapObject = new GameObject("Stage12 City");
        SceneManager.MoveGameObjectToScene(mapObject, scene);
        CityMap map = mapObject.AddComponent<CityMap>();
        CitySamples.Configure(map);
        Set(map, "initialSeed", 12345);
        Set(map, "randomSeedOnStart", false);
        Set(map, "density", 35);

        GameObject ambulanceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(AmbulancePath);
        GameObject ambulanceObject = (GameObject)PrefabUtility.InstantiatePrefab(ambulanceAsset, scene);
        ambulanceObject.name = "Player Ambulance";
        ambulanceObject.transform.position = new Vector3(80f, 80f, 0f);
        SirenController siren = ambulanceObject.GetComponent<SirenController>();
        if (!siren)
            throw new InvalidOperationException("Ambulance.prefab에 SirenController가 없습니다.");

        GameObject cameraObject = new GameObject("Stage12 Camera", typeof(Camera), typeof(AudioListener), typeof(AmbulanceCamera));
        SceneManager.MoveGameObjectToScene(cameraObject, scene);
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 18f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.12f, 0.1f);
        camera.transform.position = ambulanceObject.transform.position + new Vector3(0f, 0f, -10f);
        camera.transform.rotation = Quaternion.identity;
        Set(cameraObject.GetComponent<AmbulanceCamera>(), "target", ambulanceObject.GetComponent<Rigidbody2D>());
        Set(cameraObject.GetComponent<AmbulanceCamera>(), "map", map);

        GameObject trafficPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrafficPath);
        TrafficSpawner traffic = mapObject.AddComponent<TrafficSpawner>();
        traffic.enabled = false;
        Set(traffic, "map", map);
        Set(traffic, "player", ambulanceObject.transform);
        Set(traffic, "view", camera);
        Set(traffic, "prefab", trafficPrefab.GetComponent<VehicleAI>());
        Set(traffic, "maxVehicles", 16);
        Set(traffic, "spawnNear", 12f);
        Set(traffic, "spawnFar", 38f);
        Set(traffic, "despawnDistance", 55f);
        traffic.enabled = true;

        CitizenAI citizenPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CitizenPath)?.GetComponent<CitizenAI>();
        CitizenSpawner citizens = mapObject.AddComponent<CitizenSpawner>();
        citizens.enabled = false;
        Set(citizens, "map", map);
        Set(citizens, "player", ambulanceObject.transform);
        Set(citizens, "view", camera);
        Set(citizens, "prefab", citizenPrefab);
        Set(citizens, "maxCitizens", 24);
        Set(citizens, "spawnNear", 12f);
        Set(citizens, "spawnFar", 38f);
        Set(citizens, "despawnDistance", 55f);
        citizens.enabled = true;

        CreateCanvas(scene, siren);
    }

    // 클릭 가능한 사이렌 버튼과 간단한 조작 안내를 배치한다
    private static void CreateCanvas(Scene scene, SirenController siren)
    {
        GameObject canvasObject = new GameObject("Stage12 UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(canvasObject, scene);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject buttonAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPath);
        GameObject buttonObject = (GameObject)PrefabUtility.InstantiatePrefab(buttonAsset, scene);
        buttonObject.name = "Siren Button";
        buttonObject.transform.SetParent(canvas.transform, false);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(1f, 0f);
        buttonRect.anchorMax = new Vector2(1f, 0f);
        buttonRect.anchoredPosition = new Vector2(-110f, 65f);
        Set(buttonObject.GetComponent<SirenButtonUI>(), "siren", siren);

        GameObject labelObject = new GameObject("Instructions", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelObject.transform.SetParent(canvas.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(0f, 1f);
        labelRect.pivot = new Vector2(0f, 1f);
        labelRect.anchoredPosition = new Vector2(24f, -24f);
        labelRect.sizeDelta = new Vector2(500f, 50f);
        Text label = labelObject.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 20;
        label.color = Color.white;
        label.text = "WASD / Arrow Keys: Drive    Click: Siren";

        GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        SceneManager.MoveGameObjectToScene(eventSystem, scene);
    }

    // SerializedField 참조를 Editor에서 안전하게 연결한다
    private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        SerializedObject data = new SerializedObject(target);
        SerializedProperty property = data.FindProperty(field);
        if (property == null)
            throw new InvalidOperationException(target.GetType().Name + "." + field + "를 찾을 수 없습니다.");
        property.objectReferenceValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Set(UnityEngine.Object target, string field, float value)
    {
        SerializedObject data = new SerializedObject(target);
        SerializedProperty property = data.FindProperty(field);
        if (property == null)
            throw new InvalidOperationException(target.GetType().Name + "." + field + "를 찾을 수 없습니다.");
        property.floatValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Set(UnityEngine.Object target, string field, int value)
    {
        SerializedObject data = new SerializedObject(target);
        SerializedProperty property = data.FindProperty(field);
        if (property == null)
            throw new InvalidOperationException(target.GetType().Name + "." + field + "를 찾을 수 없습니다.");
        property.intValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Set(UnityEngine.Object target, string field, bool value)
    {
        SerializedObject data = new SerializedObject(target);
        SerializedProperty property = data.FindProperty(field);
        if (property == null)
            throw new InvalidOperationException(target.GetType().Name + "." + field + "를 찾을 수 없습니다.");
        property.boolValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }
}
