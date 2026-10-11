using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class SteeringWheelControl : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
    IDragHandler, IInitializePotentialDragHandler
{
    [SerializeField, Min(1f)] private float maximumAngle = 450f;
    [SerializeField, Min(0f)] private float keyboardTurnSpeed = 720f;
    [SerializeField, Min(0f)] private float stationaryReturnSpeed = 180f;
    [SerializeField, Min(0f)] private float movingReturnSpeed = 540f;
    [SerializeField, Min(0.01f)] private float fullReturnSpeedAt = 2f;
    [SerializeField, Min(0f)] private float centerDeadRadius = 16f;

    private RectTransform wheelRect;
    private Quaternion neutralRotation;
    private float angle;
    private int pointerId;
    private float previousPointerAngle;
    private bool isHeld;
    private bool hasPointerAngle;
    private bool canUseTouch;

    public float Steering => angle / Mathf.Max(1f, maximumAngle);

    private void Awake()
    {
        wheelRect = GetComponent<RectTransform>();
        neutralRotation = wheelRect.localRotation;
    }

    private void OnDisable()
    {
        ResetInput();
    }

    public void SetTouchEnabled(bool value)
    {
        canUseTouch = value;
        if (!value)
        {
            isHeld = false;
            hasPointerAngle = false;
        }
    }

    public void Tick(float keyboardSteering, float vehicleSpeed, float dt)
    {
        if (!isActiveAndEnabled || wheelRect == null)
        {
            return;
        }

        if (!canUseTouch && keyboardSteering != 0f)
        {
            angle = Mathf.Clamp(angle + keyboardSteering * keyboardTurnSpeed * dt, -maximumAngle, maximumAngle);
        }
        else if (!isHeld)
        {
            float returnSpeed = Mathf.Lerp(stationaryReturnSpeed, movingReturnSpeed,
                Mathf.Clamp01(vehicleSpeed / fullReturnSpeedAt));
            angle = Mathf.MoveTowards(angle, 0f, returnSpeed * dt);
        }

        ApplyRotation();
    }

    public void ResetInput()
    {
        angle = 0f;
        isHeld = false;
        hasPointerAngle = false;
        ApplyRotation();
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        eventData.useDragThreshold = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!isActiveAndEnabled || !canUseTouch || Time.timeScale <= 0f || isHeld || !IsTouch(eventData))
        {
            return;
        }

        isHeld = true;
        pointerId = eventData.pointerId;
        hasPointerAngle = TryGetPointerAngle(eventData, out previousPointerAngle);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isActiveAndEnabled || !canUseTouch || !isHeld || pointerId != eventData.pointerId || Time.timeScale <= 0f)
        {
            return;
        }

        if (!TryGetPointerAngle(eventData, out float pointerAngle))
        {
            // Rebase after crossing the center, where the angle is undefined.
            hasPointerAngle = false;
            return;
        }

        if (hasPointerAngle)
        {
            // Pointer displacement already contains time: do not multiply by deltaTime.
            angle = Mathf.Clamp(angle - Mathf.DeltaAngle(previousPointerAngle, pointerAngle), -maximumAngle, maximumAngle);
            ApplyRotation();
        }

        previousPointerAngle = pointerAngle;
        hasPointerAngle = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (isHeld && pointerId == eventData.pointerId)
        {
            isHeld = false;
            hasPointerAngle = false;
        }
    }

    public static bool IsTouch(PointerEventData eventData)
    {
        return eventData is ExtendedPointerEventData inputEvent && inputEvent.device is Touchscreen;
    }

    private bool TryGetPointerAngle(PointerEventData eventData, out float result)
    {
        result = 0f;
        // Use the unrotated parent frame; reading wheel-local coordinates would feed its rotation back into the drag.
        var parentRect = wheelRect.parent as RectTransform;
        if (parentRect == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect,
            eventData.position, eventData.pressEventCamera, out Vector2 point))
        {
            return false;
        }

        Vector2 center = parentRect.InverseTransformPoint(wheelRect.TransformPoint(wheelRect.rect.center));
        Vector2 offset = point - center;
        if (offset.sqrMagnitude < centerDeadRadius * centerDeadRadius)
        {
            return false;
        }

        result = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
        return true;
    }

    private void ApplyRotation()
    {
        if (wheelRect != null)
        {
            wheelRect.localRotation = neutralRotation * Quaternion.Euler(0f, 0f, -angle);
        }
    }
}
