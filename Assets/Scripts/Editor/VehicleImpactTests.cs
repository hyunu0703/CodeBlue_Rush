#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>격리된 물리 월드에서 운동량, 편심 회전, 거리 스케일과 기존 입력을 검증한다.</summary>
public sealed class VehicleImpactTests
{
    // 모든 오브젝트와 물리 시뮬레이션은 테스트 소유의 로컬 씬에 격리한다.
    private Scene scene;
    private Rigidbody2D ambulance;
    private Rigidbody2D traffic;
    private RoadTrafficCollision collision;

    /// <summary>프로젝트의 리로드 정책을 유지하면서 테스트 차량 두 대를 생성한다.</summary>
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return new EnterPlayMode(!EditorSettings.enterPlayModeOptionsEnabled
            || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0);
        scene = SceneManager.CreateScene("Impact-" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        traffic = CreateBody("Traffic", new Vector2(0.3f, 0.75f));
        collision = traffic.gameObject.AddComponent<RoadTrafficCollision>();
        Invoke(collision, "ResetForSpawn");
        ambulance = CreateBody("Ambulance", new Vector2(0.35f, 0.9f));
        ambulance.mass = 2500f;
        ambulance.position = Vector2.down * 10f;
        ambulance.gameObject.AddComponent<AmbulanceDriveController>();
    }

