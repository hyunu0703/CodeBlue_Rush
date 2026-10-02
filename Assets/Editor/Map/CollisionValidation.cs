using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>13단계 데모 자산과 실제 Rigidbody 및 Trigger 접촉의 Play 검증을 제공한다</summary>
public static class CollisionValidation
{
    private const string ScenePath = "Assets/Scenes/CollisionDemo.unity";
    private const string Key = "CodeBlueRush.CollisionValidation";
    private const string SirenKey = "CodeBlueRush.CollisionSirenRegression";
    private static IEnumerator run;
    private static int frame;
    private static int checks;
    private static bool errors;
    private static double deadline;
    private static CollisionDemo demo;
    private static AmbulanceController ambulance;
    private static Rigidbody2D body;
    private static PatientReport report;
    private static PatientECG ecg;
    private static HospitalTransfer transfer;
    private static TrafficSpawner traffic;
    private static int deaths;
    private static int fatals;

    // 기존 미션 데모를 복사하고 실제 두 차량 Prefab과 직선 도로 테스트를 연결한다
    [MenuItem("CodeBlue Rush/Stage 13/Create Demo Scene")]
    public static void CreateDemoScene()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/HospitalDemo.unity");
        CityMap map = Object.FindFirstObjectByType<CityMap>();
        PatientReport mission = Object.FindFirstObjectByType<PatientReport>();
        AmbulanceController player = Object.FindFirstObjectByType<AmbulanceController>();
        PatientECG patient = Object.FindFirstObjectByType<PatientECG>();
        HospitalTransfer hospital = Object.FindFirstObjectByType<HospitalTransfer>();
        Camera camera = Camera.main;
        Set(map, "randomSeedOnStart", false);
        Set(map, "initialSeed", 12345);
        GameObject root = new GameObject("Stage13 Collision Demo");
        CitizenSpawner citizens = root.AddComponent<CitizenSpawner>();
        citizens.enabled = false;
        TrafficSpawner spawner = root.AddComponent<TrafficSpawner>();
        Set(spawner, "map", map);
        Set(spawner, "player", player.transform);
        Set(spawner, "view", camera);
        Set(spawner, "prefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/TrafficVehicle.prefab").GetComponent<VehicleAI>());
        Set(spawner, "maxVehicles", 1);
        Set(spawner, "spawnNear", 5f);
        Set(spawner, "spawnFar", 200f);
        Set(spawner, "despawnDistance", 200f);
        Set(spawner, "interval", 600f);
        CollisionDemo controls = root.AddComponent<CollisionDemo>();
        Set(controls, "map", map);
        Set(controls, "navigation", Object.FindFirstObjectByType<NavigationRoute>());
        Set(controls, "report", mission);
        Set(controls, "ecg", patient);
        Set(controls, "transfer", hospital);
        Set(controls, "ambulance", player);
        Set(controls, "traffic", spawner);
        Set(controls, "view", camera);
        AmbulanceCollision collision = player.GetComponent<AmbulanceCollision>();
        collision.Configure(mission, patient, hospital);
        PrefabUtility.RecordPrefabInstancePropertyModifications(collision);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("Stage13 demo created: " + ScenePath);
    }

    // 저장된 전용 데모에서 실제 Play 검증을 시작한다
    [MenuItem("CodeBlue Rush/Stage 13/Validate Play")]
    public static void RunPlay()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Play 모드 종료 후 실행하세요.");
        CreateDemoScene();
        SessionState.SetBool(SirenKey, false);
        SessionState.SetBool(Key, true);
        Register();
        EditorApplication.EnterPlaymode();
    }

    // 10단계의 실제 복수 차선 데모에서 사이렌 양보만 추가 검증한다
    [MenuItem("CodeBlue Rush/Stage 13/Validate Siren Regression")]
    public static void RunSirenPlay()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Play 모드 종료 후 실행하세요.");
        EditorSceneManager.OpenScene("Assets/Scenes/LaneChangeDemo.unity");
        SessionState.SetBool(SirenKey, true);
        SessionState.SetBool(Key, true);
        Register();
        EditorApplication.EnterPlaymode();
    }

    // 도메인 재로드 후 검증 상태와 오류 수집을 복원한다
    [InitializeOnLoadMethod]
    private static void Register()
    {
        if (!SessionState.GetBool(Key, false))
            return;
        run = null;
        frame = -1;
        checks = deaths = fatals = 0;
        errors = false;
        deadline = EditorApplication.timeSinceStartup + 180d;
        Application.logMessageReceived -= Observe;
        Application.logMessageReceived += Observe;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    // 실제 Play 프레임마다 물리 검증을 한 번만 진행한다
    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new TimeoutException("Stage13 Play timeout");
            if (!EditorApplication.isPlaying || Time.frameCount < 3 || frame == Time.frameCount)
                return;
            frame = Time.frameCount;
            if (run == null)
                run = SessionState.GetBool(SirenKey, false) ? ValidateSiren() : Validate();
            if (run.MoveNext())
                return;
            Check(!errors, "No runtime errors / NullReference");
            Finish(true);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(false);
        }
    }

    // 미션 판정과 실제 두 차량의 움직임 및 수명 초기화를 확인한다
    private static IEnumerator Validate()
    {
        demo = Object.FindFirstObjectByType<CollisionDemo>();
        ambulance = Object.FindFirstObjectByType<AmbulanceController>();
        body = ambulance.GetComponent<Rigidbody2D>();
        report = Object.FindFirstObjectByType<PatientReport>();
        ecg = Object.FindFirstObjectByType<PatientECG>();
        transfer = Object.FindFirstObjectByType<HospitalTransfer>();
        traffic = Object.FindFirstObjectByType<TrafficSpawner>();
        Check(demo && report && ecg && transfer && traffic, "Demo Inspector references");
        Check(new SerializedObject(ambulance).FindProperty("maxSpeed").floatValue == 10f, "Actual prefab max speed 10");
        ecg.PatientDied += HandleDeath;
        report.FatalCollision += HandleFatal;
        ecg.survivalTime = 1000000f;
        ambulance.GetComponent<KeyboardAmbulanceInput>().enabled = false;
        body.position = new Vector2(-100f, -100f);
        body.rotation = 0f;
        ambulance.SetInput(0f, 1f, 0f);
        float until = Time.time + 3f;
        while (Time.time < until) yield return null;
        Check(Mathf.Abs(body.linearVelocity.magnitude - 10f) < 0.01f, "Rigidbody reaches and clamps to 10");
        ambulance.SetInput(0f, 0f, 0f);

        IEnumerator test = Scenario(0);
        while (test.MoveNext()) yield return null;
        Check(!report.IsPatientOnBoard && ecg.Value == 100f && report.CollisionCount == 0 && fatals == 0, "Before pickup low: no mission damage");
        test = Scenario(4);
        while (test.MoveNext()) yield return null;
        Check(!report.IsPatientOnBoard && ecg.Value == 100f && report.CollisionCount == 0 && fatals == 0, "Before pickup high: no fatal mission");
        test = Scenario(1);
        while (test.MoveNext()) yield return null;
        Check(report.CollisionCount == 1 && ecg.Value > 49.9f && ecg.Value <= 50f && deaths == 0, "Transport low: count +1, ECG -50");
        int mission = report.MissionId;
        test = Scenario(1);
        while (test.MoveNext()) yield return null;
        Check(report.MissionId == mission && report.CollisionCount == 2 && ecg.Value == 0f && deaths == 1 && transfer.Status == HospitalTransfer.TransferStatus.PatientDied, "Second low: existing PatientDied once");
        test = Scenario(2);
        while (test.MoveNext()) yield return null;
        Check(report.MissionId != mission && report.CollisionCount == 0 && report.HasFatalCollision && fatals == 1 && ecg.Value > 99.9f, "New mission reset / fatal once / no duplicate HP system");
        test = Scenario(3);
        while (test.MoveNext()) yield return null;
        Check(transfer.Status == HospitalTransfer.TransferStatus.Arrived && report.IsPatientOnBoard && ecg.Value == transfer.ArrivalECG && report.CollisionCount == 0 && !report.HasFatalCollision && fatals == 1, "Arrived: immutable mission results even while patient onboard");
        test = PoolDuringStun();
        while (test.MoveNext()) yield return null;

        // 동일 접촉을 실제 물리 위치로 유지하고 완전 이탈 뒤 다시 진입한다
        demo.RunScenario(1);
        until = Time.time + 15f;
        while ((!demo.Target || !ambulance.IsControlLocked) && Time.time < until) yield return null;
        Check(demo.Target && ambulance.IsControlLocked, "Persistent contact setup");
        VehicleAI car = demo.Target;
        Rigidbody2D targetBody = car.GetComponent<Rigidbody2D>();
        int count = report.CollisionCount;
        float value = ecg.Value;
        float collisionSpeed = ambulance.GetComponent<AmbulanceCollision>().LastCollisionSpeed;
        until = Time.time + 2.3f;
        while (Time.time < until)
        {
            body.position = targetBody.position;
            body.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
            yield return null;
        }
        Check(report.CollisionCount == count && ecg.Value > value - 0.01f && fatals == 1, "Continuous contact does not repeat damage or fatal");
        Check(ambulance.GetComponent<AmbulanceCollision>().LastCollisionSpeed == collisionSpeed, "Stun callbacks preserve pre-knockback sample");
        if (car && car.isActiveAndEnabled)
            traffic.Release(car);
        while (demo.IsRunning) yield return null;
        Check(!ambulance.IsControlLocked, "Control unlock after persistent contact");
        ecg.PatientDied -= HandleDeath;
        report.FatalCollision -= HandleFatal;
    }

    // 데모에서 실제 Trigger가 들어온 순간부터 넉백과 2초 잠금 및 AI 복귀를 측정한다
    private static IEnumerator Scenario(int scenario)
    {
        demo.RunScenario(scenario);
        Check(demo.IsRunning, "Scenario " + scenario + " started");
        float until = Time.time + 15f;
        while (!ambulance.IsControlLocked && demo.IsRunning && Time.time < until) yield return null;
        Check(ambulance.IsControlLocked && demo.Target, demo.Error ?? "Actual Trigger contact");
        VehicleAI car = demo.Target;
        TrafficLane lane = car.Lane;
        Rigidbody2D targetBody = car.GetComponent<Rigidbody2D>();
        Check(targetBody.bodyType == RigidbodyType2D.Kinematic && car.GetComponent<CircleCollider2D>().isTrigger, "Existing Kinematic / Trigger structure");
        float speed = ambulance.GetComponent<AmbulanceCollision>().LastCollisionSpeed;
        Check(scenario == 0 || scenario == 1 ? speed < 4f : speed >= 4f, "Pre-knockback speed threshold " + speed.ToString("F3"));
        Vector2 start = targetBody.position;
        Vector2 playerStart = body.position;
        if (scenario == 0)
            Capture((start + playerStart) * 0.5f, "Contact");
        float began = Time.time;
        int count = report.CollisionCount;
        int events = fatals;
        while (Time.time - began < 0.15f)
        {
            ambulance.SetInput(1f, 1f, 1f);
            yield return null;
        }
        Check(car.IsStunned && Vector2.Distance(start, targetBody.position) > 0.2f, "AI visible knockback without next-frame Lane snap");
        Check(Vector2.Dot(targetBody.position - start, body.position - playerStart) < 0f, "Opposite actual Rigidbody displacements");
        if (scenario == 0)
            Capture((start + playerStart) * 0.5f, "Knockback");
        Check((float)typeof(AmbulanceController).GetField("throttle", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ambulance) == 0f && (float)typeof(AmbulanceController).GetField("steer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ambulance) == 0f, "All input paths rejected / old input reset");
        while (Time.time - began < 1.8f)
        {
            ambulance.SetInput(1f, 1f, 1f);
            yield return null;
        }
        Check(ambulance.IsControlLocked && car.IsStunned && body.linearVelocity.sqrMagnitude < 0.001f, "Both stunned for about two seconds");
        while (demo.IsRunning && Time.time < until) yield return null;
        Check(!demo.IsRunning && demo.Error == null && !ambulance.IsControlLocked, demo.Error ?? "Control recovery");
        Check(report.CollisionCount == count && fatals == events, "One accident during stun");
        Check(car && car.isActiveAndEnabled && !car.IsStunned && car.Lane == lane, "AI safe projection / Lane recovery");
        float along = car.Distance;
        until = Time.time + 0.35f;
        while (Time.time < until) yield return null;
        Check(car.Distance > along && car.Speed > 0f, "AI resumes actual Lane driving");
        ambulance.GetComponent<KeyboardAmbulanceInput>().enabled = false;
        ambulance.SetInput(0f, 1f, 0f);
        until = Time.time + 0.2f;
        while (Time.time < until) yield return null;
        Check(body.linearVelocity.magnitude > 0.1f, "Controller accepts new input after unlock");
        ambulance.SetInput(0f, 0f, 0f);
    }

    // 실제 경직 중 풀 반환과 같은 인스턴스 재생성으로 잔여 상태를 검사한다
    private static IEnumerator PoolDuringStun()
    {
        demo.RunScenario(0);
        float until = Time.time + 15f;
        while ((!demo.Target || !ambulance.IsControlLocked) && demo.IsRunning && Time.time < until) yield return null;
        VehicleAI car = demo.Target;
        Check(car && car.IsStunned, "Pool test real accident");
        TrafficLane lane = car.Lane;
        float along = car.Distance;
        traffic.Release(car);
        Check(!car.IsStunned && !car.Lane && car.GetComponent<Rigidbody2D>().linearVelocity == Vector2.zero, "Pool release clears stun / velocity / Lane");
        while (demo.IsRunning && Time.time < until) yield return null;
        lane.TrySample(along, out Vector3 point, out _);
        Vector2 spawnPlayer = (Vector2)point + Vector2.right * 10f;
        body.transform.position = spawnPlayer;
        body.position = spawnPlayer;
        Camera camera = Camera.main;
        Vector3 previous = camera.transform.position;
        float size = camera.orthographicSize;
        camera.transform.position = (Vector3)body.position + Vector3.back * 10f;
        camera.orthographicSize = 1f;
        Physics2D.SyncTransforms();
        bool spawned = traffic.TrySpawn(lane, along, out VehicleAI reused);
        camera.transform.position = previous;
        camera.orthographicSize = size;
        Check(spawned && reused == car && !reused.IsStunned, "Same pooled instance resets movement lock");
        float distance = reused.Distance;
        until = Time.time + 0.3f;
        while (Time.time < until) yield return null;
        Check(reused.Distance > distance, "Reused vehicle immediately drives normally");
        traffic.Release(reused);
    }

    // 실제 접근 속도와 사이렌 이벤트를 사용해 AI의 정상 차선 양보를 검증한다
    private static IEnumerator ValidateSiren()
    {
        traffic = Object.FindFirstObjectByType<TrafficSpawner>();
        ambulance = Object.FindFirstObjectByType<AmbulanceController>();
        body = ambulance.GetComponent<Rigidbody2D>();
        ambulance.GetComponent<KeyboardAmbulanceInput>().enabled = false;
        ambulance.enabled = false;
        Camera camera = Camera.main;
        camera.GetComponent<AmbulanceCamera>().enabled = false;
        Set(traffic, "attempts", 0);
        while (traffic.ActiveCount > 0)
            traffic.Release(traffic.GetVehicle(0));
        CityMap map = traffic.Map;
        TrafficLane inner = null;
        for (int i = 0; i < map.LaneCount; i++)
            if (map.GetLane(i).RightLane && map.GetLane(i).Length > 12f)
            {
                inner = map.GetLane(i);
                break;
            }
        Check(inner, "Siren: existing adjacent lanes");
        inner.TrySample(4f, out Vector3 point, out Vector3 direction);
        body.transform.position = point + Vector3.Cross(direction, Vector3.forward) * 25f;
        body.position = body.transform.position;
        camera.transform.position = body.transform.position + Vector3.back * 10f;
        Physics2D.SyncTransforms();
        Check(traffic.TrySpawn(inner, 4f, out VehicleAI car), "Siren: real TrafficVehicle prefab spawn");
        Set(car, "cruiseSpeed", 2f);
        Set(car, "decisionInterval", 100f);
        Set(car, "cutInChance", 0f);
        car.AllowLaneChanges = false;
        float until = Time.time + 1.5f;
        while (Time.time < until) yield return null;
        inner.TrySample(Mathf.Max(0f, car.Distance - 5f), out point, out direction);
        body.transform.SetPositionAndRotation(point, Quaternion.FromToRotation(Vector3.up, direction));
        body.position = point;
        body.linearVelocity = direction * 6f;
        Physics2D.SyncTransforms();
        SirenController siren = ambulance.GetComponent<SirenController>();
        int changes = 0;
        Action<bool> changed = _ => changes++;
        siren.Changed += changed;
        siren.Set(true);
        siren.Set(true);
        car.AllowLaneChanges = true;
        typeof(VehicleAI).GetField("nextDecision", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(car, 0f);
        until = Time.time + 0.3f;
        while (!car.IsChangingLane && Time.time < until) yield return null;
        Check(changes == 1 && car.IsChangingLane && car.TargetLane == inner.RightLane, "Siren: one event and actual AI yield to safe adjacent lane");
        body.linearVelocity = Vector2.zero;
        body.transform.position = point + Vector3.Cross(direction, Vector3.forward) * 25f;
        body.position = body.transform.position;
        Physics2D.SyncTransforms();
        until = Time.time + 2f;
        while (car.IsChangingLane && Time.time < until) yield return null;
        Check(car.Lane == inner.RightLane && car.Speed > 0f && !car.IsStunned, "Siren: smooth yield completes and normal driving continues");
        siren.Set(false);
        Check(changes == 2, "Siren: off event once");
        siren.Changed -= changed;
        traffic.Release(car);
    }

    // 그래픽이 제공되는 Play에서는 실제 데모 차량의 접촉과 이동을 이미지로 남긴다
    private static void Capture(Vector2 center, string name)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            return;
        Camera camera = Camera.main;
        Vector3 position = camera.transform.position;
        float size = camera.orthographicSize;
        RenderTexture oldTarget = camera.targetTexture;
        RenderTexture oldActive = RenderTexture.active;
        RenderTexture texture = new RenderTexture(960, 540, 24);
        Texture2D image = new Texture2D(960, 540, TextureFormat.RGB24, false);
        try
        {
            camera.transform.position = (Vector3)center + Vector3.back * 10f;
            camera.orthographicSize = 6f;
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0f, 0f, 960f, 540f), 0, 0);
            image.Apply();
            string logs = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs"));
            Directory.CreateDirectory(logs);
            File.WriteAllBytes(Path.Combine(logs, "Stage13-" + name + ".png"), image.EncodeToPNG());
        }
        finally
        {
            camera.transform.position = position;
            camera.orthographicSize = size;
            camera.targetTexture = oldTarget;
            RenderTexture.active = oldActive;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(texture);
        }
    }

    // 검증에서만 직렬화 필드를 연결하며 런타임 API를 확장하지 않는다
    private static void Set(Object target, string field, object value)
    {
        SerializedObject data = new SerializedObject(target);
        SerializedProperty property = data.FindProperty(field);
        if (property == null)
            throw new MissingFieldException(target.GetType().Name, field);
        if (value is Object reference) property.objectReferenceValue = reference;
        else if (value is bool flag) property.boolValue = flag;
        else if (value is int number) property.intValue = number;
        else if (value is float numberFloat) property.floatValue = numberFloat;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    // 환자 사망 이벤트 호출 수를 기록한다
    private static void HandleDeath() { deaths++; }

    // 치명 사고 이벤트 호출 수를 기록한다
    private static void HandleFatal() { fatals++; }

    // 실패한 실제 동작을 즉시 검증 오류로 남긴다
    private static void Check(bool condition, string label)
    {
        if (!condition)
            throw new InvalidOperationException("Stage13: " + label);
        checks++;
    }

    // 런타임 예외와 중복 루틴 오류를 수집한다
    private static void Observe(string message, string trace, LogType type)
    {
        if (trace.Contains("UnityEditor.Search."))
            return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors = true;
    }

    // 결과를 파일과 Console에 남기고 배치 실행 또는 Play만 종료한다
    private static void Finish(bool success)
    {
        string result = (SessionState.GetBool(SirenKey, false) ? "Siren regression " : "Stage13 play validation ") + (success ? "passed" : "FAILED") + ": " + checks + " checks";
        string logs = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs"));
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "Stage13Result.txt"), result);
        Debug.Log(result);
        SessionState.SetBool(Key, false);
        SessionState.SetBool(SirenKey, false);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= Observe;
        if (ecg) ecg.PatientDied -= HandleDeath;
        if (report) report.FatalCollision -= HandleFatal;
        run = null;
        if (Application.isBatchMode)
            EditorApplication.Exit(success ? 0 : 1);
        else
            EditorApplication.ExitPlaymode();
    }
}
