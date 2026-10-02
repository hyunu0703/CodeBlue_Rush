using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>10단계 도로 자산과 실제 차선 변경 및 끼어들기의 Play 검증을 제공한다</summary>
public static class LaneChangeValidation
{
    private const string Key = "CodeBlueRush.LaneChangeValidation";
    private static IEnumerator run;
    private static int frame;
    private static int checks;
    private static bool errors;
    private static double deadline;
    private static double startAt;

    // 기존 규격의 분기와 합류를 가진 복수 차선 직선 자산을 작성한다
    private static void CreateRoad()
    {
        const string path = "Assets/Prefabs/City/PassingStraight.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path))
            return;
        GameObject root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/City/Straight.prefab");
        try
        {
            root.name = "Passing Straight";
            RoadChunk road = root.GetComponent<RoadChunk>();
            for (int i = 0; i < road.LaneCount; i++)
                Object.DestroyImmediate(road.GetLane(i).gameObject);
            var all = new System.Collections.Generic.List<TrafficLane>();
            TrafficLane[] entries = new TrafficLane[2];
            TrafficLane[] exits = new TrafficLane[2];
            for (int d = 0; d < 2; d++)
            {
                float sign = d == 0 ? 1f : -1f;
                TrafficLane entry = Lane(root.transform, "Entry" + d, new[] { new Vector2(1,-10)*sign, new Vector2(1,-9)*sign });
                TrafficLane exit = Lane(root.transform, "Exit" + d, new[] { new Vector2(1,9)*sign, new Vector2(1,10)*sign });
                entries[d] = entry;
                exits[d] = exit;
                all.Add(entry);
                all.Add(exit);
                TrafficLane inner = Lane(root.transform, "Inner" + d, new[] { new Vector2(1,-7)*sign, new Vector2(1,7)*sign });
                TrafficLane outer = Lane(root.transform, "Outer" + d, new[] { new Vector2(3,-7)*sign, new Vector2(3,7)*sign });
                SerializedObject a = new SerializedObject(inner);
                a.FindProperty("rightLane").objectReferenceValue = outer;
                a.ApplyModifiedPropertiesWithoutUndo();
                a = new SerializedObject(outer);
                a.FindProperty("leftLane").objectReferenceValue = inner;
                a.ApplyModifiedPropertiesWithoutUndo();
                all.Add(inner);
                all.Add(outer);
                TrafficLane[] forks = new TrafficLane[2];
                for (int side = 0; side < 2; side++)
                {
                    float x = side == 0 ? 1f : 3f;
                    TrafficLane fork = Lane(root.transform, "Fork" + d + side, Curve(new Vector2(1,-9)*sign, new Vector2(x,-7)*sign, Vector2.up*sign));
                    TrafficLane merge = Lane(root.transform, "Merge" + d + side, Curve(new Vector2(x,7)*sign, new Vector2(1,9)*sign, Vector2.up*sign));
                    TrafficLane middle = side == 0 ? inner : outer;
                    RoadSamples.SetArray(fork, "internalNext", new[] { middle });
                    RoadSamples.SetArray(middle, "internalNext", new[] { merge });
                    RoadSamples.SetArray(merge, "internalNext", new[] { exit });
                    forks[side] = fork;
                    all.Add(fork);
                    all.Add(merge);
                }
                RoadSamples.SetArray(entry, "internalNext", forks);
            }
            for (int i = 0; i < road.ConnectionCount; i++)
            {
                RoadConnection port = road.GetConnection(i);
                bool north = port.Position.y > root.transform.position.y;
                RoadSamples.SetArray(port, "incoming", new[] { entries[north ? 1 : 0] });
                RoadSamples.SetArray(port, "outgoing", new[] { exits[north ? 0 : 1] });
            }
            RoadSamples.SetArray(road, "lanes", all.ToArray());
            Transform surface = root.transform.Find("Surface");
            if (surface)
                surface.localScale = new Vector3(surface.localScale.x * 1.7f, surface.localScale.y, surface.localScale.z);
            if (!road.Validate(out string error))
                throw new InvalidOperationException(error);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // 연결 끝점에서 접선 방향을 유지하는 기존 차선 점을 만든다
    private static Vector2[] Curve(Vector2 start, Vector2 end, Vector2 direction)
    {
        Vector2[] points = new Vector2[33];
        for (int i = 0; i < points.Length; i++)
        {
            float t = i / 32f;
            float s = 1f - t;
            points[i] = s*s*s*start + 3*s*s*t*(start+direction) + 3*s*t*t*(end-direction) + t*t*t*end;
        }
        return points;
    }

    // 기존 TrafficLane 컴포넌트에 실제 도로 경로만 저장한다
    private static TrafficLane Lane(Transform parent, string name, Vector2[] points)
    {
        GameObject child = new GameObject(name, typeof(TrafficLane));
        child.transform.SetParent(parent, false);
        TrafficLane lane = child.GetComponent<TrafficLane>();
        SerializedObject data = new SerializedObject(lane);
        SerializedProperty array = data.FindProperty("points");
        array.arraySize = points.Length;
        for (int i = 0; i < points.Length; i++)
            array.GetArrayElementAtIndex(i).vector2Value = points[i];
        data.ApplyModifiedPropertiesWithoutUndo();
        return lane;
    }

    // 기존 TrafficDemo의 직선 자산만 교체한 차선 변경 데모를 저장한다
    [MenuItem("CodeBlue Rush/Lane Change/Create Demo Scene")]
    public static void CreateDemo()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        CreateRoad();
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/TrafficDemo.unity");
        CityMap map = Object.FindFirstObjectByType<CityMap>();
        SerializedObject data = new SerializedObject(map);
        data.FindProperty("straight").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/City/PassingStraight.prefab").GetComponent<RoadChunk>();
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/LaneChangeDemo.unity");
    }
    // 저장된 9단계 씬을 다시 열고 Play 검증만 실행한다
    [MenuItem("CodeBlue Rush/Lane Change/Validate Play")]
    public static void RunPlay()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Play 종료 후 실행하세요.");
        CreateDemo();
        EditorSceneManager.OpenScene("Assets/Scenes/LaneChangeDemo.unity");
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
                throw new TimeoutException("Lane change Play validation timeout");
            if (!EditorApplication.isPlaying || Time.frameCount < 2 || frame == Time.frameCount)
                return;
            frame = Time.frameCount;
            if (run == null)
                run = Validate();
            if (run.MoveNext())
                return;
            Check(!errors, "런타임 오류와 NullReference 없음");
            Debug.Log("Lane change play validation passed: " + checks + " checks");
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

    // 실제 인접 차선에서 변경 상태와 끼어들기 조건을 검증한다
    private static IEnumerator Validate()
    {
        TrafficSpawner spawner = Object.FindFirstObjectByType<TrafficSpawner>();
        CityMap map = spawner.Map;
        AmbulanceController controller = Object.FindFirstObjectByType<AmbulanceController>();
        controller.enabled = false;
        controller.GetComponent<KeyboardAmbulanceInput>().enabled = false;
        Rigidbody2D ambulance = controller.GetComponent<Rigidbody2D>();
        Camera camera = Camera.main;
        camera.GetComponent<AmbulanceCamera>().enabled = false;
        Set(spawner, "attempts", 0f, true);
        Check(map.Validate(out _), "새 실제 도로 자산과 도시 연결 유효");
        TrafficLane outer = null;
        for (int i = 0; i < map.LaneCount; i++)
            if (map.GetLane(i).LeftLane && map.GetLane(i).Length > 10f)
            {
                outer = map.GetLane(i);
                break;
            }
        Check(outer, "같은 방향 두 차선 확보");
        TrafficLane inner = outer.LeftLane;
        FrameLane(outer, controller.transform, camera);
        Check(spawner.TrySpawn(outer, 2f, out VehicleAI car), "기존 생성 API 재사용");
        Set(car, "cruiseSpeed", 2f);
        Set(car, "decisionInterval", 100f);
        float until = Time.time + 1.2f;
        while (Time.time < until) yield return null;
        Check(!car.TryChangeLane(null), "인접 Lane 없음 거절");
        inner.TryProject(car.transform.position, out float along, out _);
        Check(spawner.TrySpawn(inner, along + 2.5f, out VehicleAI block), "목표 차선 앞 차량 생성");
        Set(block, "cruiseSpeed", 0f);
        Check(!car.TryChangeLane(inner), "앞 안전 공간 부족 거절");
        spawner.Release(block);
        Check(spawner.TrySpawn(inner, Mathf.Max(0f, along - 2.5f), out block), "목표 차선 뒤 차량 생성");
        Set(block, "cruiseSpeed", 0f);
        Check(!car.TryChangeLane(inner), "뒤 안전 공간 부족 거절");
        spawner.Release(block);
        Vector3 previous = car.transform.position;
        Check(car.TryChangeLane(inner) && car.IsChangingLane && car.Lane == outer, "안전할 때 변경 시작 및 원래 Lane 유지");
        Check(!car.TryChangeLane(inner), "변경 중 중복 요청 차단");
        bool smooth = true;
        until = Time.time + 3f;
        while (car.IsChangingLane && Time.time < until)
        {
            yield return null;
            smooth &= Vector3.Distance(previous, car.transform.position) < 0.5f;
            previous = car.transform.position;
        }
        Check(!car.IsChangingLane && car.Lane == inner && smooth, "순간이동 없이 전진 횡이동 후 Lane 확정");
        Check(!car.TryChangeLane(outer), "Cooldown 좌우 왕복 차단");
        spawner.Release(car);
        FrameLane(outer, controller.transform, camera);
        Check(spawner.TrySpawn(outer, 3f, out car), "안전 상실 시험 차량");
        Set(car, "cruiseSpeed", 2f);
        until = Time.time + 1.2f;
        while (Time.time < until) yield return null;
        Check(car.TryChangeLane(inner), "안전 상실 시험 변경 시작");
        until = Time.time + 0.2f;
        while (Time.time < until) yield return null;
        inner.TrySample(car.Distance + 2.5f, out Vector3 blockedPoint, out _);
        ambulance.transform.position = blockedPoint;
        ambulance.linearVelocity = Vector2.zero;
        Physics2D.SyncTransforms();
        until = Time.time + 0.15f;
        while (Time.time < until) yield return null;
        previous = car.transform.position;
        until = Time.time + 0.2f;
        while (Time.time < until) yield return null;
        Check(car.IsChangingLane && car.Speed == 0f && Vector3.Distance(previous, car.transform.position) < 0.01f, "목표 안전 상실 시 이동 정지");
        FrameLane(outer, controller.transform, camera);
        until = Time.time + 2f;
        while (car.IsChangingLane && Time.time < until) yield return null;
        Check(car.Lane == inner && !car.IsChangingLane, "안전 복구 후 변경 재개");
        spawner.Release(car);
        FrameLane(outer, controller.transform, camera);
        Check(spawner.TrySpawn(outer, 4f, out car), "끼어들기 시험 차량");
        Set(car, "cruiseSpeed", 2f);
        Set(car, "decisionInterval", 0.2f);
        Set(car, "cutInChance", 0f);
        until = Time.time + 1.2f;
        while (Time.time < until) yield return null;
        inner.TrySample(1f, out Vector3 ambPoint, out Vector3 forward);
        ambulance.transform.position = ambPoint;
        ambulance.linearVelocity = Vector2.zero;
        Physics2D.SyncTransforms();
        Check(!car.TryCutIn(), "정차 구급차 끼어들기 거절");
        until = Time.time + 0.21f;
        while (Time.time < until) yield return null;
        ambulance.linearVelocity = forward * 3f;
        Check(!car.TryCutIn(), "확률 0이면 끼어들지 않음");
        until = Time.time + 0.21f;
        while (Time.time < until) yield return null;
        ambulance.transform.position = ambPoint;
        Physics2D.SyncTransforms();
        Set(car, "cutInChance", 1f);
        FieldInfo attemptTime = typeof(VehicleAI).GetField("nextCutIn", BindingFlags.Instance | BindingFlags.NonPublic);
        outer.TrySample(1f, out Vector3 wrongLanePoint, out _);
        ambulance.transform.position = wrongLanePoint;
        Physics2D.SyncTransforms();
        attemptTime.SetValue(car, 0f);
        Check(!car.TryCutIn(), "구급차가 대상 인접 차선이 아니면 거절");
        inner.TrySample(car.Distance - 1f, out Vector3 tooClose, out _);
        ambulance.transform.position = tooClose;
        Physics2D.SyncTransforms();
        attemptTime.SetValue(car, 0f);
        Check(!car.TryCutIn(), "구급차 뒤쪽 안전 간격 부족 거절");
        ambulance.transform.position = ambPoint;
        ambulance.linearVelocity = forward * 20f;
        Physics2D.SyncTransforms();
        attemptTime.SetValue(car, 0f);
        Check(!car.TryCutIn(), "상대 속도로 변경 중 간격 소진이 예상되면 거절");
        ambulance.linearVelocity = forward * 3f;
        attemptTime.SetValue(car, 0f);
        Check(car.TryCutIn() && car.TargetLane == inner, "실제 구급차 차선 속도 간격 조건에서 확률 끼어들기");
        ambulance.linearVelocity = Vector2.zero;
        until = Time.time + 2f;
        while (car.IsChangingLane && Time.time < until) yield return null;
        Check(car.Lane == inner, "안전한 끼어들기 완료");
        spawner.Release(car);
        Check(!car.TargetLane && !car.Lane, "풀 반환 시 차선 변경 참조 정리");
        FrameLane(outer, controller.transform, camera);
        Check(spawner.TrySpawn(outer, 4f, out car), "자동 끼어들기 시험 차량");
        Set(car, "cruiseSpeed", 2f);
        Set(car, "cutInChance", 0f);
        Set(car, "decisionInterval", 0.2f);
        until = Time.time + 1.2f;
        while (Time.time < until) yield return null;
        ambulance.transform.position = ambPoint;
        ambulance.linearVelocity = forward * 3f;
        Physics2D.SyncTransforms();
        Set(car, "cutInChance", 1f);
        until = Time.time + 2f;
        while (!car.IsChangingLane && Time.time < until) yield return null;
        Check(car.IsChangingLane && car.TargetLane == inner, "주기적 판단에서 자동 끼어들기 시작");
        ambulance.linearVelocity = Vector2.zero;
        spawner.Release(car);
        FrameLane(outer, controller.transform, camera);
        Check(spawner.TrySpawn(outer, 3f, out car) && spawner.TrySpawn(outer, 12f, out block), "일반 차선 변경 교통 상황 구성");
        Set(car, "cruiseSpeed", 2f);
        Set(car, "cutInChance", 0f);
        Set(car, "decisionInterval", 0.2f);
        Set(block, "cruiseSpeed", 0f);
        until = Time.time + 3f;
        while (!car.IsChangingLane && Time.time < until) yield return null;
        Check(car.IsChangingLane && car.TargetLane == inner, "앞차 정체가 있을 때만 일반 차선 변경 시작");
        spawner.Release(block);
        spawner.Release(car);
        spawner.enabled = false;
    }
    // 실패 조건을 즉시 보고한다
    private static void Check(bool condition, string label)
    {
        if (!condition)
            throw new InvalidOperationException("Lane change validation failed: " + label);
        checks++;
        Debug.Log("Lane change check passed: " + label);
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



