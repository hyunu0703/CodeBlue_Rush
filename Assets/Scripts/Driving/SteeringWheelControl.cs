using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class SteeringWheelControl : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
    IDragHandler, IInitializePotentialDragHandler
{
    [SerializeField, Min(1f)] private float maximumAngle = 450f;
    [SerializeField, Min(0f)] private float keyboardTurnSpeed = 720f;
    [FormerlySerializedAs("stationaryReturnSpeed")]
    [SerializeField, Min(0f)] private float returnSpeed = 1200f;
    [SerializeField, Min(0f)] private float centerDeadRadius = 16f;
    // 기본 450도 범위에서 15/45/90/180/360도 지점의 누적 조향량이다. 범위는 0~1이다.
    private const float FirstSectionSteering = 0.16f;
    private const float SecondSectionSteering = 0.34f;
    private const float ThirdSectionSteering = 0.46f;
    private const float FourthSectionSteering = 0.55f;
    private const float FifthSectionSteering = 0.8f;

    private RectTransform wheelRect;
    private Quaternion neutralRotation;
    private float angle;
    private int pointerId;
    private float previousPointerAngle;
    private bool isHeld;
    private bool hasPointerAngle;
    private bool canUseTouch;

    // UI 회전 범위는 유지하고, 차량에는 초반 감도를 높인 좌우 대칭 -1~1 조향값을 전달한다.
    public float Steering => Mathf.Sign(angle) * EvaluateSteering(Mathf.Clamp01(Mathf.Abs(angle) / Mathf.Max(1f, maximumAngle)));

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

    // 기존 호출 계약을 유지하되 핸들 복귀 속도에는 차량 속도를 적용하지 않는다.
    public void Tick(float keyboardSteering, float vehicleSpeed, float dt)
    {
        if (!isActiveAndEnabled || wheelRect == null)
        {
            return;
        }

        if (!isHeld && keyboardSteering != 0f)
        {
            // 반대 키를 누르면 이전 핸들 각도를 되감는 대기 없이 새 방향으로 돌린다.
            if (angle * keyboardSteering < 0f)
            {
                angle = 0f;
            }
            angle = Mathf.Clamp(angle + keyboardSteering * keyboardTurnSpeed * dt,
                -maximumAngle, maximumAngle);
        }
        else if (!isHeld)
        {
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
        if (!isActiveAndEnabled || !canUseTouch || Time.timeScale <= 0f || isHeld
            || eventData.button != PointerEventData.InputButton.Left)
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
            // 포인터 각도 변위에는 deltaTime을 곱하지 않고 키보드와 같은 조작 구간으로 제한한다.
            angle = Mathf.Clamp(angle - Mathf.DeltaAngle(previousPointerAngle, pointerAngle),
                -maximumAngle, maximumAngle);
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

    /// <summary>각도 비율 0~1을 여섯 구간에서 선형 보간한다. 450도 기준 요청된 지점들을 연속적으로 연결한다.</summary>
    private static float EvaluateSteering(float normalizedAngle)
    {
        // 설정된 최대 각도에 대한 비율을 기준 범위의 각도(도)로 환산한다.
        var referenceAngle = normalizedAngle * 450f;
        if (referenceAngle <= 15f)
        {
            return Mathf.Lerp(0f, FirstSectionSteering, referenceAngle / 15f);
        }
        if (referenceAngle <= 45f)
        {
            return Mathf.Lerp(FirstSectionSteering, SecondSectionSteering, (referenceAngle - 15f) / 30f);
        }
        if (referenceAngle <= 90f)
        {
            return Mathf.Lerp(SecondSectionSteering, ThirdSectionSteering, (referenceAngle - 45f) / 45f);
        }
        if (referenceAngle <= 180f)
        {
            return Mathf.Lerp(ThirdSectionSteering, FourthSectionSteering, (referenceAngle - 90f) / 90f);
        }
        if (referenceAngle <= 360f)
        {
            return Mathf.Lerp(FourthSectionSteering, FifthSectionSteering, (referenceAngle - 180f) / 180f);
        }
        return Mathf.Lerp(FifthSectionSteering, 1f, (referenceAngle - 360f) / 90f);
    }
}
