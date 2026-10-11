using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class AmbulanceDriveInput : MonoBehaviour
{
    [SerializeField] private AmbulanceDriveController vehicle;
    [SerializeField] private SteeringWheelControl wheel;
    [SerializeField] private HoldPedalControl accelerator;
    [SerializeField] private HoldPedalControl brake;
    [SerializeField] private GearShiftControl gear;
    [SerializeField] private RectTransform safeAreaRoot;

    private Rect lastSafeArea;
    private int lastScreenWidth;
    private int lastScreenHeight;
    private bool isInitialized;
    private bool hasFocus = true;
    private bool isApplicationPaused;
    private bool requireKeyboardRelease;

    public static bool IsMobileBuild
    {
        get
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            return true;
#elif UNITY_WEBGL && !UNITY_EDITOR
            // Web builds use the same binary on desktop and mobile; detect the browser at runtime.
            return Application.isMobilePlatform;
#else
            return false;
#endif
        }
    }

    private void Start()
    {
        Initialize();
    }

    private void OnEnable()
    {
        if (isInitialized)
        {
            Initialize();
        }
    }

    private void OnDisable()
    {
        ClearInput();
        SetTouchEnabled(false);
    }

    private void Update()
    {
        if (!isInitialized)
        {
            return;
        }

        UpdateSafeArea();
        bool canDrive = hasFocus && !isApplicationPaused && Time.timeScale > 0f && vehicle != null && vehicle.IsReady;
        // Browser/platform identification must not gate real touchscreen input.
        SetTouchEnabled(canDrive);
        if (!canDrive)
        {
            ClearInput();
            if (gear != null && gear.isActiveAndEnabled)
            {
                gear.Display(vehicle != null && vehicle.IsReverse, false);
            }
            return;
        }

        float throttleInput = accelerator != null ? accelerator.Value : 0f;
        float brakeInput = brake != null ? brake.Value : 0f;
        float steeringInput = 0f;
        bool shiftRequested = gear != null && gear.ConsumePress();
        // Keep keyboard support on hybrid devices and mobile browsers with a keyboard.
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            bool anyHeld = keyboard.wKey.isPressed || keyboard.aKey.isPressed || keyboard.dKey.isPressed
                || keyboard.spaceKey.isPressed || keyboard.eKey.isPressed;
            if (requireKeyboardRelease)
            {
                requireKeyboardRelease = anyHeld;
            }
            else
            {
                throttleInput = Mathf.Max(throttleInput, keyboard.wKey.isPressed ? 1f : 0f);
                brakeInput = Mathf.Max(brakeInput, keyboard.spaceKey.isPressed ? 1f : 0f);
                steeringInput = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                shiftRequested |= keyboard.eKey.wasPressedThisFrame;
            }
        }

        if (wheel != null && wheel.isActiveAndEnabled)
        {
            wheel.Tick(steeringInput, vehicle.Speed, Time.deltaTime);
        }

        vehicle.SetInput(wheel != null && wheel.isActiveAndEnabled ? wheel.Steering : 0f, throttleInput, brakeInput);
        if (shiftRequested)
        {
            vehicle.RequestGearChange();
        }

        if (gear != null && gear.isActiveAndEnabled)
        {
            gear.Display(vehicle.IsReverse, vehicle.IsStopped);
        }
    }

    private void OnApplicationFocus(bool value)
    {
        hasFocus = value;
        if (!value)
        {
            ClearInput();
            SetTouchEnabled(false);
        }
    }

    private void OnApplicationPause(bool value)
    {
        isApplicationPaused = value;
        if (value)
        {
            ClearInput();
            SetTouchEnabled(false);
        }
    }

    public void Configure(AmbulanceDriveController vehicle, SteeringWheelControl wheel,
        HoldPedalControl accelerator, HoldPedalControl brake, GearShiftControl gear, RectTransform safeAreaRoot)
    {
        this.vehicle = vehicle;
        this.wheel = wheel;
        this.accelerator = accelerator;
        this.brake = brake;
        this.gear = gear;
        this.safeAreaRoot = safeAreaRoot;
    }

    private void Initialize()
    {
        if (vehicle == null || wheel == null || accelerator == null || brake == null || gear == null || safeAreaRoot == null)
        {
            Debug.LogError("AmbulanceDriveInput requires the Ambulance, wheel, pedals, D/R control, and safe area references.", this);
            enabled = false;
            return;
        }

        bool isFirstInitialization = !isInitialized;
        isInitialized = true;
        ClearInput();
        // The initial entry may accept held keys; re-enabling still requires release for safety.
        requireKeyboardRelease = !isFirstInitialization;
        SetTouchEnabled(hasFocus && !isApplicationPaused && Time.timeScale > 0f && vehicle.IsReady);
        gear.Display(vehicle.IsReverse, vehicle.IsStopped);
        UpdateSafeArea();
    }

    private void ClearInput()
    {
        requireKeyboardRelease = true;
        if (vehicle != null)
        {
            vehicle.ClearInput();
        }
        if (wheel != null)
        {
            wheel.ResetInput();
        }
        if (accelerator != null)
        {
            accelerator.ResetInput();
        }
        if (brake != null)
        {
            brake.ResetInput();
        }
        if (gear != null)
        {
            gear.ResetInput();
        }
    }

    private void SetTouchEnabled(bool value)
    {
        if (wheel != null)
        {
            wheel.SetTouchEnabled(value);
        }
        if (accelerator != null)
        {
            accelerator.SetTouchEnabled(value);
        }
        if (brake != null)
        {
            brake.SetTouchEnabled(value);
        }
        if (gear != null)
        {
            gear.SetTouchEnabled(value);
        }
    }

    private void UpdateSafeArea()
    {
        if (safeAreaRoot == null || Screen.width <= 0 || Screen.height <= 0)
        {
            return;
        }

        Rect safeArea = IsMobileBuild ? Screen.safeArea : new Rect(0f, 0f, Screen.width, Screen.height);
        if (safeArea == lastSafeArea && Screen.width == lastScreenWidth && Screen.height == lastScreenHeight)
        {
            return;
        }

        lastSafeArea = safeArea;
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        safeAreaRoot.anchorMin = new Vector2(safeArea.xMin / Screen.width, safeArea.yMin / Screen.height);
        safeAreaRoot.anchorMax = new Vector2(safeArea.xMax / Screen.width, safeArea.yMax / Screen.height);
        safeAreaRoot.offsetMin = Vector2.zero;
        safeAreaRoot.offsetMax = Vector2.zero;
    }
}
