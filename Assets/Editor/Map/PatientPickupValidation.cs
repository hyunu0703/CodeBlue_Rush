using System;
using System.Collections;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>실제 Rigidbody2D와 활성 미션으로 픽업 조건 및 이동과 정리 수명을 검증한다</summary>
public static class PatientPickupValidation
{
    private const string PlayKey = "CodeBlueRush.PatientPickupValidation";
    private static IEnumerator run;
    private static double deadline;
    private static double startAt;
    private static int frame;
    private static int checks;
    private static bool errorLogged;

    // 2~5단계 회귀 후 픽업 자산과 별도 데모를 준비한다
    public static void RunRegression()
    {
        PatientReportValidation.Run();
        PatientPickupEditor.CreateDemo();
    }

    // 사용 중인 씬과 분리된 데모에서 Play 검증을 시작한다
    [MenuItem("CodeBlue Rush/Patient Pickup/Validate Play Lifecycle")]
    public static void RunPlay()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Play 모드 종료 후 실행하세요.");
        PatientPickupEditor.CreateDemo();
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/PatientPickupDemo.unity");
        if (!Object.FindFirstObjectByType<PatientPickup>())
            return;
        startAt = EditorApplication.timeSinceStartup + 5d;
        EditorApplication.update -= WaitForEditor;
        EditorApplication.update += WaitForEditor;
    }

    // 에디터 시작 콜백이 끝난 후 검증 Play 모드로 진입한다
    private static void WaitForEditor()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < startAt)
            return;
        EditorApplication.update -= WaitForEditor;
        SessionState.SetBool(PlayKey, true);
        Register();
        EditorApplication.EnterPlaymode();
    }

    // 도메인 재로드 후 검증 진행과 오류 수집을 준비한다
    [InitializeOnLoadMethod]
    private static void Register()
    {
        if (!SessionState.GetBool(PlayKey, false))
            return;
        checks = 0;
        frame = -1;
        run = null;
        errorLogged = false;
        deadline = EditorApplication.timeSinceStartup + 180d;
        Application.logMessageReceived -= ObserveLog;
        Application.logMessageReceived += ObserveLog;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    // 실제 플레이 프레임마다 검증을 한 단계 진행한다
    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new TimeoutException("픽업 검증 시간 초과");
            if (!EditorApplication.isPlaying || Time.frameCount < 2 || frame == Time.frameCount)
                return;
            frame = Time.frameCount;
            if (run == null)
                run = CheckPickup();
            if (run.MoveNext())
                return;
            Check(!errorLogged, "런타임 오류 로그 없음");
            Debug.Log("Patient pickup play validation passed: " + checks + " checks");
            Finish(true);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(false);
        }
    }

    // 현재 신고 현장으로 실제 차량을 배치하고 정차 속도를 적용한다
    private static void Park(PatientReport report, Rigidbody2D body)
    {
        Physics2D.SyncTransforms();
        report.Patient.TryGetPose(out Vector3 point, out Vector3 direction);
        body.position = (Vector2)(point + direction * 2f);
        body.rotation = Vector2.SignedAngle(Vector2.up, direction);
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        Physics2D.SyncTransforms();
    }

    // 이동과 정차 및 객체 수명을 실제 시간 경과에 따라 검사한다
    private static IEnumerator CheckPickup()
    {
        PatientReport report = Object.FindFirstObjectByType<PatientReport>();
        NavigationRoute route = Object.FindFirstObjectByType<NavigationRoute>();
        PatientPickup pickup = Object.FindFirstObjectByType<PatientPickup>();
        AmbulanceController controller = Object.FindFirstObjectByType<AmbulanceController>();
        Rigidbody2D body = controller.GetComponent<Rigidbody2D>();
        controller.GetComponent<KeyboardAmbulanceInput>().enabled = false;
        controller.SetInput(1f, 1f, 0f);
        float initialRotation = body.rotation;
        float driveUntil = Time.time + 0.6f;
        while (Time.time < driveUntil) yield return null;
        Check(body.linearVelocity.magnitude > 0.1f && Mathf.Abs(Mathf.DeltaAngle(initialRotation, body.rotation)) > 0.1f, "기존 구급차 가속과 조향");
        controller.SetInput(0f, 0f, 1f);
        driveUntil = Time.time + 0.5f;
        while (Time.time < driveUntil) yield return null;
        Check(body.linearVelocity.magnitude <= 0.15f, "기존 구급차 제동");
        controller.SetInput(0f, 0f, 0f);
        controller.enabled = false;
        SerializedObject settings = new SerializedObject(pickup);
        Transform patient = (Transform)settings.FindProperty("patient").objectReferenceValue;
        Transform medic = (Transform)settings.FindProperty("paramedic").objectReferenceValue;
        Transform rear = (Transform)settings.FindProperty("rearBoardingPoint").objectReferenceValue;
        Check(settings.FindProperty("report").objectReferenceValue == report && settings.FindProperty("ambulance").objectReferenceValue == body && patient && medic, "씬 Inspector 미션과 차량 및 두 배우 참조");
        CircleCollider2D area = pickup.GetComponent<CircleCollider2D>();
        Check(area && area.isTrigger && body.simulated && route.Map, "픽업 영역과 물리 및 맵 연결");
        Check(rear && rear.IsChildOf(body.transform), "실제 구급차 뒤쪽 자식 탑승점");
        Check(!pickup.TryStartPickup(), "미션 없음");
        NavigationEditor.Preview(route);
        route.ClearDestination();
        Check(report.TryReport(out _), "현장 보고");
        int firstMission = report.MissionId;
        int completions = 0;
        report.PatientPickedUp += () => completions++;
        report.Patient.TryGetPose(out Vector3 spawn, out _);
        Check(Vector3.Distance(pickup.transform.position, spawn) < 0.001f && patient.gameObject.activeSelf && medic.gameObject.activeSelf, "SpawnPoint 기준 현장 활성화");
        Park(report, body);
        Capture("before", spawn);
        Check(!pickup.TryStartPickup(), "진입만으로 시작하지 않음");
        body.linearVelocity = Vector2.right;
        Vector3 before = patient.position;
        float until = Time.time + 0.5f;
        while (Time.time < until) yield return null;
        Check(!pickup.IsBoarding && patient.position == before && !report.IsPatientOnBoard, "움직이는 구급차 픽업 금지");
        Park(report, body);
        body.angularVelocity = 30f;
        until = Time.time + 0.4f;
        while (Time.time < until) yield return null;
        Check(!pickup.IsBoarding, "회전 중 정차 판정 거절");
        Park(report, body);
        until = Time.time + 0.1f;
        while (Time.time < until) yield return null;
        Check(!pickup.IsBoarding, "연속 정차 시간 대기");
        while (!pickup.IsBoarding) yield return null;
        Check(!pickup.TryStartPickup(), "중복 시작 요청 거절");
        until = Time.time + 0.1f;
        while (Time.time < until) yield return null;
        Check(patient.position != before && patient.gameObject.activeSelf && !report.IsPatientOnBoard, "순간이동 없는 중간 이동 상태");
        Capture("moving", spawn);
        body.linearVelocity = Vector2.right * 0.5f;
        yield return null;
        while (!pickup.IsPaused) yield return null;
        before = patient.position;
        Vector3 medicBefore = medic.position;
        until = Time.time + 0.3f;
        while (Time.time < until) yield return null;
        Check(patient.position == before && medic.position == medicBefore && !report.IsPatientOnBoard, "차량 재이동 시 제자리 일시정지");
        body.linearVelocity = Vector2.zero;
        until = Time.time + 0.1f;
        while (Time.time < until) yield return null;
        Check(patient.position == before && pickup.IsPaused, "재정차 안정 시간 대기");
        while (!report.IsPatientOnBoard) yield return null;
        Check(completions == 1 && report.IsActive && report.MissionId == firstMission, "동일 미션 탑승 완료와 단일 이벤트");
        Check(!patient.gameObject.activeSelf && !medic.gameObject.activeSelf && !pickup.IsBoarding, "두 객체 숨김과 이동 종료");
        Capture("boarded", spawn);
        Check(!route.HasDestination && route.Points.Count == 0, "현장 목적지만 정리");
        Check(!pickup.TryStartPickup() && !report.TryReport(out _), "탑승 후 중복 픽업과 중복 보고 금지");
        body.position += Vector2.right * 20f;
        Physics2D.SyncTransforms();
        until = Time.time + 0.2f;
        while (Time.time < until) yield return null;
        Park(report, body);
        until = Time.time + 0.6f;
        while (Time.time < until) yield return null;
        Check(completions == 1, "영역 재진입 후 중복 완료 없음");
        route.SetDestination(route.Map.GetLane(0), 0f);
        Check(report.IsPatientOnBoard && report.IsActive, "후속 네비게이션 요청이 탑승 상태를 취소하지 않음");

        report.CancelReport();
        Check(!report.IsPatientOnBoard && !report.IsActive, "게임 종료 연결 API로 탑승 상태 초기화");
        NavigationEditor.Preview(route);
        route.ClearDestination();
        Check(report.TryReport(out _) && report.MissionId != firstMission, "새 보고 식별자와 현장 재사용");
        Park(report, body);
        typeof(PatientPickup).GetField("missionId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(pickup, firstMission);
        until = Time.time + 0.6f;
        while (Time.time < until) yield return null;
        Check(!pickup.TryStartPickup() && !pickup.IsBoarding && !report.IsPatientOnBoard && completions == 1, "이전 MissionId 현장의 픽업 거절");
        pickup.RefreshSite();
        pickup.transform.position += Vector3.right * 30f;
        body.position = pickup.transform.position;
        Physics2D.SyncTransforms();
        until = Time.time + 0.6f;
        while (Time.time < until) yield return null;
        Check(!pickup.IsBoarding && !report.IsPatientOnBoard, "다른 위치로 옮겨진 영역의 픽업 거절");
        pickup.Configure(report, body, null);
        Check(!pickup.TryStartPickup() && !string.IsNullOrEmpty(pickup.Error), "RearBoardingPoint 누락 안전 실패");
        pickup.Configure(report, body, rear);
        settings.Update();
        settings.FindProperty("patient").objectReferenceValue = null;
        settings.ApplyModifiedPropertiesWithoutUndo();
        pickup.RefreshSite();
        Check(!pickup.TryStartPickup() && !string.IsNullOrEmpty(pickup.Error), "환자 참조 누락 안전 실패");
        settings.Update();
        settings.FindProperty("patient").objectReferenceValue = patient;
        settings.FindProperty("paramedic").objectReferenceValue = null;
        settings.ApplyModifiedPropertiesWithoutUndo();
        pickup.RefreshSite();
        Check(!pickup.TryStartPickup() && !string.IsNullOrEmpty(pickup.Error), "구급대원 참조 누락 안전 실패");
        settings.Update();
        settings.FindProperty("paramedic").objectReferenceValue = medic;
        settings.ApplyModifiedPropertiesWithoutUndo();
        pickup.RefreshSite();
        Park(report, body);
        while (!pickup.IsBoarding) yield return null;
        patient.gameObject.SetActive(false);
        until = Time.time + 0.1f;
        while (Time.time < until) yield return null;
        Check(!pickup.IsBoarding && !report.IsPatientOnBoard && !medic.gameObject.activeSelf && !string.IsNullOrEmpty(pickup.Error), "이동 중 환자 비활성화 안전 중단");
        pickup.RefreshSite();
        while (!pickup.IsBoarding) yield return null;
        report.CancelReport();
        Check(!pickup.IsBoarding && !patient.gameObject.activeSelf && !medic.gameObject.activeSelf, "이동 중 미션 취소 즉시 정리");
        Check(!pickup.TryStartPickup(), "종료된 미션 요청 거절");
        NavigationEditor.Preview(route);
        route.ClearDestination();
        Check(report.TryReport(out _), "새 보고 후 이전 현장 참조 교체");
        Park(report, body);
        until = Time.time + 3f;
        while (!pickup.IsBoarding && Time.time < until) yield return null;
        Check(pickup.IsBoarding, "새 미션 픽업 시작: vehicle=" + body.position + ", site=" + pickup.transform.position + ", velocity=" + body.linearVelocity + ", active=" + report.IsActive + ", actors=" + patient.gameObject.activeInHierarchy + "/" + medic.gameObject.activeInHierarchy + ", error=" + pickup.Error);
        Check(route.Map.TryStartNewCity(618, out _), "이동 중 새 도시 생성");
        Check(!report.IsActive && !pickup.IsBoarding && !patient.gameObject.activeSelf && !medic.gameObject.activeSelf, "이전 도시 현장과 픽업 정리");
        NavigationEditor.Preview(route);
        route.ClearDestination();
        Check(report.TryReport(out _), "새 도시 현장");
        Park(report, body);
        while (!pickup.IsBoarding) yield return null;
        Object.Destroy(medic.gameObject);
        until = Time.time + 0.15f;
        while (Time.time < until) yield return null;
        Check(!pickup.IsBoarding && !report.IsPatientOnBoard && !patient.gameObject.activeSelf && !string.IsNullOrEmpty(pickup.Error), "이동 중 구급대원 파괴 안전 중단");
        Check(completions == 1, "실패 및 취소 경로에서 완료 이벤트 없음");
        report.CancelReport();
    }

    // 현장의 배치와 이동 및 탑승 후 상태를 세로 화면으로 렌더링한다
    private static void Capture(string phase, Vector3 center)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            return;
        Camera camera = Camera.main;
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        Vector3 previousPosition = camera.transform.position;
        float previousSize = camera.orthographicSize;
        bool canvasEnabled = canvas.enabled;
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture texture = new RenderTexture(480, 854, 24);
        Texture2D image = new Texture2D(480, 854, TextureFormat.RGB24, false);
        try
        {
            canvas.enabled = false;
            camera.transform.position = new Vector3(center.x, center.y, -10f);
            camera.orthographicSize = 5f;
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0f, 0f, 480f, 854f), 0, 0);
            image.Apply();
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllBytes("Logs/patient-pickup-" + phase + ".png", image.EncodeToPNG());
        }
        finally
        {
            canvas.enabled = canvasEnabled;
            camera.transform.position = previousPosition;
            camera.orthographicSize = previousSize;
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(texture);
        }
    }

    // 실패한 조건을 검증 실행의 오류로 전달한다
    private static void Check(bool condition, string label)
    {
        if (!condition)
            throw new InvalidOperationException("Patient pickup validation failed: " + label);
        checks++;
        Debug.Log("Patient pickup check passed: " + label);
    }

    // 조건 검사 밖에서 발생한 런타임 오류도 기록한다
    private static void ObserveLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errorLogged = true;
    }

    // 검증 상태와 구독을 해제하고 실행 환경에 맞게 종료한다
    private static void Finish(bool success)
    {
        SessionState.SetBool(PlayKey, false);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= ObserveLog;
        run = null;
        if (Application.isBatchMode)
            EditorApplication.Exit(success ? 0 : 1);
        else
            EditorApplication.ExitPlaymode();
    }
}
