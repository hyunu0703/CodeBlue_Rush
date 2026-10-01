using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>런타임 미션 없이 목적지를 시험하고 세로 화면 네비게이션 데모를 구성한다</summary>
[CustomEditor(typeof(NavigationRoute))]
public sealed class NavigationEditor : Editor
{
    private Vector3 destination;

    // Play 모드에서 목적지 지정과 명시적 재탐색을 시험한다
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        NavigationRoute route = (NavigationRoute)target;
        EditorGUILayout.LabelField("Status", route.Status.ToString());
        EditorGUILayout.LabelField("Lane / Point Count", route.Lanes.Count + " / " + route.Points.Count);
        destination = EditorGUILayout.Vector3Field("Test Destination", destination);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Set Test Destination"))
                route.SetDestination(destination);
            if (GUILayout.Button("Recalculate From Player"))
                route.Recalculate();
            if (GUILayout.Button("Clear Destination"))
                route.ClearDestination();
            if (GUILayout.Button("Preview Generated Road Route"))
                Preview(route);
        }
    }

    // 생성된 차선 위에 테스트 차량을 놓고 실제 도시의 반대편 차선으로 경로를 요청한다
    internal static void Preview(NavigationRoute route)
    {
        if (!route.Map || !route.Map.IsReady)
            return;
        SerializedObject data = new SerializedObject(route);
        Transform player = data.FindProperty("player").objectReferenceValue as Transform;
        if (!player)
            return;
        TrafficLane start = route.Map.GetLane(0);
        start.TrySample(start.Length * 0.5f, out Vector3 position, out Vector3 direction);
        player.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, direction));
        TrafficLane end = route.Map.GetLane(route.Map.LaneCount - 1);
        route.SetDestination(end, end.Length * 0.5f);
    }

    // 기존 씬을 보존하고 실제 구급차 Prefab과 480×854 Canvas의 별도 데모를 구성한다
    [MenuItem("CodeBlue Rush/Navigation/Create Demo Scene")]
    public static void CreateDemo()
    {
        CityMap map = CitySamples.CreateDemoMap();
        if (!map)
            return;
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Ambulance.prefab");
        GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        player.name = "Ambulance";
        NavigationRoute route = map.gameObject.AddComponent<NavigationRoute>();
        route.Configure(map, player.transform);
        GameObject canvasObject = new GameObject("Navigation Canvas", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(480f, 854f);
        scaler.matchWidthOrHeight = 0.5f;
        GameObject panel = new GameObject("Navigation Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvas.transform, false);
        RectTransform rect = (RectTransform)panel.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -16f);
        rect.sizeDelta = new Vector2(-32f, 200f);
        panel.GetComponent<Image>().color = new Color(0.025f, 0.04f, 0.065f, 0.93f);
        panel.GetComponent<Image>().raycastTarget = false;
        GameObject graphic = new GameObject("Route - Green Start Red Destination", typeof(RectTransform), typeof(NavigationUI));
        graphic.transform.SetParent(panel.transform, false);
        RectTransform graphicRect = (RectTransform)graphic.transform;
        graphicRect.anchorMin = Vector2.zero;
        graphicRect.anchorMax = Vector2.one;
        graphicRect.offsetMin = new Vector2(8f, 8f);
        graphicRect.offsetMax = new Vector2(-8f, -8f);
        NavigationUI ui = graphic.GetComponent<NavigationUI>();
        ui.color = new Color(0.2f, 0.85f, 1f);
        ui.Bind(route);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        Selection.activeGameObject = map.gameObject;
    }
}
