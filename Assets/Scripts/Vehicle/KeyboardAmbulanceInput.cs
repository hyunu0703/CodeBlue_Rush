using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>새 Input System의 키보드 입력을 차량에 전달한다.</summary>
[RequireComponent(typeof(AmbulanceController))]
public class KeyboardAmbulanceInput : MonoBehaviour
{
    private AmbulanceController controller;

    // 차량 컨트롤러를 가져온다
    private void Awake()
    {
        controller = GetComponent<AmbulanceController>();
    }

    // 키보드 입력을 매 프레임 차량에 전달한다
    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            controller.SetInput(0f, 0f, 0f);
            return;
        }

        float steer = 0f;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) steer--;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) steer++;

        float throttle = keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f;
        float brake = keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f;

        controller.SetInput(steer, throttle, brake);
    }
}