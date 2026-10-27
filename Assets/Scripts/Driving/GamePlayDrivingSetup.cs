using TMPro;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>Installs only the named GamePlay scene. Edit-time persistence is an explicit, undoable menu action.</summary>
public static class GamePlayDrivingSetup
{
    public const string ScenePath = "Assets/Scenes/GamePlay.unity";
    private const string UndoLabel = "Set Up GamePlay Driving";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistration()
    {
        // Domain Reload is disabled in this project. Remove the previous session's external event subscription.
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        Application.quitting -= ResetRegistration;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        if (AmbulanceDriveInput.IsMobileBuild)
        {
            Screen.orientation = ScreenOrientation.Portrait;
        }

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        Application.quitting -= ResetRegistration;
        Application.quitting += ResetRegistration;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ConfigureInitialScene()
    {
        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            ConfigureScene(SceneManager.GetSceneAt(index));
        }
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ConfigureScene(scene);
    }

    public static bool ConfigureScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
        {
            return false;
        }

        GameObject ambulance = FindRoot(scene, "Ambulance");
        GameObject canvasObject = FindRoot(scene, "Canvas");
        GameObject cameraObject = FindRoot(scene, "Main Camera");
        GameObject eventObject = FindRoot(scene, "EventSystem");
        if (ambulance == null || canvasObject == null || cameraObject == null || eventObject == null)
        {
            Debug.LogError("GamePlay driving setup requires roots named Ambulance, Canvas, Main Camera, and EventSystem.");
            return false;
        }

        // A saved setup is already wired; never replace Inspector tuning or duplicate components.
        if (canvasObject.GetComponent<AmbulanceDriveInput>() != null)
        {
            return true;
        }

        Transform wheelTransform = canvasObject.transform.Find("SteeringWheel");
        Transform acceleratorTransform = canvasObject.transform.Find("Accelerator");
        Transform brakeTransform = canvasObject.transform.Find("Brake");
        var spriteRenderer = ambulance.GetComponent<SpriteRenderer>();
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        var inputModule = eventObject.GetComponent<InputSystemUIInputModule>();
        if (wheelTransform == null || acceleratorTransform == null || brakeTransform == null
            || spriteRenderer == null || spriteRenderer.sprite == null || scaler == null || inputModule == null
            || wheelTransform.GetComponent<Image>() == null || acceleratorTransform.GetComponent<Image>() == null
            || brakeTransform.GetComponent<Image>() == null || TMP_Settings.defaultFontAsset == null)
        {
            Debug.LogError("GamePlay driving setup requires the existing wheel/pedal Images, Ambulance sprite, CanvasScaler, "
                + "InputSystemUIInputModule, and TMP default font. Restore missing references before running setup.", canvasObject);
            return false;
        }

        Rigidbody2D body = ambulance.GetComponent<Rigidbody2D>();
        if (body == null)
        {
            body = AddComponent<Rigidbody2D>(ambulance);
            Record(body);
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = 0f;
            body.mass = 2500f;
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }
        else if (body.bodyType != RigidbodyType2D.Dynamic || !body.simulated || body.gravityScale != 0f)
        {
            Debug.LogError("Existing Ambulance Rigidbody2D must be Dynamic, simulated, and have Gravity Scale 0. "
                + "Setup preserves existing body settings; adjust these values explicitly.", ambulance);
            return false;
        }

        var collider = ambulance.GetComponent<BoxCollider2D>();
        if (collider == null)
        {
            collider = AddComponent<BoxCollider2D>(ambulance);
            Record(collider);
            collider.size = (Vector2)spriteRenderer.sprite.bounds.size * 0.88f;
            collider.offset = spriteRenderer.sprite.bounds.center;
            collider.isTrigger = false;
        }

        var vehicle = ambulance.GetComponent<AmbulanceDriveController>();
        if (vehicle == null)
        {
            vehicle = AddComponent<AmbulanceDriveController>(ambulance);
            Record(vehicle);
        }

        Record(scaler);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(480f, 854f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        Record(inputModule);
        inputModule.pointerBehavior = UIPointerBehavior.AllPointersAsIs;

        var safeArea = CreateRect("DrivingSafeArea", canvasObject.transform);
        Stretch(safeArea);
        PlaceControl((RectTransform)wheelTransform, safeArea, new Vector2(0f, 0f), new Vector2(110.4f, 106.1f));
        PlaceControl((RectTransform)acceleratorTransform, safeArea, new Vector2(1f, 0f), new Vector2(-65f, 79.9f));
        PlaceControl((RectTransform)brakeTransform, safeArea, new Vector2(1f, 0f), new Vector2(-171f, 58.6f));

        var wheel = AddComponent<SteeringWheelControl>(wheelTransform.gameObject);
        var accelerator = AddComponent<HoldPedalControl>(acceleratorTransform.gameObject);
        var brake = AddComponent<HoldPedalControl>(brakeTransform.gameObject);

        var gearRect = CreateRect("GearShift", safeArea);
        gearRect.anchorMin = gearRect.anchorMax = new Vector2(1f, 0f);
        gearRect.pivot = new Vector2(0.5f, 0.5f);
        gearRect.anchoredPosition = new Vector2(-65f, 181.9f);
        gearRect.sizeDelta = new Vector2(100f, 60f);
        var background = AddComponent<Image>(gearRect.gameObject);
        background.color = new Color(0.08f, 0.12f, 0.18f, 0.95f);
        background.raycastTarget = true;

        var labelRect = CreateRect("Label", gearRect);
        Stretch(labelRect);
        var label = AddComponent<TextMeshProUGUI>(labelRect.gameObject);
        label.font = TMP_Settings.defaultFontAsset;
        label.fontSize = 24f;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.text = "<color=#FFD66B>D</color> / R";
        var gear = AddComponent<GearShiftControl>(gearRect.gameObject);
        Record(gear);
        gear.Configure(label);

        if (cameraObject.GetComponent<AmbulanceCameraFollow>() == null)
        {
            var follow = AddComponent<AmbulanceCameraFollow>(cameraObject);
            Record(follow);
            follow.Configure(ambulance.transform);
        }

        var input = AddComponent<AmbulanceDriveInput>(canvasObject);
        Record(input);
        input.Configure(vehicle, wheel, accelerator, brake, gear, safeArea);
        return true;
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name)
            {
                return root;
            }
        }
        return null;
    }

    private static T AddComponent<T>(GameObject target) where T : Component
    {
        T existing = target.GetComponent<T>();
        if (existing != null)
        {
            return existing;
        }
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            return Undo.AddComponent<T>(target);
        }
#endif
        return target.AddComponent<T>();
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var created = new GameObject(name, typeof(RectTransform));
        created.layer = parent.gameObject.layer;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Undo.RegisterCreatedObjectUndo(created, UndoLabel);
        }
#endif
        created.transform.SetParent(parent, false);
        return (RectTransform)created.transform;
    }

    private static void Record(Object target)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Undo.RecordObject(target, UndoLabel);
        }
#endif
    }

    private static void PlaceControl(RectTransform rect, RectTransform parent, Vector2 anchor, Vector2 position)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Undo.SetTransformParent(rect, parent, UndoLabel);
        }
        else
#endif
        {
            rect.SetParent(parent, false);
        }

        Record(rect);
        rect.localScale = Vector3.one;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
