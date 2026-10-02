using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>픽업 현장 Prefab과 구급차 탑승점을 구성하고 독립 검증 씬을 제공한다</summary>
[CustomEditor(typeof(PatientPickup))]
public sealed class PatientPickupEditor : Editor
{
    internal const string SitePath = "Assets/Prefabs/PatientPickup.prefab";

    // 상태와 설정 복구 버튼을 Inspector에 표시한다
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        PatientPickup pickup = (PatientPickup)target;
        EditorGUILayout.LabelField("Boarding / Paused", pickup.IsBoarding + " / " + pickup.IsPaused);
        if (!string.IsNullOrEmpty(pickup.Error))
            EditorGUILayout.HelpBox(pickup.Error, MessageType.Warning);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Refresh Site After Repair"))
                pickup.RefreshSite();
        }
    }

    // 기존 구급차의 차체 뒤에 탑승점만 추가하고 현장 Prefab을 작성한다
    [MenuItem("CodeBlue Rush/Patient Pickup/Create Assets")]
    public static void CreateAssets()
    {
        string path = "Assets/Prefabs/Ambulance.prefab";
        GameObject ambulance = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (!ambulance.transform.Find("RearBoardingPoint"))
            {
                Transform rear = new GameObject("RearBoardingPoint").transform;
                rear.SetParent(ambulance.transform, false);
                rear.localPosition = new Vector3(0f, -1.3f, 0f);
                PrefabUtility.SaveAsPrefabAsset(ambulance, path);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(ambulance);
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SitePath))
            return;
        GameObject root = new GameObject("Patient Pickup Site");
        try
        {
            CircleCollider2D area = root.AddComponent<CircleCollider2D>();
            area.isTrigger = true;
            area.radius = 2.5f;
            PatientPickup pickup = root.AddComponent<PatientPickup>();
            Transform patient = CreateActor(root.transform, "Patient", new Color(1f, 0.35f, 0.25f), new Vector2(0.4f, 0.65f));
            Transform medic = CreateActor(root.transform, "Paramedic", new Color(0.2f, 0.7f, 1f), new Vector2(0.4f, 0.55f));
            SerializedObject settings = new SerializedObject(pickup);
            settings.FindProperty("patient").objectReferenceValue = patient;
            settings.FindProperty("paramedic").objectReferenceValue = medic;
            settings.ApplyModifiedPropertiesWithoutUndo();
            patient.gameObject.SetActive(false);
            medic.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root, SitePath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
        AssetDatabase.SaveAssets();
    }

    // 콜라이더나 AI 없이 구별 가능한 간단한 현장 스프라이트를 작성한다
    private static Transform CreateActor(Transform parent, string name, Color color, Vector2 size)
    {
        GameObject actor = new GameObject(name);
        actor.transform.SetParent(parent, false);
        SpriteRenderer renderer = actor.AddComponent<SpriteRenderer>();
        renderer.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        renderer.color = color;
        renderer.sortingOrder = 5;
        Vector3 bounds = renderer.sprite.bounds.size;
        actor.transform.localScale = new Vector3(size.x / bounds.x, size.y / bounds.y, 1f);
        return actor.transform;
    }

    // 5단계 데모를 보존하면서 픽업 현장을 연결한 별도 씬을 저장한다
    [MenuItem("CodeBlue Rush/Patient Pickup/Create Demo Scene")]
    public static void CreateDemo()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        CreateAssets();
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/PatientReportDemo.unity");
        PatientReport report = Object.FindFirstObjectByType<PatientReport>();
        AmbulanceController vehicle = Object.FindFirstObjectByType<AmbulanceController>();
        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SitePath));
        PatientPickup pickup = root.GetComponent<PatientPickup>();
        pickup.Configure(report, vehicle.GetComponent<Rigidbody2D>(), vehicle.transform.Find("RearBoardingPoint"));
        PrefabUtility.RecordPrefabInstancePropertyModifications(pickup);
        Camera camera = Camera.main;
        camera.orthographicSize = 7.5f;
        AmbulanceCamera follow = camera.gameObject.AddComponent<AmbulanceCamera>();
        SerializedObject cameraSettings = new SerializedObject(follow);
        cameraSettings.FindProperty("target").objectReferenceValue = vehicle.GetComponent<Rigidbody2D>();
        cameraSettings.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/PatientPickupDemo.unity");
        Selection.activeGameObject = root;
    }
}
