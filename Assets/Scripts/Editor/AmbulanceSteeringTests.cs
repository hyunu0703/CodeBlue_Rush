#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>격리된 2D 물리 씬에서 핸들 감도, 반대 조향, 후진과 기존 페달 동작을 검증한다.</summary>
public sealed class AmbulanceSteeringTests
{
    // 테스트가 소유한 씬과 입력/차량이다. 사용자 씬이나 물리 시뮬레이션 설정은 변경하지 않는다.
    private Scene testScene;
    private SteeringWheelControl wheel;
    private AmbulanceDriveController vehicle;
    private Rigidbody2D body;

    /// <summary>매 테스트마다 별도 물리 월드와 실제 Dynamic 차량을 생성한다.</summary>
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        // 프로젝트의 Domain Reload 설정을 존중하며 실제 플레이 수명 안에서 물리 월드를 만든다.
        yield return new EnterPlayMode(!EditorSettings.enterPlayModeOptionsEnabled
            || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0);
        testScene = SceneManager.CreateScene("SteeringTest-" + Guid.NewGuid(),
            new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        // 실제 차량과 같은 무중력, 질량과 감쇠를 사용한다.
        var car = new GameObject("Test ambulance");
        SceneManager.MoveGameObjectToScene(car, testScene);
        body = car.AddComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.mass = 2500f;
        body.linearDamping = 0f;
        body.angularDamping = 0f;
        car.AddComponent<BoxCollider2D>().size = new Vector2(0.4f, 0.9f);
        vehicle = car.AddComponent<AmbulanceDriveController>();
        InvokeCallback(vehicle, "Awake");
        InvokeCallback(vehicle, "OnEnable");
        var wheelObject = new GameObject("Test wheel", typeof(RectTransform));
        SceneManager.MoveGameObjectToScene(wheelObject, testScene);
        wheel = wheelObject.AddComponent<SteeringWheelControl>();
        InvokeCallback(wheel, "Awake");
    }

