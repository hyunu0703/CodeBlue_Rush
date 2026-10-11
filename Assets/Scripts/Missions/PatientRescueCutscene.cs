using System.Collections;
using UnityEngine;

/// <summary>임시 구급대원 두 명과 환자 한 명의 대기/이동/탑승을 표현한다. 코루틴 수명은 미션 관리자가 소유한다.</summary>
[DisallowMultipleComponent]
public sealed class PatientRescueCutscene : MonoBehaviour
{
    // 컷신의 대기와 이동 시간은 게임 시간의 초 단위다. 일시정지 중에는 진행하지 않는다.
    [SerializeField, Min(0f)] private float waitingSeconds = 0.7f;
    [SerializeField, Min(0.1f)] private float movingSeconds = 2f;
    // 차량 뒤쪽과의 추가 간격 및 임시 인물 이미지 높이다. 단위는 월드 유닛이다.
    [SerializeField, Min(0f)] private float rearGap = 0.15f;
    [SerializeField, Min(0.1f)] private float actorHeight = 0.65f;
    // 사각형 안쪽과 차량 사이의 편측 여백 및 노란 테두리 두께다. 단위는 월드 유닛이다.
    [SerializeField] private Vector2 pickupPadding = new Vector2(0.08f, 0.08f);
    [SerializeField, Min(0.005f)] private float outlineWidth = 0.025f;
    // 이 컴포넌트가 생성/파괴하는 임시 오브젝트, 스프라이트, 텍스처다. 프로젝트 에셋은 수정하지 않는다.
    private Transform actorRoot;
    private Sprite medicSprite;
    private Sprite patientSprite;
    private Texture2D medicTexture;
    private Texture2D patientTexture;
    // 픽업 표시도 이 컴포넌트가 소유한다. 대기/구출 동안 유지하고 탑승 완료 또는 취소 때 숨긴다.
    private Transform pickupOutline;
    private Sprite outlineSprite;
    private Texture2D outlineTexture;
    private Vector2 parkingSize;

    /// <summary>세 이미지가 모두 사라져 탑승이 완료되었는지 반환한다. 취소는 완료로 처리하지 않는다.</summary>
    public bool IsCompleted { get; private set; }

