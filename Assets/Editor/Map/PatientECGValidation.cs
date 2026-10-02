using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>7단계 데모 자산 작성과 실제 픽업 이벤트 기반 ECG Play 검증을 제공한다</summary>
public static class PatientECGValidation
{
    private const string Key = "CodeBlueRush.ECGValidation";
    private static IEnumerator run;
    private static int frame;
    private static int checks;
    private static bool errors;
    private static double deadline;
    private static double startAt;

    // 기존 픽업 데모를 보존하고 ECG와 화면 아래 파동만 연결한다
    [MenuItem("CodeBlue Rush/Patient ECG/Create Demo Scene")]
    public static void CreateDemo()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/PatientPickupDemo.unity");
        PatientReport report = Object.FindFirstObjectByType<PatientReport>();
        PatientECG ecg = report.gameObject.AddComponent<PatientECG>();
        ecg.Configure(report);
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        GameObject panel = new GameObject("ECG Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvas.transform, false);
        RectTransform rect = (RectTransform)panel.transform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 24f);
        rect.sizeDelta = new Vector2(-32f, 120f);
        Image background = panel.GetComponent<Image>();
        background.color = new Color(0.015f, 0.04f, 0.035f, 0.95f);
        background.raycastTarget = false;
        GameObject wave = new GameObject("ECG Wave", typeof(RectTransform), typeof(ECGGraphic));
        wave.transform.SetParent(panel.transform, false);
        RectTransform waveRect = (RectTransform)wave.transform;
        waveRect.anchorMin = Vector2.zero;
        waveRect.anchorMax = Vector2.one;
        waveRect.offsetMin = new Vector2(12f, 12f);
        waveRect.offsetMax = new Vector2(-12f, -12f);
        ECGGraphic graphic = wave.GetComponent<ECGGraphic>();
        graphic.raycastTarget = false;
        graphic.Configure(ecg);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/PatientECGDemo.unity");
    }

    // 저장된 7단계 씬을 다시 열고 Play 검증만 실행한다
    [MenuItem("CodeBlue Rush/Patient ECG/Validate Play")]
    public static void RunPlay()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Play 종료 후 실행하세요.");
        CreateDemo();
        EditorSceneManager.OpenScene("Assets/Scenes/PatientECGDemo.unity");
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
                throw new TimeoutException("ECG Play validation timeout");
            if (!EditorApplication.isPlaying || Time.frameCount < 2 || frame == Time.frameCount)
                return;
            frame = Time.frameCount;
            if (run == null)
                run = Validate();
            if (run.MoveNext())
                return;
            Check(!errors, "런타임 오류와 NullReference 없음");
            Debug.Log("Patient ECG play validation passed: " + checks + " checks");
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

    // 값 경계와 180초 자연 감소 및 이벤트 수명을 검증한다
    private static IEnumerator Validate()
    {
        PatientECG ecg = Object.FindFirstObjectByType<PatientECG>();
        PatientReport report = Object.FindFirstObjectByType<PatientReport>();
        NavigationRoute route = Object.FindFirstObjectByType<NavigationRoute>();
        ECGGraphic graphic = Object.FindFirstObjectByType<ECGGraphic>();
        AmbulanceController vehicle = Object.FindFirstObjectByType<AmbulanceController>();
        Rigidbody2D body = vehicle.GetComponent<Rigidbody2D>();
        vehicle.GetComponent<KeyboardAmbulanceInput>().enabled = false;
        vehicle.enabled = false;
        int deaths = 0;
        float pickupValue = -1f;
        ecg.PatientDied += () => deaths++;
        report.PatientPickedUp += () => pickupValue = ecg.Value;
        Check(ecg && graphic && ecg.survivalTime == 180f, "저장된 ECG와 UI 참조 및 기본 180초");
        float until = Time.time + 0.3f;
        ecg.TakeDamage(10f);
        while (Time.time < until) yield return null;
        Check(!ecg.HasPatient && !ecg.IsDecaying && ecg.Value == 100f, "픽업 전 감소와 피해 없음");
        IEnumerator pickup = Pickup(report, route, body);
        while (pickup.MoveNext()) yield return null;
        Check(pickupValue == 100f && ecg.HasPatient && ecg.IsDecaying, "픽업 이벤트에서 100 시작");
        FieldInfo eventField = typeof(PatientReport).GetField("PatientPickedUp", BindingFlags.Instance | BindingFlags.NonPublic);
        float before = ecg.Value;
        ((Action)eventField.GetValue(report))?.Invoke();
        ((Action)eventField.GetValue(report))?.Invoke();
        Check(ecg.Value == before, "중복 이벤트가 값을 초기화하지 않음");
        double started = Time.timeAsDouble;
        float startValue = ecg.Value;
        Time.timeScale = 20f;
        while (Time.timeAsDouble - started < 90d) yield return null;
        double expected = startValue - (Time.timeAsDouble - started) * 100d / 180d;
        Check(Math.Abs(ecg.Value - expected) < 0.1d, "180초 기준 일정 감소와 중복 루틴 없음");
        while (ecg.Value > 0f) yield return null;
        Check(Math.Abs((Time.timeAsDouble - started) - startValue * 1.8d) < 1d, "180초 자연 감소 완료 시점");
        Time.timeScale = 1f;
        Check(ecg.State == PatientECG.ECGState.Flatline && !ecg.IsDecaying && deaths == 1, "자연 Flatline 및 사망 1회");
        ecg.TakeDamage(100f);
        ((Action)eventField.GetValue(report))?.Invoke();
        ecg.enabled = false;
        ecg.enabled = true;
        Check(ecg.Value == 0f && deaths == 1 && !ecg.IsDecaying, "사망 후 피해와 중복 이벤트 및 재활성화 차단");
        pickup = Pickup(report, route, body);
        while (pickup.MoveNext()) yield return null;
        ecg.StopDecay();
        before = ecg.Value;
        until = Time.time + 0.3f;
        while (Time.time < until) yield return null;
        Check(ecg.Value == before && !ecg.IsDecaying, "StopDecay 즉시 중지");
        ecg.enabled = false;
        ecg.enabled = true;
        ((Action)eventField.GetValue(report))?.Invoke();
        Check(!ecg.IsDecaying && ecg.Value == before, "Stop 이후 중복 이벤트와 재활성화로 재시작 안 됨");
        MethodInfo setValue = typeof(PatientECG).GetMethod("SetValue", BindingFlags.Instance | BindingFlags.NonPublic);
        setValue.Invoke(ecg, new object[] { 100d });
        Check(ecg.Value == 100f && ecg.State == PatientECG.ECGState.Green, "새 환자 초기 상태 Green");
        float greenHeight = CheckWave(graphic, "green");
        ecg.TakeDamage(40f);
        Check(ecg.Value == 60f && ecg.State == PatientECG.ECGState.Green, "60 포함 Green");
        ecg.TakeDamage(0.5f);
        Check(ecg.Value == 59.5f && ecg.State == PatientECG.ECGState.Yellow, "60 미만 Yellow");
        float yellowHeight = CheckWave(graphic, "yellow");
        ecg.TakeDamage(49.5f);
        Check(ecg.Value == 10f && ecg.State == PatientECG.ECGState.Yellow, "10 포함 Yellow");
        ecg.TakeDamage(0.5f);
        Check(ecg.Value == 9.5f && ecg.State == PatientECG.ECGState.Red, "10 미만 Red");
        float redHeight = CheckWave(graphic, "red");
        Check(greenHeight > yellowHeight && yellowHeight > redHeight, "실제 값에 따른 파동 크기 약화");
        ecg.TakeDamage(float.NaN);
        ecg.TakeDamage(float.PositiveInfinity);
        ecg.TakeDamage(-3f);
        Check(ecg.Value == 9.5f, "잘못된 Damage 무시");
        ecg.TakeDamage(1000f);
        Check(ecg.Value == 0f && ecg.State == PatientECG.ECGState.Flatline && deaths == 2, "초과 피해 Clamp와 새 환자 사망 1회");
        Check(CheckWave(graphic, "flatline") < 2f, "Flatline 일자 파동");
        report.CancelReport();
        Check(!ecg.HasPatient && !ecg.IsDecaying && ecg.Value == 100f, "미션 종료 시 이전 상태 정리");
        ecg.Configure(null);
        ecg.TakeDamage(10f);
        ecg.StopDecay();
        Check(!ecg.HasPatient && deaths == 2, "누락 참조 안전 처리");
        ecg.Configure(report);
        ecg.Configure(report);
        pickup = Pickup(report, route, body);
        while (pickup.MoveNext()) yield return null;
        Check(ecg.IsDecaying && pickupValue == 100f, "재연결 후 새 미션 정상 시작");
        ecg.survivalTime = 0f;
        yield return null;
        Check(float.IsFinite(ecg.Value), "잘못된 생존 시간 안전 처리");
        ecg.TakeDamage(100f);
        Check(deaths == 3, "재연결 후 중복 구독 없음");
        report.CancelReport();
    }

    // 생성된 UI 메쉬와 480×854 렌더링으로 파동을 확인한다
    private static float CheckWave(ECGGraphic graphic, string phase)
    {
        Canvas.ForceUpdateCanvases();
        Mesh mesh = graphic.canvasRenderer.GetMesh();
        Check(mesh.vertexCount > 0, phase + " 파동 메쉬");
        float height = mesh.bounds.size.y;
        Color32 tint = mesh.colors32[0];
        Check(phase == "green" ? tint.g > tint.r : phase == "yellow" ? tint.r > 200 && tint.g > 180 : tint.r > 200 && tint.g < 100, phase + " 파동 색상");
        float strength = phase == "green" ? 1f : phase == "yellow" ? 0.595f : phase == "red" ? 0.095f : 0f;
        Check(Mathf.Abs(tint.a / 255f - Mathf.Lerp(0.45f, 1f, strength)) < 0.01f, phase + " 실제 값 기반 파동 강도");
        Canvas canvas = graphic.canvas;
        Camera camera = Camera.main;
        RenderMode mode = canvas.renderMode;
        Camera oldCamera = canvas.worldCamera;
        RenderTexture oldTarget = camera.targetTexture;
        RenderTexture oldActive = RenderTexture.active;
        RenderTexture texture = new RenderTexture(480, 854, 24);
        Texture2D image = new Texture2D(480, 854, TextureFormat.RGB24, false);
        try
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            camera.targetTexture = texture;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0, 0, 480, 854), 0, 0);
            image.Apply();
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllBytes("Logs/ecg-" + phase + ".png", image.EncodeToPNG());
        }
        finally
        {
            canvas.renderMode = mode;
            canvas.worldCamera = oldCamera;
            camera.targetTexture = oldTarget;
            RenderTexture.active = oldActive;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(texture);
        }
        return height;
    }

    // 실패 조건을 즉시 보고한다
    private static void Check(bool condition, string label)
    {
        if (!condition)
            throw new InvalidOperationException("ECG validation failed: " + label);
        checks++;
        Debug.Log("ECG check passed: " + label);
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
