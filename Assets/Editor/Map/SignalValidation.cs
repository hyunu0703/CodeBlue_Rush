using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>실제 교차로 Prefab 반영과 랜덤 도시 신호의 Play 검증을 제공한다</summary>
public static class SignalValidation
{
    private const string Key = "CodeBlueRush.SignalValidation";
    private static IEnumerator run;
    private static int frame;
    private static int checks;
    private static bool errors;
    private static double deadline;
    private static double startAt;

    // 기존 교차로의 Lane과 도로 연결을 보존하며 실제 신호 참조를 저장한다
    private static void AddSignals(string path)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            RoadChunk road = root.GetComponent<RoadChunk>();
            if (root.GetComponent<TrafficSignalController>())
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return;
            }
            TrafficSignalController signal = root.AddComponent<TrafficSignalController>();
            Reference(signal, "road", road);
            var zones = new List<VehicleStopZone>();
            for (int i = 0; i < road.ConnectionCount; i++)
            {
                SerializedProperty incoming = new SerializedObject(road.GetConnection(i)).FindProperty("incoming");
                TrafficLane lane = incoming.GetArrayElementAtIndex(0).objectReferenceValue as TrafficLane;
                lane.TrySample(4f, out Vector3 line, out Vector3 forward);
                GameObject node = new GameObject("VehicleStopZone_" + i, typeof(VehicleStopZone));
                node.transform.SetParent(root.transform, false);
                node.transform.SetPositionAndRotation(line, Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.up, forward)));
                VehicleStopZone zone = node.GetComponent<VehicleStopZone>();
                Reference(zone, "signal", signal);
                Reference(zone, "lane", lane);
                Marker(node.transform, "StopLine", Vector2.zero, new Vector2(1.9f, 0.15f), Color.white);
                SpriteRenderer light = Marker(node.transform, "TrafficLight", new Vector2(1.7f, -0.5f), Vector2.one * 0.65f, Color.red);
                Reference(zone, "signalLamp", light);
                Reference(zone, "crosswalkStart", Point(node.transform, "CrosswalkStart", new Vector2(-3f, 1f)));
                Reference(zone, "crosswalkEnd", Point(node.transform, "CrosswalkEnd", new Vector2(3f, 1f)));
                Reference(zone, "pedestrianWaitPointA", Point(node.transform, "PedestrianWaitPointA", new Vector2(-3.5f, 1f)));
                Reference(zone, "pedestrianWaitPointB", Point(node.transform, "PedestrianWaitPointB", new Vector2(3.5f, 1f)));
                for (int stripe = 0; stripe < 7; stripe++)
                    Marker(node.transform, "CrosswalkStripe", new Vector2(-3f + stripe, 1f), new Vector2(0.4f, 0.7f), new Color(0.7f, 0.7f, 0.7f));
                zones.Add(zone);
            }
            RoadSamples.SetArray(signal, "zones", zones.ToArray());
            if (!road.Validate(out string error))
                throw new InvalidOperationException(error);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // 횡단보도 끝점과 대기 지점을 로컬 위치로 저장한다
    private static Transform Point(Transform parent, string name, Vector2 point)
    {
        Transform child = new GameObject(name).transform;
        child.SetParent(parent, false);
        child.localPosition = point;
        return child;
    }

    // 기존 내장 스프라이트를 실제 미터 크기로 표시한다
    private static SpriteRenderer Marker(Transform parent, string name, Vector2 offset, Vector2 size, Color color)
    {
        Transform child = Point(parent, name, offset);
        SpriteRenderer renderer = child.gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        child.localScale = new Vector3(size.x / renderer.sprite.bounds.size.x, size.y / renderer.sprite.bounds.size.y, 1f);
        renderer.color = color;
        renderer.sortingOrder = 3;
        return renderer;
    }

    // Inspector 참조를 직렬화하여 Prefab에서도 유지한다
    private static void Reference(Object target, string field, Object value)
    {
        SerializedObject data = new SerializedObject(target);
        data.FindProperty(field).objectReferenceValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    // 실제 생성에 사용되는 두 교차로와 10단계 도로를 함께 연결한다
    [MenuItem("CodeBlue Rush/Signals/Create Demo Scene")]
    public static void CreateDemo()
    {
        AddSignals("Assets/Prefabs/City/TJunction.prefab");
        AddSignals("Assets/Prefabs/City/Intersection.prefab");
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/LaneChangeDemo.unity");
        CityMap map = Object.FindFirstObjectByType<CityMap>();
        SerializedObject data = new SerializedObject(map);
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/TrafficSignalDemo.unity");
        AssetDatabase.SaveAssets();
    }

    // 저장된 실제 자산을 Play로 실행한다
    [MenuItem("CodeBlue Rush/Signals/Validate Play")]
    public static void RunPlay()
    {
        CreateDemo();
        startAt = EditorApplication.timeSinceStartup + 3d;
        EditorApplication.update -= Enter;
        EditorApplication.update += Enter;
    }
    // 컴파일과 에디터 초기화가 끝나면 검증을 시작한다
    private static void Enter()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < startAt)
            return;
        EditorApplication.update -= Enter;
        SessionState.SetBool(Key, true);
        Register();
        EditorApplication.EnterPlaymode();
    }

    // 도메인 재로드 설정과 관계없이 검증 상태를 준비한다
    [InitializeOnLoadMethod]
    private static void Register()
    {
        if (!SessionState.GetBool(Key, false))
            return;
        run = null;
        frame = -1;
        checks = 0;
        errors = false;
        deadline = EditorApplication.timeSinceStartup + 180d;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= Observe;
        Application.logMessageReceived += Observe;
    }

    // 실제 플레이 프레임 단위로 검증을 진행한다
    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new TimeoutException("Signal Play validation timeout");
            if (!EditorApplication.isPlaying || Time.frameCount < 2 || frame == Time.frameCount)
                return;
            frame = Time.frameCount;
            if (run == null)
                run = Validate();
            if (run.MoveNext())
                return;
            Check(!errors, "런타임 오류와 NullReference 없음");
            Debug.Log("Signal play validation passed: " + checks + " checks");
            Finish(true);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(false);
        }
    }


    // 시험에 필요한 Inspector 값만 조절한다
    private static void Set(Object target, string field, float value, bool integer = false)
    {
        SerializedObject data = new SerializedObject(target);
        if (integer)
            data.FindProperty(field).intValue = (int)value;
        else
            data.FindProperty(field).floatValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    // 테스트에서만 자연 순환을 멈추고 특정 신호 조건을 고정한다
    private static void Phase(TrafficSignalController signal, int phase)
    {
        signal.StopAllCoroutines();
        typeof(TrafficSignalController).GetMethod("SetPhase", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(signal, new object[] { phase });
    }

    // 실제 화면 밖 생성 조건을 유지한 채 시험 현장을 고른다
    private static void Frame(VehicleStopZone zone, Transform player, Camera camera)
    {
        player.position = zone.Signal.transform.position + Vector3.Cross(zone.Lane.StartDirection, Vector3.forward) * 28f;
        camera.transform.position = player.position + Vector3.back * 10f;
        Physics2D.SyncTransforms();
    }

    // 풀의 이전 시험 값을 제거하고 정상 생성 API로 차량을 배치한다
    private static VehicleAI Spawn(TrafficSpawner spawner, TrafficLane lane, float along, float violation = 0f)
    {
        Check(spawner.TrySpawn(lane, along, out VehicleAI car), "실제 Lane 차량 생성");
        Set(car, "signalViolationChance", violation);
        Set(car, "cruiseSpeed", 5f);
        car.AllowLaneChanges = true;
        return car;
    }

    // 실제 생성 도시의 두 교차로와 정지 재출발 및 안전 위반을 검증한다
    private static IEnumerator Validate()
    {
        TrafficSpawner spawner = Object.FindFirstObjectByType<TrafficSpawner>();
        CityMap map = spawner.Map;
        AmbulanceController player = Object.FindFirstObjectByType<AmbulanceController>();
        player.GetComponent<KeyboardAmbulanceInput>().enabled = false;
        player.enabled = false;
        Rigidbody2D ambulance = player.GetComponent<Rigidbody2D>();
        Camera camera = Camera.main;
        camera.GetComponent<AmbulanceCamera>().enabled = false;
        Set(spawner, "attempts", 0f, true);
        while (spawner.ActiveCount > 0)
            spawner.Release(spawner.GetVehicle(0));
        Time.timeScale = 3f;
        var unique = new HashSet<TrafficSignalController>();
        TrafficSignalController tee = null;
        TrafficSignalController four = null;
        for (int i = 0; i < map.LaneCount; i++)
        {
            TrafficSignalController signal = TrafficSignalController.ForLane(map.GetLane(i));
            if (!signal || !unique.Add(signal))
                continue;
            Check(signal.ZoneCount == 3 || signal.ZoneCount == 4, "생성 교차로 신호 등록");
            if (signal.ZoneCount == 3) tee = signal;
            else four = signal;
            for (int j = 0; j < signal.ZoneCount; j++)
            {
                VehicleStopZone zone = signal.GetZone(j);
                Check(zone && zone.Signal == signal && map.ContainsLane(zone.Lane) && zone.CrosswalkStart && zone.CrosswalkEnd && zone.PedestrianWaitPointA && zone.PedestrianWaitPointB, "실제 Prefab Inspector 및 횡단 참조");
            }
        }
        Check(tee && four, "동일 랜덤 도시에 T자 및 4거리 존재");
        foreach (string name in new[] { "TJunction", "Intersection" })
            for (int rotation = 0; rotation < 4; rotation++)
            {
                GameObject root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/City/" + name + ".prefab"), new Vector3(-500f, -500f), Quaternion.Euler(0f, 0f, rotation * 90f));
                TrafficSignalController signal = root.GetComponent<TrafficSignalController>();
                Check(root.GetComponent<RoadChunk>().Validate(out _), name + " 회전 도로 연결 보존 " + rotation);
                for (int i = 0; i < signal.ZoneCount; i++)
                {
                    VehicleStopZone zone = signal.GetZone(i);
                    zone.Lane.TrySample(4f, out Vector3 point, out Vector3 direction);
                    Check(Vector3.Distance(point, zone.transform.position) < 0.01f && Vector3.Dot(direction, zone.transform.up) > 0.99f && signal.Entry(zone.Lane) == zone, "회전 정지선 방향 및 Lane 일치");
                }
                Object.Destroy(root);
            }
        yield return null;
        foreach (TrafficSignalController signal in new[] { tee, four })
        {
            VehicleStopZone zone = signal.GetZone(0);
            Frame(zone, player.transform, camera);
            Phase(signal, -1);
            VehicleAI lead = Spawn(spawner, zone.Lane, 0.1f);
            float until = Time.time + 4f;
            float peak = 0f;
            while (Time.time < until) { peak = Mathf.Max(peak, lead.Speed); yield return null; }
            Check(peak > 1f && lead.Lane == zone.Lane && lead.Distance <= zone.StopDistance + 0.01f && lead.Speed < 0.1f, signal.ZoneCount + "방향 Red 실제 주행 후 정지선 앞 정차");
            TrafficLane upstream = null;
            for (int i = 0; i < map.LaneCount; i++)
            {
                TrafficLane lane = map.GetLane(i);
                for (int j = 0; j < lane.NextCount; j++)
                    if (lane.GetNext(j) == zone.Lane) upstream = lane;
            }
            Check(upstream, "직접 연결된 상류 차선");
            VehicleAI follower = Spawn(spawner, upstream, upstream.Length - 0.1f);
            until = Time.time + 3f;
            while (Time.time < until) yield return null;
            Check(follower.Speed < 0.1f && Vector2.Distance(follower.transform.position, lead.transform.position) > 1.8f, "적색 앞차 뒤 안전 대기열");
            Capture(camera, signal.transform.position, "red-" + signal.ZoneCount);
            Set(lead, "cruiseSpeed", 0f);
            Phase(signal, 0);
            until = Time.time + 1f;
            while (Time.time < until) yield return null;
            Check(follower.Speed < 0.1f, "Green이어도 정지 앞차 간격 유지");
            Set(lead, "cruiseSpeed", 5f);
            until = Time.time + 2.5f;
            while (Time.time < until) yield return null;
            Check(lead.Lane != zone.Lane && lead.Speed > 0f && follower.Speed > 0f, "Green 실제 재출발 및 기존 Next Lane 통과");
            Check(signal.OccupantCount >= 2, "같은 방향 복수 차량 동시 진입");
            Capture(camera, signal.transform.position, "green-" + signal.ZoneCount);
            spawner.Release(lead);
            spawner.Release(follower);
            Check(signal.OccupantCount == 0, "풀 반환 시 점유 제거");

            Phase(signal, -1);
            Frame(zone, player.transform, camera);
            VehicleAI violator = Spawn(spawner, zone.Lane, 0.1f, 1f);
            ambulance.position = signal.transform.position;
            Physics2D.SyncTransforms();
            until = Time.time + 3f;
            while (Time.time < until) yield return null;
            Check(violator.Lane == zone.Lane && violator.Distance <= zone.StopDistance + 0.01f && violator.Speed < 0.1f, "위반 결정 후에도 교차로 내부 구급차 위험 차단");
            Set(violator, "signalViolationChance", 0f);
            Frame(zone, player.transform, camera);
            until = Time.time + 3f;
            while (Time.time < until) yield return null;
            Check(!zone.IsGreen && violator.Lane != zone.Lane && violator.Speed > 0f, "접근 시 한 번 결정한 위반은 안전 확보 후 적색 통과");
            spawner.Release(violator);
            Check(!spawner.TrySpawn(zone.Lane, zone.StopDistance + 1f, out _) && !spawner.TrySpawn(zone.Lane.GetNext(0), 1f, out _), "정지선 뒤 및 교차로 내부 Spawn 우회 차단");
        }
        // 이전 방향의 차량이 남아 있으면 새 Green과 위반 차량도 충돌 경로를 예약하지 못한다
        VehicleStopZone first = four.GetZone(0);
        VehicleStopZone second = four.GetZone(1);
        Frame(first, player.transform, camera);
        Phase(four, 0);
        VehicleAI occupied = Spawn(spawner, first.Lane, 0.1f);
        Straight(occupied, first.Lane);
        float timeout = Time.time + 4f;
        while (occupied.Lane == first.Lane && occupied.Distance < 4.4f && Time.time < timeout) yield return null;
        Set(occupied, "cruiseSpeed", 0f);
        Check(four.OccupantCount == 1, "통과 전 예약된 실제 내부 차량");
        Frame(second, player.transform, camera);
        VehicleAI conflict = Spawn(spawner, second.Lane, 0.1f, 1f);
        Straight(conflict, second.Lane);
        Phase(four, 1);
        timeout = Time.time + 3f;
        while (Time.time < timeout) yield return null;
        Check(second.IsGreen && conflict.Lane == second.Lane && conflict.Speed < 0.1f && conflict.Distance <= second.StopDistance + 0.01f, "다른 방향 잔여 점유가 있으면 새 Green도 안전 대기");
        Phase(four, -1);
        timeout = Time.time + 1f;
        while (Time.time < timeout) yield return null;
        Check(conflict.Lane == second.Lane && conflict.Speed < 0.1f, "충돌 경로 점유 중에는 위반 차량도 진입 금지");
        spawner.Release(occupied);
        timeout = Time.time + 3f;
        while (Time.time < timeout) yield return null;
        Check(conflict.Lane != second.Lane && conflict.Speed > 0f, "점유 해제 후 위반 차량 안전 통과");
        spawner.Release(conflict);

        // 자연 순환은 실제 Coroutine을 재시작해 모든 방향 및 전적색을 관찰한다
        Set(four, "greenDuration", 0.4f);
        Set(four, "changeDelay", 0.2f);
        four.enabled = false;
        four.enabled = true;
        var phases = new HashSet<int>();
        float end = Time.time + 3f;
        while (Time.time < end)
        {
            int green = 0;
            int active = -1;
            for (int i = 0; i < four.ZoneCount; i++)
                if (four.GetZone(i).IsGreen) { green++; active = i; }
            if (green > 1) throw new InvalidOperationException("동시 충돌 Green");
            phases.Add(active);
            yield return null;
        }
        Check(phases.Count == 5, "한 루틴에서 네 방향 및 전적색 순환, 동시 충돌 Green 없음");
        TrafficLane old = four.GetZone(0).Lane;
        Check(FixedMapScene.ReloadFixture(map, out _), "새 도시 생성 유지");
        yield return null;
        Check(!TrafficSignalController.ForLane(old) && spawner.ActiveCount == 0, "이전 도시 신호 및 차량 점유 참조 정리");
        spawner.enabled = false;
    }

    // 시험에서만 기존 분기 선택값을 직진 내부 Lane으로 고정한다
    private static void Straight(VehicleAI car, TrafficLane entry)
    {
        for (int i = 0; i < entry.NextCount; i++)
            if (Vector3.Dot(entry.StartDirection, entry.GetNext(i).EndDirection) > 0.99f)
            {
                typeof(VehicleAI).GetField("choice", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(car, i);
                return;
            }
        throw new InvalidOperationException("직진 경로 없음");
    }

    // 실제 Play 화면을 480×854로 보존한다
    private static void Capture(Camera camera, Vector3 center, string name)
    {
        Vector3 before = camera.transform.position;
        float size = camera.orthographicSize;
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        bool shown = canvas && canvas.enabled;
        RenderTexture target = new RenderTexture(480, 854, 24);
        RenderTexture old = RenderTexture.active;
        RenderTexture oldTarget = camera.targetTexture;
        Texture2D image = new Texture2D(480, 854, TextureFormat.RGB24, false);
        try
        {
            if (canvas) canvas.enabled = false;
            camera.transform.position = center + Vector3.back * 10f;
            camera.orthographicSize = 21f;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 480, 854), 0, 0);
            image.Apply();
            System.IO.File.WriteAllBytes("Logs/signal-" + name + ".png", image.EncodeToPNG());
        }
        finally
        {
            if (canvas) canvas.enabled = shown;
            camera.transform.position = before;
            camera.orthographicSize = size;
            camera.targetTexture = oldTarget;
            RenderTexture.active = old;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
        }
    }

    // 실패 조건을 즉시 보고한다
    private static void Check(bool condition, string label)
    {
        if (!condition)
            throw new InvalidOperationException("Signal validation failed: " + label);
        checks++;
        Debug.Log("Signal check passed: " + label);
    }

    // 예상하지 않은 런타임 오류를 기록한다
    private static void Observe(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors = true;
    }

    // 검증 설정과 구독을 정리한다
    private static void Finish(bool success)
    {
        Time.timeScale = 1f;
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= Observe;
        run = null;
        if (Application.isBatchMode)
            EditorApplication.Exit(success ? 0 : 1);
        else
            EditorApplication.ExitPlaymode();
    }
}


