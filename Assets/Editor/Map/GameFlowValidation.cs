using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>실제 게임 Scene에서 입력 운전과 미션 수명 및 결과 경쟁을 검증한다</summary>
public static class GameFlowValidation
{
    private const string Key = "CodeBlueRush.GameFlowValidation";
    private const string LifecycleKey = "CodeBlueRush.GameFlowLifecycle";
    private static IEnumerator run;
    private static int frame;
    private static int checks;
    private static bool errors;
    private static double deadline;
    private static GameFlow flow;
    private static PatientReport report;
    private static NavigationRoute navigation;
    private static PatientECG ecg;
    private static HospitalTransfer transfer;
    private static AmbulanceController ambulance;
    private static Rigidbody2D body;
    private static TrafficSpawner traffic;

    // 실제 Build에서 Editor 검증과 데모 코드가 필요하지 않은지 확인한다
    public static void BuildPlayer()
    {
        Directory.CreateDirectory("Build");
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { GameplayScene.Path },
            locationPathName = "Build/CodeBlueRush.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new InvalidOperationException("Gameplay player build failed");
    }

    [MenuItem("CodeBlue Rush/Gameplay/Validate Play")]
    public static void RunPlay()
    {
        SessionState.SetBool(LifecycleKey, false);
        BeginPlay();
    }

    [MenuItem("CodeBlue Rush/Gameplay/Validate Lifecycle")]
    public static void RunLifecycle()
    {
        SessionState.SetBool(LifecycleKey, true);
        BeginPlay();
    }

