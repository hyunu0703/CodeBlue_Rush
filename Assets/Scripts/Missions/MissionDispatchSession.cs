using UnityEngine;

/// <summary>대기 씬에서 확정한 출동을 다음 GamePlay 진입에 한 번만 전달한다.</summary>
public static class MissionDispatchSession
{
    private static int pendingRequest; // 출동 버튼으로 확정한 요청 번호이며, 0은 미확정 상태다.

    // Domain Reload를 사용하지 않아도 이전 실행의 출동 승인이 남지 않게 한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        Clear();
    }

    // 수락만 한 상태와 실제 출동을 구분하고 중복 출동 승인을 차단한다.
    public static bool TryDispatch(int requestNumber)
    {
        if (requestNumber <= 0 || pendingRequest != 0) return false;
        pendingRequest = requestNumber;
        return true;
    }

    // GamePlay에서 맵을 준비하기 직전에 출동 승인을 한 번 소비한다.
    public static bool TryConsume(out int requestNumber)
    {
        requestNumber = pendingRequest;
        pendingRequest = 0;
        return requestNumber > 0;
    }

    // 대기 씬 재진입이나 미션 완료 시 남은 출동 승인을 비운다.
    public static void Clear()
    {
        pendingRequest = 0;
    }
}
