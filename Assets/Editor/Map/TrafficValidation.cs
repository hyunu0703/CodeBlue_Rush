using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>9단계 차량 자산과 실제 차선 주행 및 생성 수명의 Play 검증을 제공한다</summary>
public static class TrafficValidation
{
    private const string Key = "CodeBlueRush.TrafficValidation";
    private static IEnumerator run;
    private static int frame;
    private static int checks;
    private static bool errors;
    private static double deadline;
    private static double startAt;

    // 이전 데모를 보존하면서 차량 Prefab과 주변 생성 책임을 연결한다
    [MenuItem("CodeBlue Rush/Traffic/Create Demo Scene")]
    public static void CreateDemo()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/HospitalDemo.unity");
        const string path = "Assets/Prefabs/TrafficVehicle.prefab";
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(path))
        {
            GameObject root = new GameObject("Traffic Vehicle", typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(VehicleAI));
            Rigidbody2D body = root.GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            CircleCollider2D shape = root.GetComponent<CircleCollider2D>();
            shape.isTrigger = true;
            shape.radius = 0.9f;
            Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Marker(root.transform, sprite, "Body", new Vector2(0.9f, 1.7f), Vector2.zero, new Color(0.95f, 0.65f, 0.15f), 5);
            Marker(root.transform, sprite, "Windshield", new Vector2(0.65f, 0.35f), new Vector2(0f, 0.4f), new Color(0.15f, 0.3f, 0.4f), 6);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }
        CityMap map = Object.FindFirstObjectByType<CityMap>();
        Transform player = Object.FindFirstObjectByType<AmbulanceController>().transform;
        TrafficSpawner spawner = map.gameObject.AddComponent<TrafficSpawner>();
        spawner.Configure(map, player, Camera.main, AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<VehicleAI>());
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/TrafficDemo.unity");
    }

    // 기존 내장 스프라이트로 진행 방향을 구분하는 최소 차량을 만든다
    private static void Marker(Transform parent, Sprite sprite, string name, Vector2 size, Vector2 offset, Color color, int order)
    {
        GameObject marker = new GameObject(name, typeof(SpriteRenderer));
        marker.transform.SetParent(parent, false);
        marker.transform.localPosition = offset;
        marker.transform.localScale = new Vector3(size.x / sprite.bounds.size.x, size.y / sprite.bounds.size.y, 1f);
        SpriteRenderer renderer = marker.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = order;
    }
    // 저장된 9단계 씬을 다시 열고 Play 검증만 실행한다
    [MenuItem("CodeBlue Rush/Traffic/Validate Play")]
    public static void RunPlay()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Play 종료 후 실행하세요.");
        CreateDemo();
        EditorSceneManager.OpenScene("Assets/Scenes/TrafficDemo.unity");
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
                throw new TimeoutException("Traffic Play validation timeout");
            if (!EditorApplication.isPlaying || Time.frameCount < 2 || frame == Time.frameCount)
                return;
            frame = Time.frameCount;
            if (run == null)
                run = Validate();
            if (run.MoveNext())
                return;
            Check(!errors, "런타임 오류와 NullReference 없음");
            Debug.Log("Traffic play validation passed: " + checks + " checks");
            Finish(true);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(false);
        }
    }

    // 검증에서만 Inspector 수치를 명시적으로 조절한다
    private static void Set(Object target, string field, float value, bool integer = false)
    {
        SerializedObject data = new SerializedObject(target);
        if (integer)
            data.FindProperty(field).intValue = (int)value;
        else
            data.FindProperty(field).floatValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    // 시험 도로가 실제 화면 밖 생성 범위에 들어오도록 플레이어를 배치한다
    private static void FrameLane(TrafficLane lane, Transform player, Camera camera)
    {
        lane.TrySample(lane.Length * 0.5f, out Vector3 point, out Vector3 direction);
        player.position = point + Vector3.Cross(direction, Vector3.forward) * 25f;
        camera.transform.position = player.position + Vector3.back * 10f;
        Physics2D.SyncTransforms();
    }

    // 실제 도시에서 주행과 센서 및 생성 수명을 검증한다
    private static IEnumerator Validate()
    {
        TrafficSpawner spawner = Object.FindFirstObjectByType<TrafficSpawner>();
        CityMap map = spawner.Map;
        AmbulanceController player = Object.FindFirstObjectByType<AmbulanceController>();
        player.GetComponent<KeyboardAmbulanceInput>().enabled = false;
        player.enabled = false;
        Camera camera = Camera.main;
        camera.GetComponent<AmbulanceCamera>().enabled = false;
        Set(spawner, "attempts", 0f, true);
        TrafficLane straight = null;
        TrafficLane corner = null;
        for (int i = 0; i < map.LaneCount; i++)
        {
            TrafficLane lane = map.GetLane(i);
            if (!straight && lane.Length >= 15f && Vector3.Dot(lane.StartDirection, lane.EndDirection) > 0.99f)
                straight = lane;
            if (!corner && lane.Length >= 5f && Vector3.Dot(lane.StartDirection, lane.EndDirection) < 0.8f)
                corner = lane;
            if (straight && corner)
                break;
        }
        Check(straight && corner, "실제 직선과 코너 차선 확보");
        FrameLane(straight, player.transform, camera);
        Check(!spawner.TrySpawn(null, 1f, out _), "null 차선 생성 거절");
        Check(!spawner.CanSpawnAt(player.transform.position), "플레이어 근처 생성 차단");
        straight.TrySample(9f, out Vector3 visible, out _);
        Vector3 cameraPosition = camera.transform.position;
        camera.transform.position = visible + Vector3.back * 10f;
        Check(!spawner.TrySpawn(straight, 9f, out _), "실제 화면 안 생성 차단");
        camera.transform.position = cameraPosition;
        Check(spawner.TrySpawn(straight, 9f, out VehicleAI lead), "유효 차선 정방향 생성");
        Set(lead, "cruiseSpeed", 0f);
        Check(!spawner.TrySpawn(straight, 9f, out _), "같은 위치 중복 생성 차단");
        Check(spawner.TrySpawn(straight, 2f, out VehicleAI follower), "뒤 차량 생성");
        Physics2D.SyncTransforms();
        var clearance = typeof(VehicleAI).GetMethod("Clearance", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        float expectedClearance = (float)clearance.Invoke(follower, null);
        GameObject clutter = new GameObject("UnrelatedTriggers");
        straight.TrySample(2f, out Vector3 source, out Vector3 forward);
        for (int i = 0; i < 40; i++)
        {
            GameObject trigger = new GameObject("Trigger", typeof(BoxCollider2D));
            trigger.transform.SetParent(clutter.transform);
            trigger.transform.position = source + Vector3.Cross(forward, Vector3.forward) * 4f;
            trigger.GetComponent<BoxCollider2D>().isTrigger = true;
        }
        Physics2D.SyncTransforms();
        Check(Mathf.Approximately((float)clearance.Invoke(follower, null), expectedClearance), "무관한 Trigger가 조회 버퍼를 채워도 앞차 감지 거리 유지");
        Object.DestroyImmediate(clutter);
        float until = Time.time + 5f;
        while (Time.time < until) yield return null;
        Check(follower.Lane == straight && follower.Distance > 2f && follower.Speed < 0.1f && lead.Distance - follower.Distance > 1.8f, "앞 차량까지 주행 후 안전 간격 정지");
        Capture(camera, follower.transform.position);
        float before = follower.Distance;
        Set(lead, "cruiseSpeed", 5f);
        until = Time.time + 1f;
        while (Time.time < until) yield return null;
        Check(follower.Speed > 0f && follower.Distance > before, "앞 차량 출발 후 재가속");
        spawner.Release(lead);
        spawner.Release(follower);
        Check(!lead.Lane && !follower.Lane && spawner.ActiveCount == 0 && spawner.PooledCount >= 2, "반환 시 차선과 활성 참조 정리");
        FrameLane(straight, player.transform, camera);
        Check(spawner.TrySpawn(straight, straight.Length - 0.5f, out VehicleAI crossing), "끝 지점 차량 생성과 풀 재사용");
        Set(crossing, "cruiseSpeed", 5f);
        TrafficLane expected = crossing.NextLane;
        until = Time.time + 2f;
        while (crossing.Lane == straight && Time.time < until) yield return null;
        Check(crossing.Lane == expected, "직접 연결된 Next Lane 전환");
        Check(crossing.Lane.TryProject(crossing.transform.position, out _, out float offset) && offset < 0.2f, "차선 밖 이탈 없음");
        spawner.Release(crossing);
        FrameLane(straight, player.transform, camera);
        Check(spawner.TrySpawn(straight, straight.Length - 6f, out VehicleAI boundaryFollower), "경계 전 뒤 차량 생성");
        Check(spawner.TrySpawn(boundaryFollower.NextLane, 2f, out VehicleAI boundaryLeader), "다음 차선 앞 차량 생성");
        Set(boundaryFollower, "cruiseSpeed", 5f);
        Set(boundaryLeader, "cruiseSpeed", 0f);
        until = Time.time + 5f;
        while (Time.time < until) yield return null;
        Check(boundaryFollower.Speed < 0.1f && Vector3.Distance(boundaryFollower.transform.position, boundaryLeader.transform.position) > 1.8f, "Lane 경계 너머 앞차 간격 유지");
        spawner.Release(boundaryLeader);
        until = Time.time + 1f;
        while (Time.time < until) yield return null;
        Check(boundaryFollower.Speed > 0f, "앞차 반환 후 막힘 없이 재출발");
        spawner.Release(boundaryFollower);
        FrameLane(corner, player.transform, camera);
        Check(spawner.TrySpawn(corner, 0.1f, out VehicleAI turning), "코너 진입 차량 생성");
        Set(turning, "cruiseSpeed", 5f);
        float initialAngle = turning.transform.eulerAngles.z;
        until = Time.time + 4f;
        while (turning.Lane == corner && Time.time < until) yield return null;
        Check(turning.Lane && Mathf.Abs(Mathf.DeltaAngle(initialAngle, turning.transform.eulerAngles.z)) > 20f, "코너 진행 방향과 부드러운 회전");
        Check(turning.Lane.TryProject(turning.transform.position, out _, out offset) && offset < 0.2f, "코너에서도 기존 경로 추종");
        spawner.Release(turning);
        FrameLane(straight, player.transform, camera);
        Check(spawner.TrySpawn(straight, 2f, out VehicleAI invalid), "차선 무효화 시험 차량");
        straight.enabled = false;
        until = Time.time + 0.1f;
        while (Time.time < until) yield return null;
        Check(!invalid.gameObject.activeSelf && !invalid.Lane, "비활성 차선 차량 정리");
        straight.enabled = true;
        Check(spawner.TrySpawn(straight, 2f, out VehicleAI missing), "현재 Lane 누락 시험 차량");
        typeof(VehicleAI).GetField("<Lane>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(missing, null);
        until = Time.time + 0.1f;
        while (Time.time < until) yield return null;
        Check(!missing.gameObject.activeSelf && spawner.ActiveCount == 0, "현재 Lane null 안전 반환");
        FieldInfo next = typeof(TrafficLane).GetField("nextLane", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo branches = typeof(TrafficLane).GetField("internalNext", BindingFlags.Instance | BindingFlags.NonPublic);
        object oldNext = next.GetValue(straight);
        object oldBranches = branches.GetValue(straight);
        try
        {
            next.SetValue(straight, null);
            branches.SetValue(straight, Array.Empty<TrafficLane>());
            Check(!spawner.TrySpawn(straight, 2f, out _), "Next Lane 없음 생성 차단");
            next.SetValue(straight, straight);
            Check(!spawner.TrySpawn(straight, 2f, out _), "잘못된 방향과 접점 연결 차단");
        }
        finally
        {
            next.SetValue(straight, oldNext);
            branches.SetValue(straight, oldBranches);
        }
        Check(spawner.TrySpawn(straight, 2f, out VehicleAI distant), "거리 정리 시험 차량");
        player.transform.position += Vector3.right * 200f;
        until = Time.time + 0.7f;
        while (Time.time < until) yield return null;
        Check(!distant.gameObject.activeSelf && !distant.Lane, "멀어진 차량 풀 반환");
        FrameLane(straight, player.transform, camera);
        Set(spawner, "maxVehicles", 4f, true);
        Set(spawner, "attempts", 64f, true);
        bool withinLimit = true;
        until = Time.time + 3f;
        while (Time.time < until)
        {
            withinLimit &= spawner.ActiveCount <= 4;
            yield return null;
        }
        Check(withinLimit, "최대 수량 유지");
        Check(spawner.ActiveCount > 0 && spawner.PooledCount + spawner.ActiveCount <= 4, "주변 생성과 작은 풀 재사용");
        Set(spawner, "attempts", 0f, true);
        VehicleAI oldVehicle = spawner.GetVehicle(0);
        Check(map.TryStartNewCity(unchecked(map.Seed + 1), out _), "새 도시 생성");
        Check(spawner.ActiveCount == 0 && !oldVehicle.Lane && !oldVehicle.gameObject.activeSelf, "이전 도시 차량 즉시 정리");
        Check(!spawner.TrySpawn(straight, 2f, out _), "이전 도시 차선 생성 거절");
        TrafficLane fresh = map.GetLane(0);
        FrameLane(fresh, player.transform, camera);
        Check(spawner.TrySpawn(fresh, fresh.Length * 0.5f, out VehicleAI external), "새 도시 차량 재사용");
        Object.Destroy(external.gameObject);
        yield return null;
        Check(spawner.ActiveCount == 0, "외부 파괴 후 참조 정리");
        spawner.enabled = false;
    }

    // 실제 주행 현장과 차량 간격을 세로 화면으로 렌더링한다
    private static void Capture(Camera camera, Vector3 point)
    {
        Vector3 position = camera.transform.position;
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        bool shown = canvas.enabled;
        RenderTexture oldTarget = camera.targetTexture;
        RenderTexture oldActive = RenderTexture.active;
        RenderTexture texture = new RenderTexture(480, 854, 24);
        Texture2D image = new Texture2D(480, 854, TextureFormat.RGB24, false);
        try
        {
            canvas.enabled = false;
            camera.transform.position = point + Vector3.back * 10f;
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0, 0, 480, 854), 0, 0);
            image.Apply();
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllBytes("Logs/traffic-driving.png", image.EncodeToPNG());
        }
        finally
        {
            canvas.enabled = shown;
            camera.transform.position = position;
            camera.targetTexture = oldTarget;
            RenderTexture.active = oldActive;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(texture);
        }
    }
    // 실패 조건을 즉시 보고한다
    private static void Check(bool condition, string label)
    {
        if (!condition)
            throw new InvalidOperationException("Traffic validation failed: " + label);
        checks++;
        Debug.Log("Traffic check passed: " + label);
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


