using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>새 Input System의 키보드 입력을 차량에 전달한다.</summary>
[RequireComponent(typeof(AmbulanceController))]
public class KeyboardAmbulanceInput : MonoBehaviour
{
    private AmbulanceController controller;
    private readonly bool[] buttons = new bool[4];

    // 터치 입력과 키보드 입력을 한 경로에서 합친다
    public void SetButton(DriveButtonUI.Control control, bool pressed)
    {
        buttons[(int)control] = pressed;
    }

    private void OnDisable()
    {
        System.Array.Clear(buttons, 0, buttons.Length);
        if (controller) controller.SetInput(0f, 0f, 0f);
    }

    // 차량 컨트롤러를 가져온다
    private void Awake()
    {
        controller = GetComponent<AmbulanceController>();
    }

    // 키보드 입력을 매 프레임 차량에 전달한다
    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        float steer = 0f;

        if (buttons[0] || (keyboard != null && (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed))) steer--;
        if (buttons[1] || (keyboard != null && (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed))) steer++;

        float throttle = buttons[2] || (keyboard != null && (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)) ? 1f : 0f;
        float brake = buttons[3] || (keyboard != null && (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)) ? 1f : 0f;

        controller.SetInput(steer, throttle, brake);
    }
}
