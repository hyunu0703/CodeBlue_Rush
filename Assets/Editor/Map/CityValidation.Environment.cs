using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>기존 도시 검증에 환경 자산 작성과 데이터 및 미션 연동 검증을 추가한다</summary>
public static partial class CityValidation
{
    private const string EnvironmentFolder = "Assets/Prefabs/Environment";
    private static IEnumerator environmentRun;
    private static int environmentFrame;
    private static float environmentResume;
    private static double environmentDeadline;
    private static bool environmentErrors;
    private const string EnvironmentKey = "CodeBlueRush.EnvironmentValidation";
    private const string CornerCameraKey = "CodeBlueRush.CornerCameraValidation";
    private static double environmentStart;
    private static Type existingValidation;

    // 기존 검증의 Scene 생성 단계를 건너뛰고 저장된 Scene에서 Play만 실행한다
    public static void RunExistingPlay()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-validationType");
        if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("validationType 필요");
        existingValidation = typeof(CityValidation).Assembly.GetType(args[index + 1], true);
        string scene = args[index + 1].Replace("Validation", "Demo");
        if (scene == "SignalDemo") scene = "TrafficSignalDemo";
        EditorSceneManager.OpenScene("Assets/Scenes/" + scene + ".unity");
        environmentStart = EditorApplication.timeSinceStartup + 3d;
        EditorApplication.update += EnterExisting;
    }

    // 기존 검증의 초기화와 도메인 재로드 구독만 재사용한다
    private static void EnterExisting()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < environmentStart) return;
        EditorApplication.update -= EnterExisting;
        FieldInfo key = existingValidation.GetField("Key", BindingFlags.Static | BindingFlags.NonPublic) ?? existingValidation.GetField("PlayKey", BindingFlags.Static | BindingFlags.NonPublic);
        SessionState.SetBool((string)key.GetRawConstantValue(), true);
        existingValidation.GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        EditorApplication.EnterPlaymode();
    }

    // Scene을 저장하지 않고 카메라 및 표시 이벤트 수명을 Play에서 검사한다
    public static void RunEnvironmentPlay()
    {
        SessionState.SetBool(CornerCameraKey, false);
        EditorSceneManager.OpenScene("Assets/Scenes/HospitalDemo.unity");
        environmentStart = EditorApplication.timeSinceStartup + 3d;
        EditorApplication.update += EnterEnvironment;
    }

    // 기존 Scene을 저장하지 않고 코너 카메라의 물리 Trigger 동작을 검사한다
    public static void RunCornerCameraPlay()
    {
        SessionState.SetBool(CornerCameraKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/HospitalDemo.unity");
        environmentStart = EditorApplication.timeSinceStartup + 3d;
        EditorApplication.update += EnterEnvironment;
    }

    // 에디터 초기화 완료 후 검증을 등록한다
    private static void EnterEnvironment()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < environmentStart) return;
        EditorApplication.update -= EnterEnvironment;
        SessionState.SetBool(EnvironmentKey, true);
        RegisterEnvironment();
        EditorApplication.EnterPlaymode();
    }

    // 도메인 재로드 설정에 관계없이 검증을 중복 없이 등록한다
    [InitializeOnLoadMethod]
    private static void RegisterEnvironment()
    {
        if (!SessionState.GetBool(EnvironmentKey, false)) return;
        environmentRun = null;
        environmentFrame = -1;
        environmentResume = 0f;
        environmentErrors = false;
        environmentDeadline = EditorApplication.timeSinceStartup + 180d;
        EditorApplication.update -= TickEnvironment;
        EditorApplication.update += TickEnvironment;
        Application.logMessageReceived -= EnvironmentLog;
        Application.logMessageReceived += EnvironmentLog;
    }

    // 런타임 예외를 검증 실패에 반영한다
    private static void EnvironmentLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) environmentErrors = true;
    }

    // 실제 프레임 단위로 테스트를 진행하고 완료 시 배치 종료한다
    private static void TickEnvironment()
    {
        bool success = false;
        try
        {
            if (EditorApplication.timeSinceStartup > environmentDeadline) throw new TimeoutException();
            if (!EditorApplication.isPlaying || Time.frameCount < 2 || environmentFrame == Time.frameCount || Time.time < environmentResume) return;
            environmentFrame = Time.frameCount;
            if (environmentRun == null) environmentRun = SessionState.GetBool(CornerCameraKey, false) ? ValidateCornerCameraPlay() : ValidateEnvironmentPlay();
            if (environmentRun.MoveNext())
            {
                if (environmentRun.Current is float delay) environmentResume = Time.time + delay;
                return;
            }
            Check(!environmentErrors, "환경 Play 런타임 예외 없음");
            success = true;
            Debug.Log((SessionState.GetBool(CornerCameraKey, false) ? "Corner camera" : "Environment") + " play validation passed: " + checks + " checks");
        }
        catch (Exception error) { Debug.LogException(error); }
        SessionState.SetBool(EnvironmentKey, false);
        SessionState.SetBool(CornerCameraKey, false);
        EditorApplication.update -= TickEnvironment;
        Application.logMessageReceived -= EnvironmentLog;
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
        else EditorApplication.ExitPlaymode();
    }

    // 기존 원호의 방향 전환 구간만 단일 Polygon Trigger로 작성한다
    [MenuItem("CodeBlue Rush/City/Create Corner Camera Zone")]
    public static void CreateCornerCameraZone()
    {
        const string path = "Assets/Prefabs/City/Corner.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (root.GetComponentInChildren<CameraTurnZone>(true)) return;
            GameObject node = new GameObject("CameraTurnZone", typeof(PolygonCollider2D), typeof(CameraTurnZone));
            node.transform.SetParent(root.transform, false);
            PolygonCollider2D area = node.GetComponent<PolygonCollider2D>();
            area.isTrigger = true;
            const int count = 17;
            Vector2[] outline = new Vector2[count * 2];
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.Lerp(100f, 170f, i / (float)(count - 1)) * Mathf.Deg2Rad;
                Vector2 radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                outline[i] = new Vector2(10f, -10f) + radial * 12.7f;
                outline[outline.Length - 1 - i] = new Vector2(10f, -10f) + radial * 7.3f;
            }
            area.points = outline;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // 네 방향 양방향 Lane에서 실제 구급차 Collider와 카메라 보간을 확인한다
    private static IEnumerator ValidateCornerCameraPlay()
    {
        checks = 0;
        CityMap map = Object.FindFirstObjectByType<CityMap>();
        AmbulanceCamera camera = Object.FindFirstObjectByType<AmbulanceCamera>();
        AmbulanceController vehicle = Object.FindFirstObjectByType<AmbulanceController>();
        vehicle.GetComponent<KeyboardAmbulanceInput>().enabled = false;
        vehicle.enabled = false;
        Rigidbody2D body = vehicle.GetComponent<Rigidbody2D>();
        FieldInfo active = typeof(AmbulanceCamera).GetField("activeZone", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo targetAngle = typeof(AmbulanceCamera).GetField("targetAngle", BindingFlags.Instance | BindingFlags.NonPublic);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/City/Corner.prefab");
        CameraTurnZone generated = null;
        CameraTurnZone adjacent = null;
        CameraTurnZone junction = null;
        foreach (CameraTurnZone zone in map.GetComponentsInChildren<CameraTurnZone>())
        {
            Check(new SerializedObject(zone).FindProperty("cameraController").objectReferenceValue == camera, "생성된 회전 도로 카메라 Bind");
            RoadChunk road = zone.GetComponentInParent<RoadChunk>();
            if (road.ConnectionCount == 2)
            {
                adjacent = generated;
                generated = zone;
            }
            else junction = zone;
        }
        Check(generated && junction, "랜덤 도시 Corner 및 교차로 Zone 존재");
        foreach (string name in new[] { "Straight", "PassingStraight" })
            Check(!AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/City/" + name + ".prefab").GetComponentInChildren<CameraTurnZone>(true), "직선 도로에는 Zone 없음");
        for (int rotation = 0; rotation < 360; rotation += 90)
        {
            GameObject root = Object.Instantiate(prefab, new Vector3(1000f + rotation, 1000f), Quaternion.Euler(0f, 0f, rotation));
            RoadChunk road = root.GetComponent<RoadChunk>();
            CameraTurnZone zone = root.GetComponentInChildren<CameraTurnZone>();
            zone.Bind(camera);
            Check(root.GetComponentsInChildren<CameraTurnZone>().Length == 1 && zone.GetComponent<PolygonCollider2D>().isTrigger, "단일 곡선 Trigger 구성");
            for (int i = 0; i < road.LaneCount; i++)
            {
                TrafficLane lane = road.GetLane(i);
                lane.TrySample(0f, out Vector3 start, out Vector3 startDirection);
                body.position = start;
                body.rotation = Vector2.SignedAngle(Vector2.up, startDirection);
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                Physics2D.SyncTransforms();
                yield return 0.12f;
                Check(!(CameraTurnZone)active.GetValue(camera), "회전 전 Lane 시작점에서는 고정 유지");
                lane.TrySample(lane.Length * 0.25f, out Vector3 inside, out Vector3 direction);
                body.position = inside;
                body.rotation = Vector2.SignedAngle(Vector2.up, direction);
                Physics2D.SyncTransforms();
                yield return 0.12f;
                Check((CameraTurnZone)active.GetValue(camera) == zone, "회전된 Corner 실제 Trigger 진입 " + rotation + "/" + i);
                yield return 0.8f;
                Check(Mathf.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.z, body.rotation)) < 3f, "구급차 방향 추적");
                lane.TrySample(lane.Length * 0.5f, out inside, out direction);
                float before = camera.transform.eulerAngles.z;
                body.position = inside;
                body.rotation = Vector2.SignedAngle(Vector2.up, direction);
                Physics2D.SyncTransforms();
                yield return 0.06f;
                float remaining = Mathf.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.z, body.rotation));
                Check(remaining > 0.5f && remaining < Mathf.Abs(Mathf.DeltaAngle(before, body.rotation)), "순간 회전 없이 SmoothDamp 추적");
                yield return 0.8f;
                Check(Mathf.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.z, body.rotation)) < 3f, "곡선 내부 방향 변화 추적");
                lane.TrySample(lane.Length, out Vector3 end, out direction);
                body.position = end;
                body.rotation = Vector2.SignedAngle(Vector2.up, direction);
                Physics2D.SyncTransforms();
                yield return 0.12f;
                float locked = Mathf.Round(body.rotation / 90f) * 90f;
                Check(!(CameraTurnZone)active.GetValue(camera) && Mathf.Abs(Mathf.DeltaAngle((float)targetAngle.GetValue(camera), locked)) < 0.1f, "Corner 이탈 후 가장 가까운 90도 고정");
                body.rotation += 25f;
                yield return 0.8f;
                Check(Mathf.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.z, locked)) < 3f, "회전 구간 밖에서는 차량 회전을 추적하지 않음");
            }
            zone.Bind(null);
            body.position = zone.transform.TransformPoint(new Vector3(2.93f, -2.93f));
            Physics2D.SyncTransforms();
            yield return 0.12f;
            Check(!(CameraTurnZone)active.GetValue(camera), "카메라 미연결 실제 Trigger 안전 무시");
            Object.Destroy(root);
            yield return null;
        }
        foreach (CameraTurnZone zone in map.GetComponentsInChildren<CameraTurnZone>())
        {
            RoadChunk road = zone.GetComponentInParent<RoadChunk>();
            if (road.ConnectionCount < 3) continue;
            body.position = zone.transform.position;
            body.rotation = 35f;
            Physics2D.SyncTransforms();
            yield return 0.12f;
            Check((CameraTurnZone)active.GetValue(camera) == zone, "기존 T 및 4거리 실제 Trigger 진입 유지");
            body.position = new Vector2(2000f, 2000f);
            Physics2D.SyncTransforms();
            yield return 0.12f;
            Check(!(CameraTurnZone)active.GetValue(camera), "기존 교차로 실제 Trigger 이탈 유지");
        }
        foreach (bool reverse in new[] { false, true })
        {
            CameraTurnZone first = reverse ? junction : generated;
            CameraTurnZone second = reverse ? generated : junction;
            camera.EnterTurnZone(first);
            camera.EnterTurnZone(second);
            camera.ExitTurnZone(first);
            Check((CameraTurnZone)active.GetValue(camera) == second, "Corner 교차로 연속 진입에서 이전 Exit 무시");
            camera.ExitTurnZone(second);
        }
        Check(adjacent, "연속 Corner 시험 영역 확보");
        camera.EnterTurnZone(generated);
        camera.EnterTurnZone(adjacent);
        camera.ExitTurnZone(generated);
        Check((CameraTurnZone)active.GetValue(camera) == adjacent, "연속 Corner에서 이전 Exit 무시");
        camera.ExitTurnZone(adjacent);
        camera.EnterTurnZone(generated);
        Check(FixedMapScene.ReloadFixture(map, out _), "고정 World 검증용 재로드");
        yield return null;
        Check(!generated && !(CameraTurnZone)active.GetValue(camera), "이전 도시 활성 Corner 참조 정리");
        foreach (CameraTurnZone zone in map.GetComponentsInChildren<CameraTurnZone>())
            Check(new SerializedObject(zone).FindProperty("cameraController").objectReferenceValue == camera, "새 도시 카메라 Bind 유지");
    }

    // 기존 미션 데이터를 표시하고 생성 교차로의 카메라 참조를 확인한다
    private static IEnumerator ValidateEnvironmentPlay()
    {
        checks = 0;
        PatientReport report = Object.FindFirstObjectByType<PatientReport>();
        CityMap map = Object.FindFirstObjectByType<CityMap>();
        AmbulanceCamera camera = Object.FindFirstObjectByType<AmbulanceCamera>();
        AmbulanceController vehicle = Object.FindFirstObjectByType<AmbulanceController>();
        vehicle.enabled = false;
        vehicle.GetComponent<KeyboardAmbulanceInput>().enabled = false;
        CameraTurnZone[] zones = map.GetComponentsInChildren<CameraTurnZone>();
        Check(zones.Length > 0, "실제 생성 교차로 CameraTurnZone 존재");
        foreach (CameraTurnZone zone in zones)
            Check(new SerializedObject(zone).FindProperty("cameraController").objectReferenceValue == camera, "생성 시 명시적 카메라 연결");
        CameraTurnZone first = zones[0];
        Collider2D body = vehicle.GetComponent<Collider2D>();
        MethodInfo enter = typeof(CameraTurnZone).GetMethod("OnTriggerEnter2D", BindingFlags.Instance | BindingFlags.NonPublic);
        first.Bind(null);
        enter.Invoke(first, new object[] { body });
        Check(true, "카메라 미연결 Trigger NRE 없음");
        first.Bind(camera);
        Rigidbody2D ambulanceBody = vehicle.GetComponent<Rigidbody2D>();
        ambulanceBody.position = first.transform.position;
        ambulanceBody.linearVelocity = Vector2.zero;
        Physics2D.SyncTransforms();
        float wait = Time.time + 0.15f;
        while (Time.time < wait) yield return null;
        Check((CameraTurnZone)typeof(AmbulanceCamera).GetField("activeZone", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(camera) == first, "기존 카메라 회전 연결");
        GameObject ui = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PatientReportUI.prefab"));
        ui.transform.SetParent(Object.FindFirstObjectByType<Canvas>().transform, false);
        PatientReportUI display = ui.GetComponent<PatientReportUI>();
        Text label = ui.GetComponent<Text>();
        display.Bind(report);
        display.Bind(report);
        report.CancelReport();
        PatientReportValidation.PlaceSource(map, vehicle.transform, 0.2f);
        Check(report.TryReport(out _) && label.text == report.Message, "ReportChanged 메시지 표시");
        display.Bind(null);
        Check(label.text == string.Empty, "UI 미연결 보고 안전 처리");
        display.Bind(report);
        CaptureEnvironment(map, ui);
        FieldInfo changed = typeof(PatientReport).GetField("ReportChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        int count = ((Delegate)changed.GetValue(report)).GetInvocationList().Length;
        display.enabled = false;
        Check(((Delegate)changed.GetValue(report)).GetInvocationList().Length == count - 1, "UI 이벤트 해제");
        display.enabled = true;
        Check(((Delegate)changed.GetValue(report)).GetInvocationList().Length == count && label.text == report.Message, "UI 중복 구독 없음 및 즉시 갱신");
        EnvironmentSlot old = report.Patient.Slot;
        old.enabled = false;
        Check(!report.Patient.TryGetPose(out _, out _), "비활성 사고 Slot 거절");
        old.enabled = true;
        camera.EnterTurnZone(first);
        Check(FixedMapScene.ReloadFixture(map, out _), "새 도시 교체");
        yield return null;
        Check(!old && !first && !report.IsActive && label.text == string.Empty, "이전 Slot Zone 미션 UI 참조 정리");
        Check(!((CameraTurnZone)typeof(AmbulanceCamera).GetField("activeZone", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(camera)), "이전 카메라 영역 해제");
        Object.Destroy(ui);
    }

    // 실제 환경 배치와 UI 메시지를 세로 화면으로 렌더링한다
    private static void CaptureEnvironment(CityMap map, GameObject ui)
    {
        Camera camera = Camera.main;
        GameObject canvasObject = new GameObject("Environment UI Capture", typeof(Canvas));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        ui.transform.SetParent(canvas.transform, false);
        camera.GetComponent<AmbulanceCamera>().enabled = false;
        camera.transform.position = map.GetIncidentSlot(0).Road.transform.position + Vector3.back * 10f;
        camera.transform.rotation = Quaternion.identity;
        camera.orthographicSize = 22f;
        Canvas.ForceUpdateCanvases();
        RenderTexture target = new RenderTexture(480, 854, 24);
        Texture2D image = new Texture2D(480, 854, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 480, 854), 0, 0);
            image.Apply();
            System.IO.File.WriteAllBytes("Logs/environment-ui.png", image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            Object.Destroy(image);
            Object.Destroy(target);
            camera.GetComponent<AmbulanceCamera>().enabled = true;
        }
    }

    // Scene 저장 없이 명시적 배치 위치와 Placeholder 및 UI Prefab만 작성한다
    [MenuItem("CodeBlue Rush/City/Create Environment Prefabs")]
    public static void CreateEnvironmentPrefabs()
    {
        if (!AssetDatabase.IsValidFolder(EnvironmentFolder))
            AssetDatabase.CreateFolder("Assets/Prefabs", "Environment");
        GameObject small = Placeholder("Building_Small", new Vector2(2.5f, 3f), new Color(0.28f, 0.42f, 0.58f));
        GameObject medium = Placeholder("Building_Medium", new Vector2(3f, 4f), new Color(0.5f, 0.4f, 0.3f));
        GameObject tree = Placeholder("Tree", Vector2.one * 0.9f, new Color(0.12f, 0.48f, 0.2f));
        GameObject pole = Placeholder("Pole", new Vector2(0.25f, 0.7f), Color.gray);
        GameObject sidewalk = Placeholder("Sidewalk", Vector2.one, new Color(0.5f, 0.5f, 0.48f));
        foreach (string name in new[] { "Straight", "PassingStraight", "Corner", "TJunction", "Intersection" })
        {
            string path = "Assets/Prefabs/City/" + name + ".prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                RoadChunk road = root.GetComponent<RoadChunk>();
                if (!root.transform.Find("Environment"))
                {
                    Transform environment = new GameObject("Environment").transform;
                    environment.SetParent(root.transform, false);
                    if (name == "Corner")
                        CornerWalk(road, environment, sidewalk);
                    else
                        PortWalk(road, environment, sidewalk);
                    if (name == "Straight" || name == "PassingStraight")
                    {
                        for (int side = -1; side <= 1; side += 2)
                        {
                            Slot(environment, road, EnvironmentSlot.Usage.Building, new Vector2(side * 8f, -5f), null, 0f, small, medium);
                            Slot(environment, road, EnvironmentSlot.Usage.Building, new Vector2(side * 8f, 5f), null, 0f, small, medium);
                            Slot(environment, road, EnvironmentSlot.Usage.Decoration, new Vector2(side * 8f, 0f), null, 0f, tree, pole);
                            TrafficLane lane = null;
                            for (int i = 0; i < road.LaneCount; i++)
                            {
                                TrafficLane candidate = road.GetLane(i);
                                candidate.TrySample(candidate.Length / 2f, out Vector3 middle, out _);
                                if (candidate.Length >= 10f && Mathf.Sign(middle.x) == side && (!lane || Mathf.Abs(middle.x) > Mathf.Abs(lane.StartPoint.x)))
                                    lane = candidate;
                            }
                            if (!lane) throw new InvalidOperationException("도로운전 접근 Lane 누락");
                            lane.TrySample(lane.Length / 2f, out Vector3 access, out _);
                            lane.TryProject(new Vector3(access.x, -3f), out float hospitalAlong, out _);
                            lane.TryProject(new Vector3(access.x, 3f), out float incidentAlong, out _);
                            Slot(environment, road, EnvironmentSlot.Usage.Hospital, new Vector2(access.x + side * 1.8f, -3f), lane, hospitalAlong);
                            Slot(environment, road, EnvironmentSlot.Usage.Incident, new Vector2(access.x + side * 1.8f, 3f), lane, incidentAlong);
                        }
                    }
                    else if (name == "Corner")
                        Slot(environment, road, EnvironmentSlot.Usage.Decoration, new Vector2(-8f, 8f), null, 0f, tree, pole);
                    else
                        for (int x = -1; x <= 1; x += 2)
                            for (int y = -1; y <= 1; y += 2)
                                Slot(environment, road, EnvironmentSlot.Usage.Building, new Vector2(x * 8f, y * 8f), null, 0f, small, medium);
                }
                if (road.ConnectionCount >= 3 && !root.GetComponentInChildren<CameraTurnZone>())
                {
                    GameObject zone = new GameObject("CameraTurnZone", typeof(BoxCollider2D), typeof(CameraTurnZone));
                    zone.transform.SetParent(root.transform, false);
                    BoxCollider2D area = zone.GetComponent<BoxCollider2D>();
                    area.isTrigger = true;
                    area.size = Vector2.one * 12f;
                }
                if (!road.Validate(out string error)) throw new InvalidOperationException(error);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        CreateReportUI();
        AssetDatabase.SaveAssets();
    }

    // 중심 Pivot과 실제 월드 크기를 가진 교체 가능한 시각 Prefab을 작성한다
    private static GameObject Placeholder(string name, Vector2 size, Color color)
    {
        string path = EnvironmentFolder + "/" + name + ".prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing) return existing;
        GameObject root = new GameObject(name);
        GameObject visual = new GameObject("Visual", typeof(SpriteRenderer));
        visual.transform.SetParent(root.transform, false);
        SpriteRenderer renderer = visual.GetComponent<SpriteRenderer>();
        renderer.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        renderer.color = color;
        renderer.sortingOrder = name == "Sidewalk" ? -9 : -2;
        visual.transform.localScale = new Vector3(size.x / renderer.sprite.bounds.size.x, size.y / renderer.sprite.bounds.size.y, 1f);
        GameObject result = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return result;
    }

    // 직렬화된 Object 참조를 설정한다
    private static void EnvReference(Object target, string field, Object value)
    {
        SerializedObject data = new SerializedObject(target);
        data.FindProperty(field).objectReferenceValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    // 도로 옆 배치 영역과 접근 차선을 명시적으로 작성한다
    private static void Slot(Transform parent, RoadChunk road, EnvironmentSlot.Usage use, Vector2 point, TrafficLane lane, float distance, params GameObject[] variants)
    {
        GameObject node = new GameObject(use.ToString(), typeof(EnvironmentSlot));
        node.transform.SetParent(parent, false);
        node.transform.localPosition = point;
        EnvironmentSlot slot = node.GetComponent<EnvironmentSlot>();
        EnvReference(slot, "road", road);
        EnvReference(slot, "lane", lane);
        SerializedObject data = new SerializedObject(slot);
        data.FindProperty("usage").intValue = (int)use;
        data.FindProperty("distance").floatValue = distance;
        data.ApplyModifiedPropertiesWithoutUndo();
        RoadSamples.SetArray(slot, "variants", variants);
    }

    // 보도 Point를 저장하고 각 구간에 보도 Placeholder를 배치한다
    private static SidewalkPath Walk(RoadChunk road, Transform parent, GameObject prefab, params Vector2[] points)
    {
        GameObject node = new GameObject("SidewalkPath", typeof(SidewalkPath));
        node.transform.SetParent(parent, false);
        SidewalkPath path = node.GetComponent<SidewalkPath>();
        EnvReference(path, "road", road);
        SerializedObject data = new SerializedObject(path);
        SerializedProperty array = data.FindProperty("points");
        array.arraySize = points.Length;
        for (int i = 0; i < points.Length; i++) array.GetArrayElementAtIndex(i).vector2Value = points[i];
        data.ApplyModifiedPropertiesWithoutUndo();
        for (int i = 1; i < points.Length; i++)
        {
            GameObject strip = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            strip.transform.SetParent(node.transform, false);
            strip.transform.localPosition = (points[i - 1] + points[i]) / 2f;
            strip.transform.localRotation = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.up, points[i] - points[i - 1]));
            strip.transform.localScale = new Vector3(0.6f, Vector2.Distance(points[i - 1], points[i]), 1f);
        }
        return path;
    }

    // 기존 교차로 포트와 횡단 참조를 보도에 연결한다
    private static void PortWalk(RoadChunk road, Transform parent, GameObject prefab)
    {
        bool[] open = new bool[4];
        TrafficSignalController signal = road.GetComponent<TrafficSignalController>();
        for (int p = 0; p < road.ConnectionCount; p++)
        {
            RoadConnection port = road.GetConnection(p);
            Vector2 axis = road.transform.InverseTransformDirection(port.Outward);
            Vector2 right = new Vector2(axis.y, -axis.x);
            for (int d = 0; d < 4; d++)
                if (Vector2.Dot(axis, CitySamples.Direction(d)) > 0.99f) open[d] = true;
            SidewalkPath a = Walk(road, parent, prefab, axis * 10f - right * 5f, axis * 5f - right * 5f);
            SidewalkPath b = Walk(road, parent, prefab, axis * 10f + right * 5f, axis * 5f + right * 5f);
            if (signal)
                for (int i = 0; i < signal.ZoneCount; i++)
                {
                    VehicleStopZone crossing = signal.GetZone(i);
                    if (!crossing || Vector3.Dot(-crossing.Lane.StartDirection, port.Outward) < 0.99f) continue;
                    EnvReference(a, "crosswalk", crossing);
                    EnvReference(b, "crosswalk", crossing);
                    EnvReference(a, "across", b);
                    EnvReference(b, "across", a);
                }
        }
        for (int d = 0; d < 4; d++)
        {
            if (open[d]) continue;
            Vector2 axis = CitySamples.Direction(d);
            Vector2 right = new Vector2(axis.y, -axis.x);
            Walk(road, parent, prefab, axis * 5f - right * 5f, axis * 5f + right * 5f);
        }
    }

    // 기존 코너의 명시적 원호 기하와 같은 중심에서 도로 밖 보도를 작성한다
    private static void CornerWalk(RoadChunk road, Transform parent, GameObject prefab)
    {
        foreach (float radius in new[] { 5f, 15f })
        {
            Vector2[] points = new Vector2[25];
            for (int i = 0; i < points.Length; i++)
            {
                float angle = (90f + 90f * i / (points.Length - 1)) * Mathf.Deg2Rad;
                points[i] = new Vector2(10f, -10f) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
            Walk(road, parent, prefab, points);
        }
    }

    // 표시 전용 UI를 나중에 Canvas에 배치할 수 있는 Prefab으로 저장한다
    private static void CreateReportUI()
    {
        const string path = "Assets/Prefabs/PatientReportUI.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path)) return;
        GameObject root = new GameObject("PatientReportUI", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(PatientReportUI));
        Text label = root.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 22;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.raycastTarget = false;
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -70f);
        rect.sizeDelta = new Vector2(440f, 90f);
        EnvReference(root.GetComponent<PatientReportUI>(), "label", label);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }
}