    private static void BeginPlay()
    {
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/Stage14Checks.txt", string.Empty);
        File.WriteAllText("Logs/Stage14Result.txt", "RUNNING");
        EditorSceneManager.OpenScene(GameplayScene.Path);
        SessionState.SetBool(Key, true);
        Register();
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    private static void Register()
    {
        if (!SessionState.GetBool(Key, false)) return;
        run = null;
        frame = -1;
        checks = 0;
        errors = false;
        deadline = EditorApplication.timeSinceStartup + 1200d;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= Observe;
        Application.logMessageReceived += Observe;
    }

    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Gameplay validation timeout");
            if (!EditorApplication.isPlaying || Time.frameCount < 3 || frame == Time.frameCount) return;
            frame = Time.frameCount;
            if (run == null) run = Validate();
            if (run.MoveNext()) return;
            Check(!errors, "No runtime errors");
            Finish(true);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(false);
        }
    }

    private static IEnumerator Validate()
    {
        flow = Object.FindFirstObjectByType<GameFlow>();
        report = Object.FindFirstObjectByType<PatientReport>();
        navigation = Object.FindFirstObjectByType<NavigationRoute>();
        ecg = Object.FindFirstObjectByType<PatientECG>();
        transfer = Object.FindFirstObjectByType<HospitalTransfer>();
        ambulance = Object.FindFirstObjectByType<AmbulanceController>();
        body = ambulance.GetComponent<Rigidbody2D>();
        traffic = Object.FindFirstObjectByType<TrafficSpawner>();
        Time.timeScale = 3f;
        CityMap map = navigation.Map;
        Check(flow.State == GameFlow.Phase.DrivingToPatient && map.IsReady && report.IsActive, "Play starts city and mission automatically");
        Check(navigation.Status == NavigationRoute.RouteStatus.Ready && navigation.Points.Count > 0, "Patient navigation ready");
        Check(!Object.FindFirstObjectByType<CollisionDemo>(), "Gameplay has no demo controls");
        Check(Object.FindFirstObjectByType<TrafficSpawner>() && Object.FindFirstObjectByType<CitizenSpawner>(), "Traffic and pedestrians connected");
        Capture("GameplayStart.png");
        Check(GameFlow.Evaluate(PatientECG.ECGState.Green, 0) == 3 && GameFlow.Evaluate(PatientECG.ECGState.Yellow, 0) == 3 && GameFlow.Evaluate(PatientECG.ECGState.Red, 0) == 2, "Zero collision ratings");
        Check(GameFlow.Evaluate(PatientECG.ECGState.Yellow, 1) == 2 && GameFlow.Evaluate(PatientECG.ECGState.Red, 1) == 1 && GameFlow.Evaluate(PatientECG.ECGState.Green, 1) == 0 && GameFlow.Evaluate(PatientECG.ECGState.Red, 2) == 0, "One collision ratings and invalid combinations");
        ambulance.GetComponent<KeyboardAmbulanceInput>().enabled = false;
        ambulance.GetComponent<SirenController>().Toggle();
        RoadChunk layout = map.GetRoad(0, 0);
        int worldId = map.GetRoad(0, 0).GetInstanceID();
        int mission = report.MissionId;
        var previous = report.Patient;
        int results = 0;
        int failures = 0;
        flow.Changed += () => { if (flow.State == GameFlow.Phase.Result) results++; if (flow.State == GameFlow.Phase.GameOver) failures++; };

        for (int cycle = 0; cycle < 2; cycle++)
        {
            IEnumerator drive = Drive(false);
            while (drive.MoveNext()) yield return null;
            Check(report.IsPatientOnBoard && ecg.IsDecaying && flow.State == GameFlow.Phase.Transporting, "Normal stop and boarding start ECG");
            Check(transfer.Destination && navigation.DestinationLane == transfer.Destination.Lane, "Pickup selects hospital route");
            drive = Drive(true);
            while (drive.MoveNext()) yield return null;
            Check(flow.State == GameFlow.Phase.Result && !ecg.IsDecaying && flow.Stars == GameFlow.Evaluate(transfer.ArrivalState, report.CollisionCount), "Hospital arrival produces frozen result");
            Check(results == cycle + 1 && failures == 0, "One success and no failure per mission");
            float arrival = ecg.Value;
            for (int i = 0; i < 8; i++) yield return null;
            Check(ecg.Value == arrival && !transfer.TryArrive(transfer.Destination), "Arrival cannot repeat");
            if (cycle == 0) Capture("GameplayResult.png");
            ClickContinue();
            flow.Continue();
            yield return null;
            Check(map.GetRoad(0, 0) == layout && map.GetRoad(0, 0).GetInstanceID() == worldId && report.MissionId == ++mission, "Continue reuses city and creates exactly one mission");
            Check(!report.IsPatientOnBoard && !ecg.HasPatient && ecg.Value == 100f && report.CollisionCount == 0 && !report.HasFatalCollision && transfer.Destination == null, "Mission reset removes previous patient, ECG, collision and hospital");
            Check(previous.Slot != report.Patient.Slot, "New patient location");
            previous = report.Patient;
            if (!ambulance.GetComponent<SirenController>().IsOn) ambulance.GetComponent<SirenController>().Toggle();
        }

        // 실패 및 경계값 회귀에서만 기존 픽업 위치/API를 제한적으로 사용한다
        IEnumerator pickup = PickupAtSite();
        while (pickup.MoveNext()) yield return null;
        ecg.TakeDamage(100f);
        Check(flow.State == GameFlow.Phase.GameOver && !report.IsActive && !body.simulated && failures == 1 && results == 2, "PatientDied commits one GameOver and stops mission");
        Capture("GameplayGameOver.png");
        ClickContinue();
        yield return null;
        Check(map.GetRoad(0, 0).GetInstanceID() == worldId && map.GetRoad(0, 0) == layout && flow.State == GameFlow.Phase.DrivingToPatient, "Death restarts a mission on the same fixed map");
        worldId = map.GetRoad(0, 0).GetInstanceID();
        layout = map.GetRoad(0, 0);
        pickup = PickupAtSite();
        while (pickup.MoveNext()) yield return null;
        RecordCollision(true);
        RecordCollision(true);
        ecg.TakeDamage(100f);
        Check(flow.State == GameFlow.Phase.GameOver && failures == 2 && results == 2, "FatalCollision and subsequent death cannot double-complete");
        ClickContinue();
        yield return null;
        Check(map.GetRoad(0, 0).GetInstanceID() == worldId && map.GetRoad(0, 0) == layout && report.CollisionCount == 0 && !report.HasFatalCollision, "Fatal collision resets mission on the same fixed map");
        pickup = PickupAtSite();
        while (pickup.MoveNext()) yield return null;
        RecordCollision(false);
        ecg.TakeDamage(50f);
        Park(transfer.Destination.HospitalPoint, transfer.Destination.Lane.StartDirection);
        float until = Time.time + 2f;
        while (flow.State != GameFlow.Phase.Result && Time.time < until) yield return null;
        Check(flow.State == GameFlow.Phase.Result && flow.Stars == 2 && report.CollisionCount == 1, "One low speed collision data produces two stars");
        RecordCollision(true);
        ecg.TakeDamage(100f);
        Check(flow.State == GameFlow.Phase.Result && failures == 2, "Late failure cannot replace success");
        ClickContinue();
        pickup = PickupAtSite();
        while (pickup.MoveNext()) yield return null;
        RecordCollision(false);
        ecg.TakeDamage(50f);
        RecordCollision(false);
        ecg.TakeDamage(50f);
        Check(flow.State == GameFlow.Phase.GameOver && failures == 3, "Second collision causes GameOver");
    }

    // 좌표나 상태를 변경하지 않고 기존 가속·조향·브레이크 입력으로 운전한다
    private static IEnumerator Drive(bool hospital)
    {
        if (SessionState.GetBool(LifecycleKey, false))
        {
            if (!hospital)
            {
                IEnumerator pickup = PickupAtSite();
                while (pickup.MoveNext()) yield return null;
            }
            else
            {
                Park(transfer.Destination.HospitalPoint, transfer.Destination.Lane.StartDirection);
                float timeout = Time.time + 2f;
                while (flow.State != GameFlow.Phase.Result && Time.time < timeout) yield return null;
            }
            yield break;
        }
        Vector3[] points = new Vector3[navigation.Points.Count + 1];
        for (int i = 0; i < navigation.Points.Count; i++)
        {
            Vector3 direction = navigation.Points[Mathf.Min(i + 1, navigation.Points.Count - 1)] - navigation.Points[Mathf.Max(0, i - 1)];
            points[i] = navigation.Points[i] + Vector3.Cross(direction.normalized, Vector3.forward) * 1.7f;
        }
        points[points.Length - 1] = hospital ? transfer.Destination.HospitalPoint : report.Patient.Slot.transform.position;
        int index = 1;
        float until = Time.time + 300f;
        float nextLog = Time.time;
        while (Time.time < until)
        {
            if (flow.State == GameFlow.Phase.Result || (!hospital && report.IsPatientOnBoard))
            {
                ambulance.SetInput(0f, 0f, 1f);
                yield break;
            }
            if (flow.State == GameFlow.Phase.GameOver) throw new InvalidOperationException($"Driving failed: collisions={report.CollisionCount}, fatal={report.HasFatalCollision}, impact={ambulance.GetComponent<AmbulanceCollision>().LastCollisionSpeed}");
            Vector2 destination = points[points.Length - 1];
            float remaining = Vector2.Distance(body.position, destination);
            if (remaining < 1.4f)
                ambulance.SetInput(0f, 0f, 1f);
            else
            {
                while (index < points.Length - 1 && Vector2.Distance(body.position, points[index]) < 5f) index++;
                Vector2 delta = (Vector2)points[index] - body.position;
                for (int i = 0; i < traffic.ActiveCount; i++)
                {
                    VehicleAI car = traffic.GetVehicle(i);
                    Vector2 offset = (Vector2)car.transform.position - body.position;
                    float forward = Vector2.Dot(offset, body.transform.up);
                    float side = Vector2.Dot(offset, body.transform.right);
                    if (forward > 0f && forward < 10f && Mathf.Abs(side) < 2.5f)
                    {
                        delta += (Vector2)body.transform.right * (side > 0f ? -8f : 8f);
                        break;
                    }
                }
                float angle = Vector2.SignedAngle(body.transform.up, delta);
                float steer = Mathf.Clamp(-angle / 30f, -1f, 1f);
                float targetSpeed = remaining < 6f ? 2f : Mathf.Abs(angle) > 25f ? 2.5f : hospital ? 3.2f : 6f;
                float speed = body.linearVelocity.magnitude;
                ambulance.SetInput(steer, speed < targetSpeed ? 1f : 0f, speed > targetSpeed + 0.1f ? 1f : 0f);
            }
            if (Time.time >= nextLog)
            {
                File.WriteAllText("Logs/Stage14Progress.txt", $"{(hospital ? "Hospital" : "Patient")} waypoint {index}/{points.Length}, position {body.position}, target {points[index]}, remaining {remaining:F1}, phase {flow.State}, ECG {ecg.Value:F1}");
                nextLog = Time.time + 5f;
            }
            yield return null;
        }
        throw new TimeoutException("Normal input driving timed out");
    }

    private static IEnumerator PickupAtSite()
    {
        report.Patient.TryGetPose(out Vector3 position, out Vector3 direction);
        Park(position, direction);
        float until = Time.time + 8f;
        while (!report.IsPatientOnBoard && Time.time < until) yield return null;
        Check(report.IsPatientOnBoard, "Regression pickup completes");
    }

    private static void Park(Vector3 point, Vector3 direction)
    {
        ambulance.SetInput(0f, 0f, 1f);
        body.position = point;
        body.rotation = Vector2.SignedAngle(Vector2.up, direction);
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        Physics2D.SyncTransforms();
    }

    private static void RecordCollision(bool fatal)
    {
        typeof(PatientReport).GetMethod("RecordCollision", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(report, new object[] { report.MissionId, fatal });
    }

    private static void ClickContinue()
    {
        Object.FindFirstObjectByType<GameFlowUI>().transform.Find("Result/Continue").GetComponent<Button>().onClick.Invoke();
    }

    private static void Capture(string name)
    {
        Camera camera = Camera.main;
        Canvas canvas = Object.FindFirstObjectByType<GameFlowUI>().GetComponent<Canvas>();
        RenderTexture previous = camera.targetTexture;
        RenderTexture active = RenderTexture.active;
        int order = canvas.sortingOrder;
        var texture = new RenderTexture(480, 854, 24);
        var image = new Texture2D(480, 854, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = texture;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.sortingOrder = 100;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0f, 0f, 480f, 854f), 0, 0);
            image.Apply();
            File.WriteAllBytes("Logs/" + name, image.EncodeToPNG());
        }
        finally
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            canvas.worldCamera = null;
            camera.targetTexture = previous;
            RenderTexture.active = active;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(texture);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
        File.AppendAllText("Logs/Stage14Checks.txt", message + "\n");
    }

    private static void Observe(string message, string trace, LogType type)
    {
        if (!trace.Contains("UnityEditor.Search.") && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) errors = true;
    }

    private static void Finish(bool success)
    {
        File.WriteAllText(SessionState.GetBool(LifecycleKey, false) ? "Logs/Stage14LifecycleResult.txt" : "Logs/Stage14Result.txt", (success ? "PASS: " : "FAIL: ") + checks);
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= Observe;
        Time.timeScale = 1f;
        EditorApplication.ExitPlaymode();
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
    }
}
