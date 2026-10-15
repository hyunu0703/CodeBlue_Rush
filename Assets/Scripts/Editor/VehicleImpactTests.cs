#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>격리된 물리 월드에서 실제 접촉, 충격 방향, 사망 정지 및 풀 초기화를 검증한다.</summary>
public sealed class VehicleImpactTests
{
    private Scene scene;
    private Rigidbody2D ambulance;
    private Rigidbody2D traffic;
    private RoadTrafficCollision collision;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return new EnterPlayMode(!EditorSettings.enterPlayModeOptionsEnabled
            || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0);
        scene = SceneManager.CreateScene("Impact-" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var target = new GameObject("Traffic");
        SceneManager.MoveGameObjectToScene(target, scene);
        traffic = target.AddComponent<Rigidbody2D>();
        traffic.gravityScale = 0f;
        target.AddComponent<BoxCollider2D>().size = new Vector2(0.6f, 1f);
        collision = target.AddComponent<RoadTrafficCollision>();
        Invoke(collision, "ResetForSpawn");
        var player = new GameObject("Ambulance");
        SceneManager.MoveGameObjectToScene(player, scene);
        ambulance = player.AddComponent<Rigidbody2D>();
        ambulance.gravityScale = 0f;
        ambulance.mass = 2500f;
        ambulance.position = Vector2.down * 10f;
        player.AddComponent<BoxCollider2D>().size = new Vector2(0.4f, 0.9f);
        player.AddComponent<AmbulanceDriveController>();
    }

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

    [TestCase(0f)]
    [TestCase(45f)]
    [TestCase(90f)]
    [TestCase(135f)]
    [TestCase(180f)]
    [TestCase(225f)]
    [TestCase(270f)]
    [TestCase(315f)]
    public void Collision_HighSpeedFromEightDirections_BothMoveForward(float angle)
    {
        var direction = (Vector2)(Quaternion.Euler(0f, 0f, angle) * Vector2.up);
        ambulance.position = -direction * 1.4f;
        ambulance.rotation = angle;
        ambulance.linearVelocity = direction * 8f;
        for (var step = 0; step < 50 && !collision.IsReacting; step++)
        {
            scene.GetPhysicsScene2D().Simulate(0.02f);
        }
        Assert.IsTrue(collision.IsReacting, "실제 접촉에서 물리 제어로 전환되어야 한다.");
        scene.GetPhysicsScene2D().Simulate(0.02f);
        Assert.That(Vector2.Dot(traffic.linearVelocity, direction), Is.GreaterThan(0f));
        var forwardSpeed = Vector2.Dot(ambulance.linearVelocity, direction);
        var launchSpeed = Vector2.Dot(traffic.linearVelocity, direction);
        Assert.That(forwardSpeed, Is.GreaterThan(0.1f));
        Assert.That(launchSpeed, Is.GreaterThan(0.1f));
        // 정면/측면/대각선에서도 두 차량이 충돌 전 진행 방향으로 이동해야 한다.
        Assert.That(Vector2.Dot(ambulance.linearVelocity.normalized, direction), Is.GreaterThan(0.8f));
        Assert.That(Vector2.Dot(traffic.linearVelocity.normalized, direction), Is.GreaterThan(0.8f));
        Assert.That(ambulance.linearVelocity.magnitude, Is.LessThanOrEqualTo(8.01f));
    }

    /// <summary>고속 모서리 충돌에서도 두 차량은 작은 회전만 허용하고 구급차는 뒤로 튕기지 않는다.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void Collision_OffCenterAtHighSpeed_LimitsSpinAndBusRebound(bool isBus)
    {
        Invoke(collision, "ConfigureVehicle", isBus);
        Invoke(collision, "ResetForSpawn");
        ambulance.position = new Vector2(0.23f, -1.4f);
        ambulance.linearVelocity = Vector2.up * 8f;
        var drive = ambulance.GetComponent<AmbulanceDriveController>();
        var ambulanceAngle = ambulance.rotation;
        var trafficAngle = traffic.rotation;
        var ambulanceRotation = 0f;
        var trafficRotation = 0f;
        var minimumForwardSpeed = 0f;
        var maximumPosition = ambulance.position.y;
        var maximumRetreat = 0f;
        for (var step = 0; step < 250; step++)
        {
            Invoke(drive, "FixedUpdate");
            scene.GetPhysicsScene2D().Simulate(0.02f);
            ambulanceRotation += Mathf.Abs(Mathf.DeltaAngle(ambulanceAngle, ambulance.rotation));
            trafficRotation += Mathf.Abs(Mathf.DeltaAngle(trafficAngle, traffic.rotation));
            ambulanceAngle = ambulance.rotation;
            trafficAngle = traffic.rotation;
            maximumPosition = Mathf.Max(maximumPosition, ambulance.position.y);
            maximumRetreat = Mathf.Max(maximumRetreat, maximumPosition - ambulance.position.y);
            minimumForwardSpeed = Mathf.Min(minimumForwardSpeed, ambulance.linearVelocity.y);
        }
        Assert.IsTrue(collision.IsReacting);
        Assert.That(ambulanceRotation, Is.LessThan(45f));
        Assert.That(trafficRotation, Is.LessThan(45f));
        Assert.That(traffic.position.y, Is.GreaterThan(0f));
        Assert.That(maximumRetreat, Is.LessThan(0.1f));
        Assert.That(minimumForwardSpeed, Is.GreaterThan(-0.3f));
    }