    /// <summary>차량 이미지와 콜라이더를 모두 감쌀 사각형의 안쪽 크기를 월드 단위로 반환한다.</summary>
    public Vector2 GetParkingSize(SpriteRenderer vehicleRenderer, BoxCollider2D vehicleCollider)
    {
        if (!TryGetVehicleBounds(vehicleRenderer, vehicleCollider, out var minimum, out var maximum))
        {
            return Vector2.zero;
        }
        // 이미지와 콜라이더의 큰 쪽을 포함하고, 양옆과 앞뒤에 같은 여백을 둔다.
        var scale = vehicleCollider.transform.lossyScale;
        return Vector2.Scale(maximum - minimum, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)))
            + new Vector2(Mathf.Max(0f, pickupPadding.x), Mathf.Max(0f, pickupPadding.y)) * 2f;
    }

    /// <summary>노란 사각형을 픽업 차선 방향으로 배치한다. 렌더러는 표시만 담당하고 감지는 별도로 수행한다.</summary>
    public void ConfigurePickupZone(Vector2 position, float rotation, Vector2 size, SpriteRenderer vehicleRenderer)
    {
        if (!isActiveAndEnabled || vehicleRenderer == null || size.x <= 0f || size.y <= 0f)
        {
            return;
        }
        if (pickupOutline == null)
        {
            // 1px 흰색 스프라이트를 네 노란 가장자리에서 공유한다. 영구 머티리얼/이미지 에셋을 변경하지 않는다.
            outlineTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            outlineTexture.SetPixel(0, 0, Color.white);
            outlineTexture.Apply(false, true);
            outlineSprite = Sprite.Create(outlineTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            var root = new GameObject("PatientPickupOutline");
            root.SetActive(false);
            root.transform.SetParent(transform, true);
            pickupOutline = root.transform;
            for (var index = 0; index < 4; index++)
            {
                var edge = new GameObject($"Edge{index}", typeof(SpriteRenderer));
                edge.transform.SetParent(pickupOutline, false);
                var renderer = edge.GetComponent<SpriteRenderer>();
                renderer.sprite = outlineSprite;
                renderer.color = Color.yellow;
                renderer.sortingLayerID = vehicleRenderer.sortingLayerID;
                renderer.sortingOrder = vehicleRenderer.sortingOrder + 1;
            }
        }
        parkingSize = size;
        pickupOutline.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, rotation));
        // 테두리는 감지 영역 바깥에 그린다. 노란 선 안쪽 크기와 판정 크기를 일치시킨다.
        var thickness = Mathf.Max(0.005f, outlineWidth);
        for (var index = 0; index < 4; index++)
        {
            var edge = pickupOutline.GetChild(index);
            var isHorizontal = index < 2;
            var sign = index % 2 == 0 ? -1f : 1f;
            edge.localPosition = isHorizontal ? new Vector2(0f, sign * (size.y + thickness) * 0.5f)
                : new Vector2(sign * (size.x + thickness) * 0.5f, 0f);
            edge.localScale = isHorizontal ? new Vector3(size.x + thickness * 2f, thickness, 1f)
                : new Vector3(thickness, size.y, 1f);
        }
        pickupOutline.gameObject.SetActive(true);
    }

    /// <summary>현재 물리 위치에서 차량 이미지와 콜라이더 전체가 노란 사각형 안에 들어왔는지 검사한다.</summary>
    public bool IsVehicleInsidePickup(Rigidbody2D vehicle, SpriteRenderer vehicleRenderer, BoxCollider2D vehicleCollider)
    {
        if (!isActiveAndEnabled || pickupOutline == null || !pickupOutline.gameObject.activeInHierarchy || vehicle == null
            || !TryGetVehicleBounds(vehicleRenderer, vehicleCollider, out var minimum, out var maximum))
        {
            return false;
        }
        // 보간된 렌더 위치 대신 Rigidbody의 실제 위치/회전을 사용한다. 회전된 차량의 네 모서리를 포함하는 판정이다.
        var scale = vehicle.transform.lossyScale;
        var localCenter = (minimum + maximum) * 0.5f;
        var center = vehicle.position + (Vector2)(Quaternion.Euler(0f, 0f, vehicle.rotation)
            * Vector2.Scale(localCenter, new Vector2(scale.x, scale.y)));
        var relativeCenter = pickupOutline.InverseTransformPoint(center);
        var halfSize = Vector2.Scale((maximum - minimum) * 0.5f, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
        var angle = (vehicle.rotation - pickupOutline.eulerAngles.z) * Mathf.Deg2Rad;
        return PickupParkingGeometry.ContainsVehicle(parkingSize.x * 0.5f, parkingSize.y * 0.5f,
            relativeCenter.x, relativeCenter.y, halfSize.x, halfSize.y, angle);
    }

    /// <summary>같은 차량의 이미지와 콜라이더 로컬 사각형을 합친 경계를 구한다.</summary>
    private static bool TryGetVehicleBounds(SpriteRenderer vehicleRenderer, BoxCollider2D vehicleCollider,
        out Vector2 minimum, out Vector2 maximum)
    {
        minimum = Vector2.zero;
        maximum = Vector2.zero;
        if (vehicleRenderer == null || vehicleRenderer.sprite == null || vehicleCollider == null
            || vehicleRenderer.transform != vehicleCollider.transform)
        {
            return false;
        }
        // 콜라이더보다 큰 이미지도 주차 표시 밖으로 튀어나오지 않도록 같은 차량의 두 로컬 경계를 합친다.
        var spriteBounds = vehicleRenderer.sprite.bounds;
        minimum = Vector2.Min(spriteBounds.min, vehicleCollider.offset - vehicleCollider.size * 0.5f);
        maximum = Vector2.Max(spriteBounds.max, vehicleCollider.offset + vehicleCollider.size * 0.5f);
        return true;
    }

    /// <summary>픽업 장소에 임시 인물을 배치한다. 다음 미션에서는 같은 표시 오브젝트를 재사용한다.</summary>
    public void ShowPickup(Vector2 position, float viewAngle, SpriteRenderer ambulanceRenderer)
    {
        if (!isActiveAndEnabled || ambulanceRenderer == null)
        {
            return;
        }
        EnsureActors(ambulanceRenderer);
        IsCompleted = false;
        actorRoot.position = position;
        actorRoot.rotation = Quaternion.Euler(0f, 0f, viewAngle);
        SetActorSpacing(1f);
        actorRoot.gameObject.SetActive(true);
        if (pickupOutline != null)
        {
            pickupOutline.gameObject.SetActive(true);
        }
    }

    /// <summary>대기한 뒤 세 이미지를 차량 후면으로 옮기고 숨긴다. 차량 물리 잠금과 입력 복구는 호출자가 담당한다.</summary>
    public IEnumerator Play(Rigidbody2D ambulance, BoxCollider2D vehicleCollider)
    {
        IsCompleted = false;
        if (!isActiveAndEnabled || actorRoot == null || ambulance == null || vehicleCollider == null)
        {
            yield break;
        }
        // 입력이 잠긴 차량의 로컬 -Y가 후면이다. 회전/스케일/콜라이더 오프셋을 모두 반영한다.
        var start = actorRoot.position;
        var rear = ambulance.transform.TransformPoint(new Vector3(vehicleCollider.offset.x,
            vehicleCollider.offset.y - vehicleCollider.size.y * 0.5f, 0f));
        rear -= ambulance.transform.up * rearGap;
        var elapsed = 0f;
        while (elapsed < waitingSeconds)
        {
            if (!isActiveAndEnabled || actorRoot == null || ambulance == null || vehicleCollider == null)
            {
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
        elapsed = 0f;
        while (elapsed < movingSeconds)
        {
            if (!isActiveAndEnabled || actorRoot == null || ambulance == null || vehicleCollider == null)
            {
                yield break;
            }
            elapsed += Time.deltaTime;
            // 세로 대형을 점차 좁혀 세 이미지가 모두 같은 차량 후면 지점에 도착하게 한다.
            var progress = Mathf.SmoothStep(0f, 1f, elapsed / movingSeconds);
            actorRoot.position = Vector3.Lerp(start, rear, progress);
            SetActorSpacing(1f - progress);
            yield return null;
        }
        // 완료 판정은 이미지가 사라진 뒤에만 공개한다.
        actorRoot.gameObject.SetActive(false);
        if (pickupOutline != null)
        {
            pickupOutline.gameObject.SetActive(false);
        }
        IsCompleted = true;
    }

    /// <summary>활성 기간 종료나 미션 종료 시 표시를 숨기고 완료 판정을 비운다.</summary>
    public void Cancel()
    {
        IsCompleted = false;
        if (actorRoot != null)
        {
            actorRoot.gameObject.SetActive(false);
        }
        if (pickupOutline != null)
        {
            pickupOutline.gameObject.SetActive(false);
        }
    }

    /// <summary>비활성화만으로 호출자가 소유한 코루틴이 멈추지는 않으므로 표시만 명시적으로 정리한다.</summary>
    private void OnDisable()
    {
        Cancel();
    }

    /// <summary>씬에서 제거될 때 소유한 임시 이미지 리소스를 해제한다. 외부 에셋에는 Destroy를 호출하지 않는다.</summary>
    private void OnDestroy()
    {
        if (medicSprite != null)
        {
            Destroy(medicSprite);
        }
        if (patientSprite != null)
        {
            Destroy(patientSprite);
        }
        if (medicTexture != null)
        {
            Destroy(medicTexture);
        }
        if (patientTexture != null)
        {
            Destroy(patientTexture);
        }
        if (actorRoot != null)
        {
            Destroy(actorRoot.gameObject);
        }
        if (outlineSprite != null)
        {
            Destroy(outlineSprite);
        }
        if (outlineTexture != null)
        {
            Destroy(outlineTexture);
        }
        if (pickupOutline != null)
        {
            Destroy(pickupOutline.gameObject);
        }
    }

    /// <summary>기본 이미지와 세 렌더러를 한 번만 만든다. 다음 픽업에서 배치와 표시만 변경한다.</summary>
    private void EnsureActors(SpriteRenderer ambulanceRenderer)
    {
        if (actorRoot != null)
        {
            return;
        }
        // 작은 절차적 임시 이미지는 외부 이미지나 import 없이 실행된다. 구급대원은 십자 유니폼, 환자는 들것이다.
        medicTexture = CreateTexture(false);
        patientTexture = CreateTexture(true);
        medicSprite = Sprite.Create(medicTexture, new Rect(0f, 0f, 24f, 32f), new Vector2(0.5f, 0.5f), 32f);
        patientSprite = Sprite.Create(patientTexture, new Rect(0f, 0f, 24f, 32f), new Vector2(0.5f, 0.5f), 32f);
        var group = new GameObject("PatientRescueActors");
        group.SetActive(false);
        group.transform.SetParent(transform, false);
        actorRoot = group.transform;
        // 도보 중심선을 따라 서로 겹치지 않는 세로 한 줄로 배치한다.
        CreateActor("MedicLeft", medicSprite, new Vector2(0f, actorHeight * 1.15f), ambulanceRenderer);
        CreateActor("MedicRight", medicSprite, new Vector2(0f, -actorHeight * 1.15f), ambulanceRenderer);
        CreateActor("Patient", patientSprite, Vector2.zero, ambulanceRenderer);
    }

    /// <summary>도보에서는 세로 대형을 유지하고 차량 후면에 도착할 때는 세 인물의 위치를 모은다.</summary>
    private void SetActorSpacing(float fraction)
    {
        if (actorRoot == null || actorRoot.childCount != 3)
        {
            return;
        }
        // 생성 순서는 대원 두 명, 환자 한 명으로 고정되어 있으며 매 프레임 컴포넌트 검색을 하지 않는다.
        var spacing = actorHeight * 1.15f * Mathf.Clamp01(fraction);
        actorRoot.GetChild(0).localPosition = Vector2.up * spacing;
        actorRoot.GetChild(1).localPosition = Vector2.down * spacing;
        actorRoot.GetChild(2).localPosition = Vector3.zero;
    }

    /// <summary>구급차보다 위에 그려지는 표시 전용 스프라이트를 추가한다. 충돌과 물리에는 참여하지 않는다.</summary>
    private void CreateActor(string actorName, Sprite sprite, Vector2 offset, SpriteRenderer ambulanceRenderer)
    {
        // 작은 이미지의 로컬 배치와 정렬 순서다. 차량 자체의 머티리얼 인스턴스는 생성하지 않는다.
        var actor = new GameObject(actorName, typeof(SpriteRenderer));
        actor.transform.SetParent(actorRoot, false);
        actor.transform.localPosition = offset;
        actor.transform.localScale = Vector3.one * actorHeight;
        var renderer = actor.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingLayerID = ambulanceRenderer.sortingLayerID;
        renderer.sortingOrder = ambulanceRenderer.sortingOrder + 2;
    }

    /// <summary>구급대원 또는 환자 기본 이미지를 투명한 24x32 텍스처에 만든다. 텍스처 수명은 이 컴포넌트다.</summary>
    private static Texture2D CreateTexture(bool isPatient)
    {
        // 피부, 유니폼/담요, 들것의 색이다. 투명 픽셀은 배경 도로를 가리지 않는다.
        var texture = new Texture2D(24, 32, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color32[24 * 32];
        var skin = new Color32(255, 209, 167, 255);
        var clothing = isPatient ? new Color32(255, 171, 78, 255) : new Color32(40, 156, 202, 255);
        for (var y = 0; y < 32; y++)
        {
            for (var x = 0; x < 24; x++)
            {
                // 얼굴 원, 몸, 다리 및 환자의 들것을 서로 다른 도형으로 표시한다.
                var tint = new Color32(0, 0, 0, 0);
                if ((x - 12) * (x - 12) + (y - 26) * (y - 26) <= 20)
                {
                    tint = skin;
                }
                if (y >= 8 && y <= 21 && x >= 6 && x <= 17)
                {
                    tint = clothing;
                }
                if (y >= 2 && y < 8 && ((x >= 6 && x <= 10) || (x >= 13 && x <= 17)))
                {
                    tint = new Color32(38, 57, 83, 255);
                }
                if (!isPatient && ((x >= 11 && x <= 13 && y >= 12 && y <= 18)
                    || (y >= 14 && y <= 16 && x >= 9 && x <= 15)))
                {
                    tint = new Color32(255, 255, 255, 255);
                }
                if (isPatient && y >= 1 && y <= 23 && (x == 3 || x == 20))
                {
                    tint = new Color32(220, 231, 239, 255);
                }
                pixels[y * 24 + x] = tint;
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
}

/// <summary>엔진 상태 없이 회전된 차량 사각형의 완전 진입을 계산한다. 값은 동일한 월드 단위를 사용한다.</summary>
internal static class PickupParkingGeometry
{
    /// <summary>감지 사각형 좌표계에서 차량의 모든 모서리가 내부인지 반환한다. 각도 단위는 라디안이다.</summary>
    public static bool ContainsVehicle(float areaHalfWidth, float areaHalfLength, float centerX, float centerY,
        float vehicleHalfWidth, float vehicleHalfLength, float angle)
    {
        // 회전된 차량의 사각형 축에 투영한 반폭/반길이다. 가장자리 접촉은 부동소수점 오차 범위에서 허용한다.
        var cosine = System.Math.Abs(System.Math.Cos(angle));
        var sine = System.Math.Abs(System.Math.Sin(angle));
        var projectedWidth = vehicleHalfWidth * cosine + vehicleHalfLength * sine;
        var projectedLength = vehicleHalfWidth * sine + vehicleHalfLength * cosine;
        return areaHalfWidth > 0f && areaHalfLength > 0f && vehicleHalfWidth > 0f && vehicleHalfLength > 0f
            && System.Math.Abs(centerX) + projectedWidth <= areaHalfWidth + 0.00001f
            && System.Math.Abs(centerY) + projectedLength <= areaHalfLength + 0.00001f;
    }
}
