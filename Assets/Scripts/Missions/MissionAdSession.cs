using UnityEngine;

/// <summary>전면 광고 간격과 저장된 다음 일차에 한 번 적용할 출동 대기 보상을 유지한다.</summary>
internal static class MissionAdSession
{
    private const double CooldownSeconds = 30;           // 실제 전면 광고 표시 시작 사이의 최소 간격이다.
    private static double lastInterstitial = -1;        // 음수는 이 실행에서 표시한 전면 광고가 없음을 뜻한다.
    private static GameProgress rewardProgress;         // 초기화 후 같은 숫자의 일차에 보상이 넘어가지 않게 원본을 연결한다.

    // Domain Reload 설정과 관계없이 새 실행에서는 광고 시각과 보상을 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        lastInterstitial = -1;
        rewardProgress = null;
    }

    // 첫 일차에는 전면 광고를 건너뛰고 실제 노출 사이에 시간 간격을 둔다.
    internal static bool CanShowInterstitial(int day, double now) =>
        day >= 2 && (lastInterstitial < 0 || now - lastInterstitial >= CooldownSeconds);

    // 준비 확인이나 시도 시각이 아닌 실제 표시 시작 시각을 사용한다.
    internal static void RecordInterstitial(double now) => lastInterstitial = now;

    // 하루 완료 저장 성공 뒤에만 다음 일반 진행 상태를 보상 대상으로 지정한다.
    internal static void GrantWaitSkip(GameProgress progress) => rewardProgress = progress;

    // 보상은 같은 저장 결과의 첫 출동 요청을 만들 때 한 번만 적용한다.
    internal static bool ConsumeWaitSkip(GameProgress progress)
    {
        var earned = progress != null && progress.Stage == ProgressStage.Ready && ReferenceEquals(progress, rewardProgress);
        rewardProgress = null;
        return earned;
    }
}