    /// <summary>테스트가 만든 씬과 오브젝트만 제거한다.</summary>
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (testScene.IsValid())
        {
            yield return SceneManager.UnloadSceneAsync(testScene);
        }
        if (Application.isPlaying)
        {
            yield return new ExitPlayMode();
        }
    }

    /// <summary>450도 범위에서 합의한 누적 조향량과 좌우 대칭을 검증한다.</summary>
    [TestCase(0f, 0f)]
    [TestCase(15f, 0.16f)]
    [TestCase(45f, 0.34f)]
    [TestCase(90f, 0.4f)]
    [TestCase(180f, 0.46f)]
    [TestCase(360f, 0.7f)]
    [TestCase(450f, 1f)]
    [TestCase(-15f, -0.16f)]
    [TestCase(-45f, -0.34f)]
    [TestCase(-90f, -0.4f)]
    [TestCase(-180f, -0.46f)]
    [TestCase(-360f, -0.7f)]
    [TestCase(-450f, -1f)]
    public void Steering_WheelAngle_ProducesAgreedStrength(float degrees, float expected)
    {
        wheel.Tick(Mathf.Sign(degrees), 0f, Mathf.Abs(degrees) / 720f);
        Assert.That(wheel.Steering, Is.EqualTo(expected).Within(0.00001f));
    }

    /// <summary>전체 범위의 단조 증가와 구간 경계의 연속성을 실제 입력 경로로 확인한다.</summary>
    [Test]
    public void Steering_IncreasingAngle_IsContinuousAndMonotonic()
    {
        // 0.1도마다 확인해 경계에서 역전되거나 조향값이 급격히 뛰는 현상을 검출한다.
        var previous = 0f;
        for (var index = 0; index < 4500; index++)
        {
            wheel.Tick(1f, 0f, 0.1f / 720f);
            Assert.That(wheel.Steering, Is.GreaterThanOrEqualTo(previous));
            Assert.That(wheel.Steering - previous, Is.LessThan(0.002f));
            previous = wheel.Steering;
        }
        Assert.That(previous, Is.EqualTo(1f).Within(0.0001f));
    }

    /// <summary>최대 각도에서 반대 키를 누른 프레임에 입력 부호가 바뀌는지 검증한다.</summary>
    [Test]
    public void Tick_OppositeKeyboardInput_ReversesWithoutUnwinding()
    {
        wheel.Tick(-1f, 4f, 1f);
        Assert.That(wheel.Steering, Is.EqualTo(-1f));
        wheel.Tick(1f, 4f, 1f / 60f);
        Assert.That(wheel.Steering, Is.GreaterThan(0f));
    }

    /// <summary>포인터 드래그로 450도까지 돌린 뒤 반대로 움직여도 같은 감도 곡선을 쓰는지 검사한다.</summary>
    [Test]
    public void OnDrag_MultipleRotations_Preserves450DegreeRange()
    {
        // 부모 좌표계의 원점을 핸들 중심으로 사용한다. 테스트 입력의 단위는 화면 좌표다.
        var parent = new GameObject("Test wheel parent", typeof(RectTransform));
        SceneManager.MoveGameObjectToScene(parent, testScene);
        wheel.transform.SetParent(parent.transform, false);
        wheel.SetTouchEnabled(true);
        var pointer = new PointerEventData(null)
        {
            pointerId = 1,
            button = PointerEventData.InputButton.Left,
            position = new Vector2(100f, 0f)
        };
        wheel.OnPointerDown(pointer);
        for (var index = 1; index <= 5; index++)
        {
            pointer.position = (Vector2)(Quaternion.Euler(0f, 0f, -90f * index) * Vector2.right) * 100f;
            wheel.OnDrag(pointer);
            if (index == 1)
            {
                Assert.That(wheel.Steering, Is.EqualTo(0.4f).Within(0.0001f));
            }
        }
        Assert.That(wheel.Steering, Is.EqualTo(1f).Within(0.0001f));
        // 반대쪽 15도까지 되돌린 뒤 출력 부호와 초반 감도를 확인한다.
        for (var index = 1; index <= 6; index++)
        {
            pointer.position = (Vector2)(Quaternion.Euler(0f, 0f, -450f + index * 77.5f) * Vector2.right) * 100f;
            wheel.OnDrag(pointer);
        }
        Assert.That(wheel.Steering, Is.EqualTo(-0.16f).Within(0.0001f));
    }

    /// <summary>전진/후진 모두 반대 입력 다음 스텝에서 회전 부호가 바뀌는지 검증한다.</summary>
    [TestCase(4f)]
    [TestCase(-2f)]
    public void FixedUpdate_OppositeSteering_ReversesYawAndPathOnNextStep(float speed)
    {
        body.linearVelocity = Vector2.up * speed;
        for (var index = 0; index < 20; index++)
        {
            Step(-0.16f, 0f, 0f);
        }
        // 입력 전환 직전의 이동 방향과 회전 방향을 기록한다.
        var previousVelocity = body.linearVelocity;
        var previousYaw = body.angularVelocity;
        Step(0.16f, 0f, 0f);
        Assert.That(body.angularVelocity * previousYaw, Is.LessThan(0f));
        Assert.That(Vector2.SignedAngle(previousVelocity, body.linearVelocity) * previousYaw, Is.LessThan(0f));
        Assert.That(Vector2.Angle(body.linearVelocity, (Vector2)body.transform.up * Mathf.Sign(speed)), Is.LessThan(1f));
    }

    /// <summary>정지 상태에서 핸들만으로 차체가 회전하지 않는지 검증한다.</summary>
    [Test]
    public void FixedUpdate_StationarySteering_DoesNotRotate()
    {
        for (var index = 0; index < 50; index++)
        {
            Step(1f, 0f, 0f);
        }
        Assert.That(body.rotation, Is.EqualTo(0f).Within(0.001f));
        Assert.That(body.linearVelocity.magnitude, Is.EqualTo(0f));
    }

    /// <summary>페달과 저항을 끈 격리 조건에서 조향이 속력을 생성하거나 잃지 않는지 검사한다.</summary>
    [Test]
    public void FixedUpdate_SteeringWithoutResistance_PreservesSpeed()
    {
        SetField(vehicle, "rollingResistance", 0f);
        SetField(vehicle, "airResistance", 0f);
        SetField(vehicle, "engineBrakeDeceleration", 0f);
        body.linearVelocity = Vector2.up * 4f;
        for (var index = 0; index < 100; index++)
        {
            Step(index < 50 ? -0.75f : 0.75f, 0f, 0f);
        }
        Assert.That(body.linearVelocity.magnitude, Is.EqualTo(4f).Within(0.001f));
    }

    /// <summary>주행 중 기어만 바꿔도 아직 진행 중인 방향에 대한 조향은 뒤집히지 않는다.</summary>
    [Test]
    public void RequestGearChange_StillMovingForward_PreservesSteeringDirection()
    {
        body.linearVelocity = Vector2.up * 4f;
        Step(0.55f, 0f, 0f);
        vehicle.RequestGearChange();
        Step(0.55f, 0f, 0f);
        Assert.That(vehicle.IsReverse, Is.True);
        Assert.That(body.angularVelocity, Is.LessThan(0f));
    }

    /// <summary>새 속도 보정이 정적 벽 충돌을 관통하지 않는지 실제 물리 월드에서 검사한다.</summary>
    [Test]
    public void FixedUpdate_DrivingIntoWall_DoesNotPassThroughCollider()
    {
        var wall = new GameObject("Test wall");
        SceneManager.MoveGameObjectToScene(wall, testScene);
        wall.transform.position = Vector3.up;
        wall.AddComponent<BoxCollider2D>().size = new Vector2(10f, 0.2f);
        body.linearVelocity = Vector2.up * 4f;
        for (var index = 0; index < 100; index++)
        {
            Step(0f, 1f, 0f);
        }
        Assert.That(body.position.y, Is.InRange(0.3f, 0.48f));
        Assert.That(body.linearVelocity.magnitude, Is.LessThan(0.1f));
    }

    /// <summary>직진 조건에서 새 조향을 켜거나 꺼도 가속/제동/엔진 제동 결과가 같은지 비교한다.</summary>
    [TestCase(1f, 0f)]
    [TestCase(0f, 1f)]
    [TestCase(0f, 0f)]
    public void FixedUpdate_StraightPedals_MatchesOriginalSpeed(float throttle, float brake)
    {
        body.linearVelocity = Vector2.up * 4f;
        for (var index = 0; index < 50; index++)
        {
            Step(0f, throttle, brake);
        }
        // 같은 출발 상태에서 기존 타이어 방식으로 재실행한다.
        var responsiveSpeed = body.linearVelocity.magnitude;
        vehicle.ClearInput();
        body.linearVelocity = Vector2.up * 4f;
        SetField(vehicle, "useResponsiveSteering", false);
        for (var index = 0; index < 50; index++)
        {
            Step(0f, throttle, brake);
        }
        Assert.That(body.linearVelocity.magnitude, Is.EqualTo(responsiveSpeed).Within(0.001f));
    }

    /// <summary>실제 컨트롤러의 고정 스텝 처리 후 격리된 Box2D 월드를 한 스텝 시뮬레이션한다.</summary>
    private void Step(float steering, float throttle, float brake)
    {
        vehicle.SetInput(steering, throttle, brake);
        InvokeCallback(vehicle, "FixedUpdate");
        Assert.That(testScene.GetPhysicsScene2D().Simulate(Time.fixedDeltaTime), Is.True);
    }

    /// <summary>EditMode에서도 실제 런타임 콜백을 호출하며 public API를 테스트 목적으로 늘리지 않는다.</summary>
    private static void InvokeCallback(MonoBehaviour target, string method)
    {
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }

    /// <summary>저장된 에셋을 건드리지 않고 테스트 인스턴스의 튜닝 값만 변경한다.</summary>
    private static void SetField(MonoBehaviour target, string field, object value)
    {
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}

