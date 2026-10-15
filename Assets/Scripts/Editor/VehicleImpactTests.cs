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
    public void Collision_HighSpeedFromEightDirections_PushesTargetAndRecoils(float angle)
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
        Assert.That(Vector2.Dot(ambulance.linearVelocity, direction), Is.LessThan(0f));
    }

    [Test]
    public void Collision_LowSpeed_DoesNotDisableTraffic()
    {
        ambulance.position = Vector2.down * 1.1f;
        ambulance.linearVelocity = Vector2.up;
        for (var step = 0; step < 100; step++)
        {
            scene.GetPhysicsScene2D().Simulate(0.02f);
        }
        Assert.IsFalse(collision.IsReacting);
        Assert.IsFalse(collision.IsWrecked);
    }

    [Test]
    public void Impact_Fatal_SettlesPermanentlyAndResetsForPool()
    {
        Invoke(collision, "BeginImpact", true);
        traffic.AddForce(Vector2.right * traffic.mass * 3f, ForceMode2D.Impulse);
        for (var step = 0; step < 500; step++)
        {
            Invoke(collision, "Tick", 0.02f);
            scene.GetPhysicsScene2D().Simulate(0.02f);
        }
        Assert.IsTrue(collision.IsWrecked);
        Assert.That(traffic.bodyType, Is.EqualTo(RigidbodyType2D.Static));
        var position = traffic.position;
        for (var step = 0; step < 50; step++)
        {
            Invoke(collision, "Tick", 0.02f);
            scene.GetPhysicsScene2D().Simulate(0.02f);
        }
        Assert.That(traffic.position, Is.EqualTo(position));
        Invoke(collision, "ResetForSpawn");
        Assert.IsFalse(collision.IsWrecked);
        Assert.IsFalse(collision.IsReacting);
        Assert.That(traffic.bodyType, Is.EqualTo(RigidbodyType2D.Kinematic));
    }

    [Test]
    public void Impact_Nonfatal_ReturnsToLaneWithoutTeleport()
    {
        ambulance.position = Vector2.down * 10f;
        Invoke(collision, "BeginImpact", false);
        traffic.AddForce(Vector2.right * traffic.mass * 2f, ForceMode2D.Impulse);
        for (var step = 0; step < 1000 && collision.IsReacting; step++)
        {
            var previous = traffic.position;
            Invoke(collision, "Tick", 0.02f);
            scene.GetPhysicsScene2D().Simulate(0.02f);
            Assert.That(Vector2.Distance(previous, traffic.position), Is.LessThan(0.1f));
        }
        Assert.IsFalse(collision.IsReacting);
        Assert.That(traffic.position.magnitude, Is.LessThan(0.03f));
    }

    private static void Invoke(object target, string method, params object[] arguments)
    {
        target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, arguments);
    }
}
#endif
