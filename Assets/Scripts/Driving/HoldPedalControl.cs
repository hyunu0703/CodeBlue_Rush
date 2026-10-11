using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class HoldPedalControl : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    private bool canUseTouch;
    private bool isHeld;
    private int pointerId;

    public float Value => isActiveAndEnabled && isHeld ? 1f : 0f;

    private void OnDisable()
    {
        ResetInput();
    }

    public void SetTouchEnabled(bool value)
    {
        canUseTouch = value;
        if (!value)
        {
            ResetInput();
        }
    }

    public void ResetInput()
    {
        isHeld = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!isActiveAndEnabled || !canUseTouch || Time.timeScale <= 0f || isHeld || !SteeringWheelControl.IsTouch(eventData))
        {
            return;
        }

        pointerId = eventData.pointerId;
        isHeld = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        ReleasePointer(eventData);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ReleasePointer(eventData);
    }

    private void ReleasePointer(PointerEventData eventData)
    {
        if (isHeld && pointerId == eventData.pointerId)
        {
            ResetInput();
        }
    }
}
