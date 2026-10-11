using System;

// 실제 컷신 스크립트의 순수 사각형 판정 함수를 직접 검사한다. Unity 오브젝트는 생성하지 않는다.
internal static class GeometryChecks
{
    /// <summary>차량의 완전 진입, 가장자리 접촉, 부분 진입 및 회전 시의 경계를 검사한다.</summary>
    private static void Main()
    {
        // 현재 구급차 이미지의 월드 크기 0.568×0.944에 편측 0.08 여백을 둔 표시 크기다.
        const float areaWidth = 0.364f;
        const float areaLength = 0.552f;
        const float vehicleWidth = 0.284f;
        const float vehicleLength = 0.472f;
        Expect(PickupParkingGeometry.ContainsVehicle(areaWidth, areaLength, 0f, 0f, vehicleWidth, vehicleLength, 0f), "중앙 진입");
        Expect(PickupParkingGeometry.ContainsVehicle(areaWidth, areaLength, 0.08f, 0f, vehicleWidth, vehicleLength, 0f), "오른쪽 가장자리 접촉");
        Expect(PickupParkingGeometry.ContainsVehicle(areaWidth, areaLength, 0f, -0.08f, vehicleWidth, vehicleLength, 0f), "아래 가장자리 접촉");
        Expect(!PickupParkingGeometry.ContainsVehicle(areaWidth, areaLength, 0.081f, 0f, vehicleWidth, vehicleLength, 0f), "오른쪽 일부만 진입");
        Expect(!PickupParkingGeometry.ContainsVehicle(areaWidth, areaLength, 0f, 0.081f, vehicleWidth, vehicleLength, 0f), "앞쪽 일부만 진입");
        Expect(!PickupParkingGeometry.ContainsVehicle(areaWidth, areaLength, 0f, 0f, vehicleWidth, vehicleLength, (float)Math.PI / 2f), "90도 차량 돌출");
        Expect(!PickupParkingGeometry.ContainsVehicle(areaWidth, areaLength, 0f, 0f, vehicleWidth, vehicleLength, (float)Math.PI / 4f), "45도 차량 돌출");
        Expect(PickupParkingGeometry.ContainsVehicle(areaWidth, areaLength, 0f, 0f, vehicleWidth, vehicleLength, (float)Math.PI), "180도 진입");
        Expect(PickupParkingGeometry.ContainsVehicle(areaWidth, areaLength, 0f, 0f, vehicleWidth, vehicleLength, (float)Math.PI / 36f), "여백 안 5도 진입");
        Expect(!PickupParkingGeometry.ContainsVehicle(0f, areaLength, 0f, 0f, vehicleWidth, vehicleLength, 0f), "잘못된 감지 폭");
        Expect(!PickupParkingGeometry.ContainsVehicle(areaWidth, areaLength, 0f, 0f, 0f, vehicleLength, 0f), "잘못된 차량 폭");
        Expect(!PickupParkingGeometry.ContainsVehicle(areaWidth, areaLength, 0f, 0f, vehicleWidth, vehicleLength, float.NaN), "잘못된 각도");
        Console.WriteLine("PASS: 12 parking containment checks");
    }

    /// <summary>기대 조건을 만족하지 않으면 검사 이름을 포함하여 실패시킨다.</summary>
    private static void Expect(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException(name);
        }
    }
}