    /// <summary>구급차는 전진 속도를 대부분 유지하고 상대 차량의 초기 밀림은 이전보다 약해진다.</summary>
    [TestCase(4f)]
    [TestCase(8f)]
    public void Collision_HighSpeed_ResponseScalesWithSpeed(float speed)
    {
        ambulance.position = Vector2.down * 1.4f;
        ambulance.linearVelocity = Vector2.up * speed;
        for (var step = 0; step < 100 && !collision.IsReacting; step++)
        {
            scene.GetPhysicsScene2D().Simulate(0.02f);
        }
        Assert.IsTrue(collision.IsReacting);
        Assert.That(ambulance.linearVelocity.y, Is.EqualTo(speed * 0.92f).Within(speed * 0.02f));
        Assert.That(traffic.linearVelocity.y, Is.EqualTo(speed * 0.78f).Within(speed * 0.02f));
    }

    /// <summary>충돌 직후 같은 고정 스텝부터 조향·가속·제동을 처리하고 강제 회전이나 경직이 없는지 확인한다.</summary>
    [TestCase(4f)]
    [TestCase(8f)]
    [TestCase(9f)]
    public void Collision_HighSpeed_DoesNotLockSteeringThrottleOrBrake(float speed)
    {
        var drive = HitAtSpeed(speed);
        drive.SetInput(1f, 1f, 0f);
        Invoke(drive, "FixedUpdate");
        Assert.That(ambulance.angularVelocity, Is.LessThan(-1f));
        var appliedThrottle = (float)typeof(AmbulanceDriveController)
            .GetField("appliedThrottle", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(drive);
        Assert.That(appliedThrottle, Is.GreaterThan(0f));
        drive.SetInput(-1f, 0f, 1f);
        Invoke(drive, "FixedUpdate");
        Assert.That(ambulance.angularVelocity, Is.GreaterThan(1f));
        Assert.That(drive.BrakingDeceleration, Is.GreaterThan(0f));
    }

    /// <summary>충돌 위치를 순간 변경하지 않고 여러 물리 스텝에 걸쳐 이동하며 점차 감속하는지 검증한다.</summary>
    [Test]
    public void Collision_TargetSlidesAcrossSteps_StartsFastAndSlowsGradually()
    {
        HitAtSpeed(8f);
        ambulance.position = Vector2.down * 10f;
        ambulance.linearVelocity = Vector2.zero;
        var initialSpeed = traffic.linearVelocity.magnitude;
        var previousSpeed = initialSpeed;
        var firstDistance = 0f;
        var lastDistance = 0f;
        for (var interval = 0; interval < 5; interval++)
        {
            var start = traffic.position;
            for (var step = 0; step < 10; step++)
            {
                scene.GetPhysicsScene2D().Simulate(0.02f);
                Assert.That(traffic.linearVelocity.magnitude, Is.LessThanOrEqualTo(previousSpeed + 0.001f));
                previousSpeed = traffic.linearVelocity.magnitude;
            }
            var distance = Vector2.Distance(start, traffic.position);
            if (interval == 0)
            {
                firstDistance = distance;
            }
            lastDistance = distance;
        }
        Assert.That(initialSpeed, Is.GreaterThan(6f));
        Assert.That(firstDistance, Is.GreaterThan(0.5f));
        Assert.That(lastDistance, Is.InRange(0.1f, firstDistance * 0.5f));
        Assert.That(previousSpeed, Is.GreaterThan(initialSpeed * 0.15f));
    }

    /// <summary>충돌 후 비활성화/재활성화해도 조작을 즉시 처리하는지 확인한다.</summary>
    [Test]
    public void Collision_DisableAndEnable_DoesNotLeaveControlLocked()
    {
        var drive = HitAtSpeed(8f);
        Invoke(drive, "FixedUpdate");
        scene.GetPhysicsScene2D().Simulate(0.02f);
        drive.enabled = false;
        ambulance.linearVelocity = Vector2.up;
        drive.enabled = true;
        drive.SetInput(1f, 1f, 0f);
        Invoke(drive, "FixedUpdate");
        Assert.That(ambulance.angularVelocity, Is.LessThan(-1f));
    }

    /// <summary>새 최초 접촉까지 진행하고 해당 구급차의 주행 컴포넌트를 반환한다.</summary>
    private AmbulanceDriveController HitAtSpeed(float speed)
    {
        ambulance.position = Vector2.down * 1.4f;
        ambulance.linearVelocity = Vector2.up * speed;
        for (var step = 0; step < 100 && !collision.IsReacting; step++)
        {
            scene.GetPhysicsScene2D().Simulate(0.02f);
        }
        Assert.IsTrue(collision.IsReacting);
        return ambulance.GetComponent<AmbulanceDriveController>();
    }

    /// <summary>이미 사고 처리된 차량에 고속으로 재충돌해도 두 차량 모두 앞으로 진행한다.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void Collision_PreviouslyCrashedVehicle_BothContinueForward(bool fatal)
    {
        Invoke(collision, "BeginImpact", fatal);
        ambulance.position = Vector2.down * 1.4f;
        ambulance.linearVelocity = Vector2.up * 8f;
        for (var step = 0; step < 100 && traffic.linearVelocity.y <= 0f; step++)
        {
            scene.GetPhysicsScene2D().Simulate(0.02f);
        }
        Assert.That(ambulance.linearVelocity.y, Is.GreaterThan(0f));
        Assert.That(traffic.linearVelocity.y, Is.GreaterThan(0f));
    }

    [TestCase(0.2f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void Collision_LowSpeed_PushesAndDisablesDriving(float speed)
    {
        ambulance.position = Vector2.down * 1.1f;
        ambulance.linearVelocity = Vector2.up * speed;
        for (var step = 0; step < 150 && !collision.IsReacting; step++)
        {
            scene.GetPhysicsScene2D().Simulate(0.02f);
        }
        Assert.IsTrue(collision.IsReacting);
        Assert.IsFalse(collision.IsWrecked);
        scene.GetPhysicsScene2D().Simulate(0.02f);
        Assert.That(traffic.linearVelocity.y, Is.GreaterThan(0f));
        Assert.That(ambulance.linearVelocity.y, Is.GreaterThanOrEqualTo(-0.01f));
    }

    [Test]
    public void Impact_Fatal_SettlesPermanentlyAndResetsForPool()
    {
        Invoke(collision, "BeginImpact", true);
        traffic.AddForce(Vector2.right * traffic.mass * 3f, ForceMode2D.Impulse);
        for (var step = 0; step < 500; step++)
        {
            scene.GetPhysicsScene2D().Simulate(0.02f);
        }
        Assert.IsTrue(collision.IsWrecked);
        // 사망 차량도 다시 밀 수 있어야 하므로 Dynamic 상태로 잠든다.
        Assert.That(traffic.bodyType, Is.EqualTo(RigidbodyType2D.Dynamic));
        Assert.That(traffic.linearVelocity.magnitude, Is.LessThan(0.01f));
        var position = traffic.position;
        for (var step = 0; step < 50; step++)
        {
            scene.GetPhysicsScene2D().Simulate(0.02f);
        }
        Assert.That(Vector2.Distance(traffic.position, position), Is.LessThan(0.001f));
        Invoke(collision, "ResetForSpawn");
        Assert.IsFalse(collision.IsWrecked);
        Assert.IsFalse(collision.IsReacting);
        Assert.That(traffic.bodyType, Is.EqualTo(RigidbodyType2D.Kinematic));
    }

    [Test]
    public void Impact_Nonfatal_NeverReturnsToLaneOrResumesDriving()
    {
        ambulance.position = Vector2.down * 10f;
        Invoke(collision, "BeginImpact", false);
        traffic.AddForce(Vector2.right * traffic.mass * 2f, ForceMode2D.Impulse);
        for (var step = 0; step < 1000; step++)
        {
            var previous = traffic.position;
            scene.GetPhysicsScene2D().Simulate(0.02f);
            Assert.That(Vector2.Distance(previous, traffic.position), Is.LessThan(0.1f));
        }
        Assert.IsTrue(collision.IsReacting);
        Assert.IsFalse(collision.IsWrecked);
        Assert.That(traffic.position.x, Is.GreaterThan(0.3f));
        Assert.That(traffic.bodyType, Is.EqualTo(RigidbodyType2D.Dynamic));
    }

    /// <summary>물리 스텝마다 실제 구동력을 적용해 정지한 사고 차량도 계속 밀 수 있는지 확인한다.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void Driving_AgainstStoppedCrashedVehicle_PushesItForward(bool fatal)
    {
        Invoke(collision, "BeginImpact", fatal);
        for (var step = 0; step < 100; step++)
        {
            scene.GetPhysicsScene2D().Simulate(0.02f);
        }
        var displacement = PushWithEngine();
        Assert.That(displacement, Is.GreaterThan(0.15f));
        Assert.IsTrue(collision.IsReacting);
        Assert.That(collision.IsWrecked, Is.EqualTo(fatal));
    }

    /// <summary>같은 속도/페달 입력에서 버스가 승용차보다 훨씬 적게 밀리는지 실제 접촉으로 비교한다.</summary>
    [Test]
    public void Driving_IntoBus_PushesMuchLessThanCarAndKeepsMassOnReuse()
    {
        var carDistance = PushWithEngine();
        Invoke(collision, "ConfigureVehicle", true);
        Invoke(collision, "ResetForSpawn");
        traffic.position = Vector2.zero;
        traffic.linearVelocity = Vector2.zero;
        traffic.angularVelocity = 0f;
        var busDistance = PushWithEngine();
        Assert.That(busDistance, Is.GreaterThan(0f));
        Assert.That(busDistance, Is.LessThan(carDistance * 0.25f));
        var busMass = traffic.mass;
        Invoke(collision, "ResetForSpawn");
        Assert.That(traffic.mass, Is.EqualTo(busMass));
    }

    /// <summary>접촉 후 자유 이동량이 저속에서도 충돌 속도에 비례해 증가하는지 검증한다.</summary>
    [Test]
    public void Collision_IncreasingLowSpeed_IncreasesDisplacement()
    {
        var slowDistance = MeasureImpactDisplacement(0.5f);
        Invoke(collision, "ResetForSpawn");
        traffic.position = Vector2.zero;
        traffic.linearVelocity = Vector2.zero;
        traffic.angularVelocity = 0f;
        var fastDistance = MeasureImpactDisplacement(2f);
        Assert.That(slowDistance, Is.GreaterThan(0f));
        Assert.That(fastDistance, Is.GreaterThan(slowDistance * 2f));
    }

    /// <summary>정상 주행 콜백과 물리 경계를 함께 실행한다. 회전은 고정해 전방 밀기만 비교한다.</summary>
    private float PushWithEngine()
    {
        var start = traffic.position;
        traffic.constraints = RigidbodyConstraints2D.FreezeRotation;
        ambulance.constraints = RigidbodyConstraints2D.FreezeRotation;
        ambulance.position = start + Vector2.down * 1.1f;
        ambulance.rotation = 0f;
        ambulance.linearVelocity = Vector2.up * 0.5f;
        ambulance.angularVelocity = 0f;
        var drive = ambulance.GetComponent<AmbulanceDriveController>();
        drive.ClearInput();
        drive.SetInput(0f, 1f, 0f);
        for (var step = 0; step < 250; step++)
        {
            Invoke(drive, "FixedUpdate");
            scene.GetPhysicsScene2D().Simulate(0.02f);
        }
        Assert.IsTrue(collision.IsReacting);
        return traffic.position.y - start.y;
    }

    /// <summary>실제 최초 접촉 뒤 구급차를 멀리 두어 재접촉 없이 충격량에 따른 이동만 측정한다.</summary>
    private float MeasureImpactDisplacement(float speed)
    {
        ambulance.position = Vector2.down * 1.1f;
        ambulance.linearVelocity = Vector2.up * speed;
        for (var step = 0; step < 150 && !collision.IsReacting; step++)
        {
            scene.GetPhysicsScene2D().Simulate(0.02f);
        }
        Assert.IsTrue(collision.IsReacting);
        ambulance.position = Vector2.down * 10f;
        ambulance.linearVelocity = Vector2.zero;
        for (var step = 0; step < 50; step++)
        {
            scene.GetPhysicsScene2D().Simulate(0.02f);
        }
        return traffic.position.y;
    }

    private static void Invoke(object target, string method, params object[] arguments)
    {
        target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, arguments);
    }
}
#endif