    /// <summary>테스트가 소유한 씬만 정리한다.</summary>
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (scene.IsValid())
        {
            yield return SceneManager.UnloadSceneAsync(scene);
        }
        if (Application.isPlaying)
        {
            yield return new ExitPlayMode();
        }
    }

    /// <summary>중력이 없는 차체와 실측 스케일의 사각형 콜라이더를 생성한다.</summary>
    private Rigidbody2D CreateBody(string name, Vector2 size)
    {
        var value = new GameObject(name);
        SceneManager.MoveGameObjectToScene(value, scene);
        var result = value.AddComponent<Rigidbody2D>();
        result.gravityScale = 0f;
        value.AddComponent<BoxCollider2D>().size = size;
        return result;
    }

    /// <summary>정면 충돌은 질량에 비례해 운동량을 나누고 운동 에너지를 증가시키지 않는다.</summary>
    [TestCase(1f, false)]
    [TestCase(4f, false)]
    [TestCase(8f, false)]
    [TestCase(8f, true)]
    public void Collision_Centred_ConservesMomentumAndLosesEnergy(float speed, bool bus)
    {
        Invoke(collision, "ConfigureVehicle", bus);
        Invoke(collision, "ResetForSpawn");
        Hit(speed, 0f);
        var momentum = ambulance.mass * ambulance.linearVelocity + traffic.mass * traffic.linearVelocity;
        Assert.That(momentum.x, Is.EqualTo(0f).Within(ambulance.mass * speed * 0.02f));
        Assert.That(momentum.y, Is.EqualTo(ambulance.mass * speed).Within(ambulance.mass * speed * 0.02f));
        var energy = ambulance.mass * ambulance.linearVelocity.sqrMagnitude + traffic.mass * traffic.linearVelocity.sqrMagnitude
            + ambulance.inertia * Mathf.Pow(ambulance.angularVelocity * Mathf.Deg2Rad, 2f)
            + traffic.inertia * Mathf.Pow(traffic.angularVelocity * Mathf.Deg2Rad, 2f);
        Assert.That(energy, Is.LessThanOrEqualTo(ambulance.mass * speed * speed * 1.01f));
        Assert.That(traffic.linearVelocity.y, Is.GreaterThan(0f));
        // 고속 접촉의 위치 보정과 순차 마찰 해결로 남는 작은 수치 오차만 허용한다(도/초).
        Assert.That(Mathf.Abs(traffic.angularVelocity), Is.LessThan(5f));
    }

    /// <summary>충돌점이 질량중심을 벗어나면 회전 방향이 좌우 대칭으로 달라진다.</summary>
    [TestCase(-0.2f)]
    [TestCase(0.2f)]
    public void Collision_Offset_ProducesSignedRotation(float offset)
    {
        Hit(4f, offset);
        Assert.That(traffic.angularVelocity * offset, Is.GreaterThan(1f));
        Assert.That(Mathf.Abs(traffic.angularVelocity), Is.GreaterThan(30f));
    }

    /// <summary>충돌 각도에 따라 접촉 법선이 달라져도 운동량과 에너지 한계를 지킨다.</summary>
    [TestCase(0f)]
    [TestCase(30f)]
    [TestCase(60f)]
    [TestCase(90f)]
    [TestCase(135f)]
    [TestCase(180f)]
    [TestCase(225f)]
    [TestCase(270f)]
    public void Collision_Angled_ConservesMomentumWithoutAddingEnergy(float angle)
    {
        // 4유닛/초는 86.4km/h다. 충돌 전 진행 방향과 운동량을 저장한다.
        var direction = (Vector2)(Quaternion.Euler(0f, 0f, angle) * Vector2.up);
        ambulance.position = -direction * 1.1f;
        ambulance.rotation = angle;
        ambulance.linearVelocity = direction * 4f;
        for (var step = 0; step < 300 && !collision.IsReacting; step++)
        {
            scene.GetPhysicsScene2D().Simulate(0.005f);
        }
        Assert.IsTrue(collision.IsReacting);
        var momentum = ambulance.mass * ambulance.linearVelocity + traffic.mass * traffic.linearVelocity;
        Assert.That((momentum - direction * (ambulance.mass * 4f)).magnitude, Is.LessThan(ambulance.mass * 4f * 0.03f));
        var energy = ambulance.mass * ambulance.linearVelocity.sqrMagnitude + traffic.mass * traffic.linearVelocity.sqrMagnitude
            + ambulance.inertia * Mathf.Pow(ambulance.angularVelocity * Mathf.Deg2Rad, 2f)
            + traffic.inertia * Mathf.Pow(traffic.angularVelocity * Mathf.Deg2Rad, 2f);
        Assert.That(energy, Is.LessThanOrEqualTo(ambulance.mass * 16f * 1.03f));
    }

    /// <summary>평행하게 스치는 접촉은 정면 충돌처럼 전방 속도를 발사하지 않는다.</summary>
    [Test]
    public void Collision_Glancing_PreservesTangentialMomentum()
    {
        ambulance.position = new Vector2(-0.7f, -1.5f);
        ambulance.linearVelocity = new Vector2(1f, 4f);
        for (var step = 0; step < 100 && !collision.IsReacting; step++)
        {
            scene.GetPhysicsScene2D().Simulate(0.005f);
        }
        Assert.IsTrue(collision.IsReacting);
        Assert.That(traffic.linearVelocity.y, Is.LessThan(1f));
    }

    /// <summary>24m/s로 미끄러지는 차체의 정지 거리를 v²/(2μg), 1유닛=6m와 비교한다.</summary>
    [TestCase(0.01f)]
    [TestCase(0.02f)]
    public void Impact_Sliding_StopsAtScaledFrictionDistance(float dt)
    {
        // 타임스텝은 마찰 적분과 물리 시뮬레이션에서 동일하게 사용하고 종료 시 복구한다.
        var previousStep = Time.fixedDeltaTime;
        try
        {
            Time.fixedDeltaTime = dt;
            Invoke(collision, "BeginImpact", true);
            traffic.constraints = RigidbodyConstraints2D.FreezeRotation;
            traffic.linearVelocity = Vector2.right * 4f;
            for (var step = 0; step < 800; step++)
            {
                Invoke(collision, "FixedUpdate");
                scene.GetPhysicsScene2D().Simulate(dt);
            }
            var expectedUnits = 24f * 24f / (2f * 0.6f * 9.80665f) / 6f;
            Assert.That(traffic.position.x, Is.EqualTo(expectedUnits).Within(0.12f));
            Assert.That(traffic.linearVelocity.magnitude, Is.LessThan(0.01f));
            Assert.That(traffic.bodyType, Is.EqualTo(RigidbodyType2D.Dynamic));
            Invoke(collision, "ResetForSpawn");
            Assert.IsFalse(collision.IsReacting);
            Assert.IsFalse(collision.IsWrecked);
            Assert.That(traffic.bodyType, Is.EqualTo(RigidbodyType2D.Kinematic));
        }
        finally
        {
            Time.fixedDeltaTime = previousStep;
        }
    }

    /// <summary>충돌 직후에도 기존 조향·페달·제동 경로가 입력을 처리한다.</summary>
    [Test]
    public void Collision_AfterImpact_DoesNotLockControls()
    {
        Hit(4f, 0f);
        var drive = ambulance.GetComponent<AmbulanceDriveController>();
        drive.SetInput(1f, 1f, 0f);
        Invoke(drive, "FixedUpdate");
        Assert.That(ambulance.angularVelocity, Is.LessThan(-1f));
        Assert.That((float)typeof(AmbulanceDriveController).GetField("appliedThrottle", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(drive), Is.GreaterThan(0f));
        drive.SetInput(-1f, 0f, 1f);
        Invoke(drive, "FixedUpdate");
        Assert.That(ambulance.angularVelocity, Is.GreaterThan(1f));
        Assert.That(drive.BrakingDeceleration, Is.GreaterThan(0f));
    }

    /// <summary>50km/h 대표 사고의 자유 미끄러짐 거리와 누적 회전을 기록하고 유한 정지를 검증한다.</summary>
    [TestCase(0f, 0f)]
    [TestCase(0f, 0.2f)]
    [TestCase(90f, 0f)]
    public void Impact_Representative50Kmh_SettlesWithoutEnergyGrowth(float angle, float offset)
    {
        // 50km/h를 초당 유닛으로 변환한다. 상대 차량은 정지한 1500kg 승용차다.
        var direction = (Vector2)(Quaternion.Euler(0f, 0f, angle) * Vector2.up);
        var sideways = new Vector2(direction.y, -direction.x);
        ambulance.position = -direction * 1.1f + sideways * offset;
        ambulance.rotation = angle;
        ambulance.linearVelocity = direction * (50f / 3.6f / 6f);
        for (var step = 0; step < 300 && !collision.IsReacting; step++)
        {
            scene.GetPhysicsScene2D().Simulate(0.005f);
        }
        Assert.IsTrue(collision.IsReacting);
        // 재접촉과 구급차 입력의 영향을 제외하고 피충돌 차량의 자유 운동만 측정한다.
        ambulance.simulated = false;
        var previousAngle = traffic.rotation;
        var rotation = 0f;
        var previousEnergy = traffic.mass * traffic.linearVelocity.sqrMagnitude
            + traffic.inertia * Mathf.Pow(traffic.angularVelocity * Mathf.Deg2Rad, 2f);
        for (var step = 0; step < 600; step++)
        {
            Invoke(collision, "FixedUpdate");
            scene.GetPhysicsScene2D().Simulate(Time.fixedDeltaTime);
            rotation += Mathf.DeltaAngle(previousAngle, traffic.rotation);
            previousAngle = traffic.rotation;
            var energy = traffic.mass * traffic.linearVelocity.sqrMagnitude
                + traffic.inertia * Mathf.Pow(traffic.angularVelocity * Mathf.Deg2Rad, 2f);
            Assert.That(energy, Is.LessThanOrEqualTo(previousEnergy + 0.01f));
            previousEnergy = energy;
        }
        Assert.That(traffic.linearVelocity.magnitude, Is.LessThan(0.01f));
        Assert.That(Mathf.Abs(traffic.angularVelocity), Is.LessThan(1f));
        TestContext.WriteLine($"50km/h angle={angle} offset={offset * 6f:F2}m displacement={traffic.position.magnitude * 6f:F2}m rotation={rotation:F2}deg");
    }

    /// <summary>입력 제어 없이 실제 최초 접촉까지만 진행해 충돌 순간의 결과를 관측한다.</summary>
    private void Hit(float speed, float offset)
    {
        ambulance.position = new Vector2(offset, -1.1f);
        ambulance.linearVelocity = Vector2.up * speed;
        for (var step = 0; step < 300 && !collision.IsReacting; step++)
        {
            scene.GetPhysicsScene2D().Simulate(0.005f);
        }
        Assert.IsTrue(collision.IsReacting);
    }

    /// <summary>실제 생성 경로가 차종별 질량을 선택하고 충돌/풀 재사용 뒤에도 관성과 함께 복원하는지 검증한다.</summary>
    [TestCase("Avante", 1250f)]
    [TestCase("Grandeur", 1620f)]
    [TestCase("SonataTaxi", 1470f)]
    [TestCase("Ray", 1085f)]
    [TestCase("Sorento", 1765f)]
    [TestCase("Sportage", 1575f)]
    [TestCase("Carnival", 2080f)]
    [TestCase("PorterII", 1755f)]
    [TestCase("Bus", 10400f)]
    public void CreateInstance_ModelMassAndInertia_PersistThroughPoolReuse(string model, float kilograms)
    {
        // 활성 주행/맵 생성 없이 실제 프리팹 복제 경로만 테스트 씬에서 실행한다.
        var owner = new GameObject("Mass test controller");
        SceneManager.MoveGameObjectToScene(owner, scene);
        owner.SetActive(false);
        var controller = owner.AddComponent<RoadTrafficController>();
        var root = new GameObject("Mass test vehicles");
        SceneManager.MoveGameObjectToScene(root, scene);
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(RoadTrafficController).GetField("vehicleRoot", flags).SetValue(controller, root);
        var prefabs = (System.Collections.Generic.List<GameObject>)typeof(RoadTrafficController)
            .GetField("spawnPrefabs", flags).GetValue(controller);
        prefabs.Add(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Car/" + model + ".prefab"));
        var instance = typeof(RoadTrafficController).GetMethod("CreateInstance", flags).Invoke(controller, new object[] { 0 });
        var created = (GameObject)instance.GetType().GetField("Root", flags).GetValue(instance);
        var modelBody = created.GetComponent<Rigidbody2D>();
        var modelCollision = created.GetComponent<RoadTrafficCollision>();
        created.SetActive(true);
        modelBody.simulated = true;
        // 비활성 복제본을 활성화한 뒤 배치해야 프리팹 Transform 동기화가 시험 위치를 덮어쓰지 않는다.
        modelBody.position = Vector2.one * 100f;
        Invoke(modelCollision, "ResetForSpawn");
        Invoke(modelCollision, "BeginImpact", true);
        Assert.That(modelBody.mass, Is.EqualTo(kilograms));
        var inertia = modelBody.inertia;
        Assert.That(inertia, Is.GreaterThan(0f));
        // 동일한 충격량은 가벼운 차에 더 큰 속도 변화를 주어야 한다. 단위는 kg·유닛/초다.
        modelBody.AddForce(Vector2.right * 1000f, ForceMode2D.Impulse);
        scene.GetPhysicsScene2D().Simulate(0.005f);
        Assert.That(modelBody.linearVelocity.x, Is.EqualTo(1000f / kilograms).Within(0.001f));
        created.SetActive(false);
        Invoke(modelCollision, "ResetForSpawn");
        created.SetActive(true);
        Invoke(modelCollision, "BeginImpact", false);
        Assert.That(modelBody.mass, Is.EqualTo(kilograms));
        Assert.That(modelBody.inertia, Is.EqualTo(inertia).Within(inertia * 0.001f));
        Assert.IsFalse(modelCollision.IsWrecked);
    }

    /// <summary>같은 속도의 구급차와 충돌하면 버스는 레이보다 덜 밀리고 구급차의 감속은 더 커진다.</summary>
    [Test]
    public void Collision_HeavyBusComparedWithRay_TransfersLessSpeedAndSlowsAmbulanceMore()
    {
        Invoke(collision, "ConfigureVehicleModel", "Ray");
        Invoke(collision, "ResetForSpawn");
        Hit(4f, 0f);
        var raySpeed = traffic.linearVelocity.y;
        var ambulanceAfterRay = ambulance.linearVelocity.y;
        Invoke(collision, "ConfigureVehicleModel", "Bus");
        Invoke(collision, "ResetForSpawn");
        traffic.position = Vector2.zero;
        traffic.rotation = 0f;
        traffic.linearVelocity = Vector2.zero;
        traffic.angularVelocity = 0f;
        ambulance.rotation = 0f;
        ambulance.angularVelocity = 0f;
        // 이전 접촉을 해제한 별도 물리 경계 뒤 두 번째 사고를 시작한다.
        ambulance.position = Vector2.down * 10f;
        ambulance.linearVelocity = Vector2.zero;
        scene.GetPhysicsScene2D().Simulate(0.005f);
        Hit(4f, 0f);
        Assert.That(traffic.linearVelocity.y, Is.LessThan(raySpeed * 0.4f));
        Assert.That(ambulance.linearVelocity.y, Is.LessThan(ambulanceAfterRay));
    }

    /// <summary>기존 내부 생명주기 메서드를 테스트 경계에서 호출한다.</summary>
    private static void Invoke(object target, string method, params object[] arguments)
    {
        target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, arguments);
    }
}
#endif
