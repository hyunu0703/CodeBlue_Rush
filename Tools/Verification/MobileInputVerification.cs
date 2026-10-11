// Copy into Assets/Editor in an isolated project, then run with
// -batchmode -nographics -executeMethod MobileInputVerification.Run (without -quit)
// These are native Unity component checks, not physical-browser touch tests.
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

public static class MobileInputVerification
{
    private static int passed;

    public static void Run()
    {
        EditorSceneManager.OpenScene(GamePlayDrivingSetup.ScenePath);
        EditorApplication.playModeStateChanged += HandlePlayMode;
        EditorApplication.isPlaying = true;
    }

    private static void HandlePlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode)
        {
            return;
        }
        EditorApplication.playModeStateChanged -= HandlePlayMode;
        try
        {
            RunChecks();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void RunChecks()
    {
        passed = 0;
        var scene = SceneManager.GetActiveScene();
        // Replacing InputSystem's non-persistent defaults destroys them; retain a value snapshot.
        InputSettings originalSettings = UnityEngine.Object.Instantiate(InputSystem.settings);
        InputSettings testSettings = UnityEngine.Object.Instantiate(originalSettings);
        // Headless verification has no focused Game view. Route synthetic events to player state.
        testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = testSettings;
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Touchscreen touchscreen = InputSystem.AddDevice<Touchscreen>();
        float previousTimeScale = Time.timeScale;
        try
        {
            Time.timeScale = 1f;
            Check(GamePlayDrivingSetup.ConfigureScene(scene), "GamePlay setup");
            var input = UnityEngine.Object.FindFirstObjectByType<AmbulanceDriveInput>();
            var vehicle = UnityEngine.Object.FindFirstObjectByType<AmbulanceDriveController>();
            var wheel = UnityEngine.Object.FindFirstObjectByType<SteeringWheelControl>();
            var gear = UnityEngine.Object.FindFirstObjectByType<GearShiftControl>();
            var accelerator = Get<HoldPedalControl>(input, "accelerator");
            var brake = Get<HoldPedalControl>(input, "brake");
            // Explicit boundaries keep these checks independent of frame ordering.
            Invoke(vehicle, "Awake");
            Invoke(vehicle, "OnEnable");
            Invoke(wheel, "Awake");
            Invoke(gear, "Awake");
            Invoke(input, "Start");
            var events = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            var finger = new ExtendedPointerEventData(events)
            {
                device = touchscreen, pointerId = 101, button = PointerEventData.InputButton.Left
            };
            var secondFinger = new ExtendedPointerEventData(events)
            {
                device = touchscreen, pointerId = 102, button = PointerEventData.InputButton.Left
            };

            Check(!AmbulanceDriveInput.IsMobileBuild, "Regression runs with mobile-platform flag false");
            accelerator.OnPointerDown(finger);
            Invoke(input, "Update");
            Check(Get<float>(vehicle, "throttle") == 1f, "Touch accelerates even when platform flag is false");
            accelerator.OnPointerUp(secondFinger);
            Check(accelerator.Value == 1f, "Other finger cannot release pedal");
            brake.OnPointerDown(secondFinger);
            Invoke(input, "Update");
            Check(Get<float>(vehicle, "brake") == 1f && Get<float>(vehicle, "throttle") == 0f,
                "Two fingers: brake overrides held accelerator");
            brake.OnPointerUp(secondFinger);
            accelerator.OnPointerExit(finger);
            Invoke(input, "Update");
            Check(Get<float>(vehicle, "throttle") == 0f && Get<float>(vehicle, "brake") == 0f,
                "Release and exit clear pedals");

            var browserPointer = new PointerEventData(events)
            {
                pointerId = -1, button = PointerEventData.InputButton.Left
            };
            accelerator.OnPointerDown(browserPointer);
            Invoke(input, "Update");
            Check(Get<float>(vehicle, "throttle") == 1f, "Browser primary pointer works without Touchscreen metadata");
            accelerator.OnPointerUp(browserPointer);
            browserPointer.button = PointerEventData.InputButton.Right;
            accelerator.OnPointerDown(browserPointer);
            Check(accelerator.Value == 0f, "Secondary mouse button is ignored");

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.D));
            InputSystem.Update();
            Invoke(input, "Update");
            Check(Get<float>(vehicle, "throttle") == 1f, "PC W accelerates with touch enabled");
            wheel.ResetInput();
            wheel.Tick(1f, 0f, 0.1f);
            Check(wheel.Steering > 0f, "PC D steers with touch enabled");
            wheel.Tick(-1f, 0f, 0.2f);
            Check(wheel.Steering < 0f, "PC A steers with touch enabled");

            wheel.ResetInput();
            wheel.OnPointerDown(finger);
            wheel.Tick(1f, 0f, 0.1f);
            Check(wheel.Steering == 0f, "Held touch wheel has priority over keyboard");
            wheel.OnPointerUp(secondFinger);
            Check(Get<bool>(wheel, "isHeld"), "Other finger cannot release wheel");
            wheel.OnPointerUp(finger);
            wheel.Tick(1f, 0f, 0.1f);
            Check(wheel.Steering > 0f, "Keyboard steering resumes after touch release");

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.Space));
            InputSystem.Update();
            Invoke(input, "Update");
            Check(Get<float>(vehicle, "brake") == 1f && Get<float>(vehicle, "throttle") == 0f,
                "PC Space brake overrides W");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            Invoke(input, "Update");
            gear.OnPointerClick(finger);
            Invoke(input, "Update");
            Check(Get<bool>(vehicle, "isGearChangePending"), "Touch gear change reaches vehicle");
            Invoke(vehicle, "FixedUpdate");
            Check(vehicle.IsReverse, "Touch shift applied at physics boundary");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
            InputSystem.Update();
            Invoke(input, "Update");
            Invoke(vehicle, "FixedUpdate");
            Check(!vehicle.IsReverse, "PC E changes gear at physics boundary");

            accelerator.OnPointerDown(finger);
            Invoke(input, "OnApplicationFocus", false);
            Check(accelerator.Value == 0f && Get<float>(vehicle, "throttle") == 0f,
                "Focus loss clears touch and vehicle input");
            accelerator.OnPointerDown(finger);
            Check(accelerator.Value == 0f, "Touch blocked while unfocused");
            Invoke(input, "OnApplicationFocus", true);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            InputSystem.Update();
            Invoke(input, "Update");
            Check(Get<float>(vehicle, "throttle") == 0f, "Focus return requires held-key release");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            Invoke(input, "Update");
            accelerator.OnPointerDown(finger);
            Invoke(input, "Update");
            Check(Get<float>(vehicle, "throttle") == 1f, "Fresh touch works after focus return");

            Time.timeScale = 0f;
            Invoke(input, "Update");
            Check(accelerator.Value == 0f && Get<float>(vehicle, "throttle") == 0f,
                "Pause clears input");
            Time.timeScale = 1f;
            Invoke(input, "Update");
            Check(accelerator.Value == 0f, "Resume does not replay held touch");
            accelerator.OnPointerDown(finger);
            input.enabled = false;
            Invoke(input, "OnDisable");
            Check(accelerator.Value == 0f, "Disable clears touch");
            input.enabled = true;
            Invoke(input, "OnEnable");
            accelerator.OnPointerDown(finger);
            Invoke(input, "Update");
            Check(Get<float>(vehicle, "throttle") == 1f, "Re-enable accepts fresh touch");

            Debug.Log("MOBILE_INPUT_VERIFICATION: " + passed + " passed, 0 failed");
        }
        finally
        {
            Time.timeScale = previousTimeScale;
            InputSystem.RemoveDevice(keyboard);
            InputSystem.RemoveDevice(touchscreen);
            InputSystem.settings = originalSettings;
            if (testSettings != null)
            {
                UnityEngine.Object.DestroyImmediate(testSettings);
            }
        }
    }

    public static void BuildWebGL()
    {
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Start.unity", GamePlayDrivingSetup.ScenePath },
            locationPathName = Path.GetFullPath("docs"),
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new Exception("WebGL build failed: " + report.summary.result);
        }
        Debug.Log("MOBILE_INPUT_WEBGL_BUILD: Passed");
    }

    private static T Get<T>(object target, string name)
    {
        return (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    }

    private static void Invoke(object target, string name, params object[] arguments)
    {
        target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, arguments);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception("FAIL: " + message);
        }
        passed++;
        Debug.Log("PASS: " + message);
    }
}
