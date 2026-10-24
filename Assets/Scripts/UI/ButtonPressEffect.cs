using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>버튼의 기존 크기를 기준으로 누름과 복원 효과를 표시한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class ButtonPressEffect : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
    IPointerExitHandler, IPointerEnterHandler, ISubmitHandler
{
    private Button button;               // 상호작용 가능 여부를 확인할 버튼이다.
    private Vector3 originalScale;       // 비활성화 시에도 복원할 원래 크기다.
    private bool isPressed;              // 현재 포인터가 누른 상태인지 나타낸다.
    private int pointerId = int.MinValue; // 누름을 시작한 포인터만 해제할 수 있다.
    private float submitUntil;           // 키보드 제출 효과의 종료 시각이다.

    // 기존 버튼에 효과를 한 번만 연결한다.
    public static void Attach(Button target)
    {
        if (target != null && !target.TryGetComponent<ButtonPressEffect>(out _))
            target.gameObject.AddComponent<ButtonPressEffect>();
    }

    private void Awake()
    {
        button = GetComponent<Button>();
        originalScale = transform.localScale;
    }

    private void Update()
    {
        bool held = button.IsInteractable() && (isPressed || Time.unscaledTime < submitUntil);
        Vector3 target = originalScale * (held ? 0.94f : 1f);
        transform.localScale = Vector3.Lerp(transform.localScale, target,
            1f - Mathf.Exp(-30f * Time.unscaledDeltaTime));
    }

    private void OnDisable()
    {
        isPressed = false;
        pointerId = int.MinValue;
        submitUntil = 0f;
        transform.localScale = originalScale;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus) return;
        // 창 밖에서 입력을 놓아 PointerUp이 누락되어도 누름 상태가 남지 않게 한다.
        isPressed = false;
        pointerId = int.MinValue;
        submitUntil = 0f;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || !button.IsInteractable()
            || pointerId != int.MinValue) return;
        pointerId = eventData.pointerId;
        isPressed = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId != pointerId) return;
        pointerId = int.MinValue;
        isPressed = false;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (eventData.pointerId == pointerId) isPressed = false;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (eventData.pointerId == pointerId) isPressed = true;
    }

    public void OnSubmit(BaseEventData eventData)
    {
        if (button.IsInteractable()) submitUntil = Time.unscaledTime + 0.12f;
    }
}
