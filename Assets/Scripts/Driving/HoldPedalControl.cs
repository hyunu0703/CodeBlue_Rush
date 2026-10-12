using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class HoldPedalControl : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    // 입력이 0보다 클 때 표시할 눌린 페달 에셋이다.
    [SerializeField] private Sprite pressedSprite;

    // 같은 오브젝트의 UI 이미지와 입력 전 기본 스프라이트를 보관한다.
    private Image pedalImage;
    private Sprite defaultSprite;
    // 자동 슬라이스로 잘린 여백을 복원한 표시용 스프라이트이며 이 컴포넌트가 소유한다.
    private Sprite pressedDisplaySprite;
    private bool canUseTouch;
    private bool isHeld;
    private int pointerId;

    public float Value => isActiveAndEnabled && isHeld ? 1f : 0f;

    // 기본 이미지를 보관하고 전체 텍스처 영역으로 눌린 이미지를 준비한다.
    private void Awake()
    {
        pedalImage = GetComponent<Image>();
        if (pedalImage == null || pedalImage.sprite == null || pressedSprite == null)
        {
            Debug.LogError("페달의 Image, 기본 Sprite, Pressed Sprite 참조를 설정하세요.", this);
            enabled = false;
            return;
        }

        defaultSprite = pedalImage.sprite;
        // 두 상태 모두 투명 여백을 포함한 전체 PNG를 사용해 UI 크기와 기준점을 유지한다.
        var texture = pressedSprite.texture;
        pressedDisplaySprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), pressedSprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
    }

    private void OnDisable()
    {
        ResetInput();
    }

    // 런타임에 만든 표시용 스프라이트만 정리하며 원본 에셋은 보존한다.
    private void OnDestroy()
    {
        if (pressedDisplaySprite != null)
        {
            Destroy(pressedDisplaySprite);
        }
    }

    /// <summary>터치와 키보드를 합친 최종 입력값(0~1)에 맞춰 페달 이미지를 표시한다.</summary>
    public void Display(float inputValue)
    {
        if (pedalImage == null || defaultSprite == null)
        {
            return;
        }

        // 비활성화되거나 입력이 없으면 기본 이미지로 복원한다.
        var nextSprite = isActiveAndEnabled && inputValue > 0f && pressedDisplaySprite != null
            ? pressedDisplaySprite : defaultSprite;
        if (pedalImage.sprite != nextSprite)
        {
            pedalImage.sprite = nextSprite;
        }
    }

    public void SetTouchEnabled(bool value)
    {
        canUseTouch = value;
        if (!value)
        {
            ResetInput();
        }
    }

    // 대기 중인 터치 입력을 비우고 기본 페달 이미지를 복원한다.
    public void ResetInput()
    {
        isHeld = false;
        Display(0f);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!isActiveAndEnabled || !canUseTouch || Time.timeScale <= 0f || isHeld
            || eventData.button != PointerEventData.InputButton.Left)
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
