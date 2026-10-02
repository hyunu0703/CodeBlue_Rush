using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>누르고 있는 조작 버튼을 기존 차량 입력에 전달한다</summary>
public sealed class DriveButtonUI : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public enum Control { Left, Right, Throttle, Brake }
    [SerializeField] private KeyboardAmbulanceInput input;
    [SerializeField] private Control control;
    private int? pointer;

    public void OnPointerDown(PointerEventData data)
    {
        if (pointer.HasValue) return;
        pointer = data.pointerId;
        if (input) input.SetButton(control, true);
    }

    public void OnPointerUp(PointerEventData data)
    {
        if (pointer == data.pointerId) Release();
    }

    public void OnPointerExit(PointerEventData data)
    {
        OnPointerUp(data);
    }

    private void OnDisable()
    {
        Release();
    }

    private void Release()
    {
        if (pointer.HasValue && input) input.SetButton(control, false);
        pointer = null;
    }
}
