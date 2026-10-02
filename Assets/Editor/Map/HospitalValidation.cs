using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>8단계 데모와 실제 픽업부터 병원 도착까지의 Play 검증을 제공한다</summary>
public static class HospitalValidation
{
    private const string Key = "CodeBlueRush.HospitalValidation";
    private static IEnumerator run;
    private static int frame;
    private static int checks;
    private static bool errors;
    private static double deadline;
    private static double startAt;

    // 기존 ECG 데모에 한 병원과 이송 컴포넌트만 연결한다
    [MenuItem("CodeBlue Rush/Hospital/Create Demo Scene")]
    public static void CreateDemo()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/PatientECGDemo.unity");
        CityMap map = Object.FindFirstObjectByType<CityMap>();
        PatientReport report = Object.FindFirstObjectByType<PatientReport>();
        NavigationRoute route = Object.FindFirstObjectByType<NavigationRoute>();
        PatientECG ecg = Object.FindFirstObjectByType<PatientECG>();
        Rigidbody2D body = Object.FindFirstObjectByType<AmbulanceController>().GetComponent<Rigidbody2D>();
        const string path = "Assets/Prefabs/Hospital.prefab";
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(path))
        {
            GameObject root = new GameObject("Hospital Arrival Point", typeof(CircleCollider2D), typeof(HospitalArrivalZone));
            CircleCollider2D area = root.GetComponent<CircleCollider2D>();
            area.isTrigger = true;
            area.radius = 2.5f;
            Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            AddMarker(root.transform, sprite, "Hospital Marker", new Vector2(2f, 2f), new Color(0.1f, 0.45f, 0.8f), 4);
            AddMarker(root.transform, sprite, "Cross Horizontal", new Vector2(1.4f, 0.4f), Color.white, 5);
            AddMarker(root.transform, sprite, "Cross Vertical", new Vector2(0.4f, 1.4f), Color.white, 5);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        HospitalArrivalZone zone = instance.GetComponent<HospitalArrivalZone>();
        zone.Configure(map);
        PrefabUtility.RecordPrefabInstancePropertyModifications(zone);
        HospitalTransfer transfer = report.gameObject.AddComponent<HospitalTransfer>();
        transfer.Configure(report, route, ecg, body, new[] { zone });
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/HospitalDemo.unity");
    }

    // 기존 내장 스프라이트로 병원 도착점의 최소 표식을 만든다
    private static void AddMarker(Transform parent, Sprite sprite, string name, Vector2 size, Color color, int order)
    {
        GameObject marker = new GameObject(name, typeof(SpriteRenderer));
        marker.transform.SetParent(parent, false);
        marker.transform.localScale = new Vector3(size.x / sprite.bounds.size.x, size.y / sprite.bounds.size.y, 1f);
        SpriteRenderer renderer = marker.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = order;
    }
    // 저장된 8단계 씬을 다시 열고 Play 검증만 실행한다
    [MenuItem("CodeBlue Rush/Hospital/Validate Play")]
    public static void RunPlay()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Play 종료 후 실행하세요.");
        CreateDemo();
        EditorSceneManager.OpenScene("Assets/Scenes/HospitalDemo.unity");
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
                throw new TimeoutException("Hospital Play validation timeout");
            if (!EditorApplication.isPlaying || Time.frameCount < 2 || frame == Time.frameCount)
                return;
            frame = Time.frameCount;
            if (run == null)
                run = Validate();
            if (run.MoveNext())
                return;
            Check(!errors, "런타임 오류와 NullReference 없음");
            Debug.Log("Hospital play validation passed: " + checks + " checks");
            Finish(true);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(false);
        }
    }

    // 실제 보고와 차량 정차로 기존 픽업 시스템을 완료시킨다
    private static IEnumerator Pickup(PatientReport report, NavigationRoute route, Rigidbody2D body)
    {
        report.CancelReport();
        NavigationEditor.Preview(route);
        route.ClearDestination();
        Check(report.TryReport(out _), "기존 상황 보고 성공");
        Physics2D.SyncTransforms();
        report.Patient.TryGetPose(out Vector3 position, out Vector3 direction);
        body.position = (Vector2)(position + direction * 2f);
        body.rotation = Vector2.SignedAngle(Vector2.up, direction);
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        Physics2D.SyncTransforms();
        float until = Time.time + 5f;
        while (!report.IsPatientOnBoard && Time.time < until)
            yield return null;
        Check(report.IsPatientOnBoard, "기존 PatientPickup 실제 탑승 완료");
    }

    // 기존 픽업과 실제 물리 영역으로 병원 이송 및 종료 경쟁을 검사한다
    private static IEnumerator Validate()
    {
        PatientReport report = Object.FindFirstObjectByType<PatientReport>();
        NavigationRoute route = Object.FindFirstObjectByType<NavigationRoute>();
        PatientECG ecg = Object.FindFirstObjectByType<PatientECG>();
        HospitalTransfer transfer = Object.FindFirstObjectByType<HospitalTransfer>();
        HospitalArrivalZone zone = Object.FindFirstObjectByType<HospitalArrivalZone>();
        AmbulanceController vehicle = Object.FindFirstObjectByType<AmbulanceController>();
        Rigidbody2D body = vehicle.GetComponent<Rigidbody2D>();
        vehicle.GetComponent<KeyboardAmbulanceInput>().enabled = false;
        vehicle.enabled = false;
        int arrivals = 0;
        int deaths = 0;
        transfer.HospitalArrived += () => arrivals++;
        ecg.PatientDied += () => deaths++;
        Check(zone.IsValidFor(route.Map), "생성된 도시의 실제 차선에 병원 등록");
        body.position = zone.HospitalPoint;
        Physics2D.SyncTransforms();
        float until = Time.time + 0.15f;
        while (Time.time < until) yield return null;
        Check(!transfer.TryArrive(zone) && transfer.Status == HospitalTransfer.TransferStatus.Idle && arrivals == 0, "픽업 전 Trigger 진입 거절");
        IEnumerator pickup = Pickup(report, route, body);
        while (pickup.MoveNext()) yield return null;
        Check(transfer.Status == HospitalTransfer.TransferStatus.Transporting && transfer.Destination == zone, "픽업 후 병원 지정");
        Check(route.Status == NavigationRoute.RouteStatus.Ready && route.DestinationLane == zone.Lane && route.DestinationDistance == zone.Distance && route.Points.Count > 0, "환자 경로를 실제 병원 도로 경로로 교체");
        HospitalArrivalZone wrong = Object.Instantiate(zone);
        wrong.Configure(route.Map, 5);
        body.position = wrong.HospitalPoint;
        Physics2D.SyncTransforms();
        Check(!transfer.TryArrive(wrong) && arrivals == 0, "다른 병원 영역 거절");
        Object.Destroy(wrong.gameObject);
        body.position = zone.HospitalPoint + Vector3.right * 10f;
        Physics2D.SyncTransforms();
        Check(!transfer.TryArrive(zone), "올바른 병원도 영역 밖에서 거절");
        until = Time.time + 0.1f;
        while (Time.time < until) yield return null;
        ecg.TakeDamage(15f);
        body.position = zone.HospitalPoint;
        Physics2D.SyncTransforms();
        until = Time.time + 1f;
        while (arrivals == 0 && Time.time < until) yield return null;
        Check(arrivals == 1 && transfer.Status == HospitalTransfer.TransferStatus.Arrived, "실제 Trigger로 도착 1회");
        float frozen = ecg.Value;
        PatientECG.ECGState frozenState = ecg.State;
        Check(!ecg.IsDecaying && transfer.ArrivalECG == frozen && frozen < 100f && transfer.ArrivalState == frozenState, "도착 값 보존 및 자연 감소 즉시 중지");
        until = Time.time + 0.3f;
        while (Time.time < until) yield return null;
        Check(ecg.Value == frozen && ecg.State == frozenState && deaths == 0, "회복과 Reset 없이 ECG 고정");
        Check(!transfer.TryArrive(zone) && !route.HasDestination && arrivals == 1, "중복 도착과 경로 종료");
        transfer.enabled = false;
        transfer.enabled = true;
        Check(!transfer.TryArrive(zone) && arrivals == 1, "재활성화 후 확정 상태 유지");
        transfer.Configure(report, route, ecg, body, new[] { zone });
        Check(!transfer.TryArrive(zone) && arrivals == 1 && transfer.Status == HospitalTransfer.TransferStatus.Arrived, "같은 미션 재연결 후 중복 확정 없음");
        pickup = Pickup(report, route, body);
        while (pickup.MoveNext()) yield return null;
        Check(transfer.Status == HospitalTransfer.TransferStatus.Transporting, "새 미션 이송 준비");
        ecg.TakeDamage(100f);
        body.position = zone.HospitalPoint;
        Physics2D.SyncTransforms();
        Check(!transfer.TryArrive(zone) && transfer.Status == HospitalTransfer.TransferStatus.PatientDied && deaths == 1 && arrivals == 1, "사망 우선 시 같은 미션 도착 차단");
        pickup = Pickup(report, route, body);
        while (pickup.MoveNext()) yield return null;
        ecg.TakeDamage(ecg.Value - 0.001f);
        body.position = zone.HospitalPoint;
        Physics2D.SyncTransforms();
        Check(transfer.TryArrive(zone), "ECG 0 직전 도착 우선 확정");
        until = Time.time + 0.2f;
        while (Time.time < until) yield return null;
        Check(ecg.Value > 0f && deaths == 1 && arrivals == 2, "도착 우선 시 자연 사망 동시 확정 없음");
        report.CancelReport();
        Check(transfer.Destination == null && transfer.MissionId == 0 && transfer.Status == HospitalTransfer.TransferStatus.Idle, "종료된 미션 병원 참조 정리");
        zone.enabled = false;
        pickup = Pickup(report, route, body);
        while (pickup.MoveNext()) yield return null;
        Check(transfer.Status == HospitalTransfer.TransferStatus.NoHospital && !transfer.TryArrive(zone), "유효 병원 없음 안전 실패");
        zone.enabled = true;
        body.transform.position = new Vector3(-999f, -999f, 0f);
        Physics2D.SyncTransforms();
        Check(!transfer.TryBeginTransfer() && transfer.Status == HospitalTransfer.TransferStatus.NoRoute, "병원은 있지만 유효 경로 없음 안전 실패");
        transfer.Configure(report, null, ecg, body, new[] { zone });
        Check(transfer.Status == HospitalTransfer.TransferStatus.MissingReferences && !transfer.TryArrive(zone), "Navigation 누락 안전 실패");
        transfer.Configure(report, route, null, body, new[] { zone });
        Check(transfer.Status == HospitalTransfer.TransferStatus.MissingReferences && !transfer.TryArrive(zone), "ECG 누락 안전 실패");
        transfer.Configure(report, route, ecg, body, new[] { zone });
        TrafficLane sample = route.Map.GetLane(0);
        if (sample == zone.Lane)
            sample = route.Map.GetLane(route.Map.LaneCount - 1);
        sample.TrySample(sample.Length * 0.5f, out Vector3 isolatedPosition, out _);
        body.transform.position = isolatedPosition;
        Physics2D.SyncTransforms();
        Check(route.Map.TryLocate(isolatedPosition, 2f, out TrafficLane isolated, out _) && isolated != zone.Lane, "실제 도로의 단절 경로 시험 위치");
        FieldInfo nextField = typeof(TrafficLane).GetField("nextLane", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo internalField = typeof(TrafficLane).GetField("internalNext", BindingFlags.Instance | BindingFlags.NonPublic);
        object savedNext = nextField.GetValue(isolated);
        object savedInternal = internalField.GetValue(isolated);
        bool noPath = false;
        Action observeRoute = () => noPath |= route.Status == NavigationRoute.RouteStatus.NoPath;
        route.RouteChanged += observeRoute;
        try
        {
            nextField.SetValue(isolated, null);
            internalField.SetValue(isolated, Array.Empty<TrafficLane>());
            Check(!transfer.TryBeginTransfer() && transfer.Status == HospitalTransfer.TransferStatus.NoRoute && noPath, "실제 단절 차선에서는 병원 이송 실패");
        }
        finally
        {
            nextField.SetValue(isolated, savedNext);
            internalField.SetValue(isolated, savedInternal);
            route.RouteChanged -= observeRoute;
        }
        pickup = Pickup(report, route, body);
        while (pickup.MoveNext()) yield return null;
        TrafficLane previousLane = zone.Lane;
        int oldMission = transfer.MissionId;
        Check(route.Map.TryStartNewCity(unchecked(route.Map.Seed + 1), out _), "새 도시 생성");
        Check(!report.IsActive && transfer.Destination == null && zone.Lane != previousLane && zone.IsValidFor(route.Map) && !transfer.TryArrive(zone), "새 도시 병원 재등록 및 이전 참조 무효화");
        pickup = Pickup(report, route, body);
        while (pickup.MoveNext()) yield return null;
        Check(transfer.MissionId != oldMission && transfer.Destination == zone, "새 도시의 새 미션 연결");
        Object.Destroy(zone.gameObject);
        yield return null;
        Check(!transfer.TryArrive(zone), "파괴된 병원 안전 거절");
        report.CancelReport();
        Check(transfer.Destination == null && arrivals == 2 && deaths == 1, "정리 후 중복 성공 및 사망 없음");
    }
    // 실패 조건을 즉시 보고한다
    private static void Check(bool condition, string label)
    {
        if (!condition)
            throw new InvalidOperationException("Hospital validation failed: " + label);
        checks++;
        Debug.Log("Hospital check passed: " + label);
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