/// <summary>저장된 GamePlay 씬의 직렬화 호환성을 편집 모드에서 확인한다.</summary>
public sealed class AmbulanceSteeringSceneTests
{
    /// <summary>기존 씬을 저장 없이 열어 새 필드의 기본 동작과 기존 450도 설정을 검증한다.</summary>
    [Test]
    public void GamePlay_ExistingSerializedScene_UsesResponsiveDefaultsAnd450Degrees()
    {
        var preview = EditorSceneManager.OpenPreviewScene(GamePlayDrivingSetup.ScenePath);
        try
        {
            // 이 테스트에서는 씬이나 프리팹을 저장하지 않는다.
            var foundWheel = false;
            var foundVehicle = false;
            foreach (var root in preview.GetRootGameObjects())
            {
                foreach (var control in root.GetComponentsInChildren<SteeringWheelControl>(true))
                {
                    var data = new SerializedObject(control);
                    Assert.That(data.FindProperty("maximumAngle").floatValue, Is.EqualTo(450f));
                    foundWheel = true;
                }
                foreach (var controller in root.GetComponentsInChildren<AmbulanceDriveController>(true))
                {
                    var data = new SerializedObject(controller);
                    Assert.That(data.FindProperty("useResponsiveSteering").boolValue, Is.True);
                    Assert.That(data.FindProperty("maximumTurnSpeed").floatValue, Is.EqualTo(120f));
                    foundVehicle = true;
                }
            }
            Assert.That(foundWheel && foundVehicle, Is.True);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
}
#endif
