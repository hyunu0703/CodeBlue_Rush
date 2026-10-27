using UnityEngine;

/// <summary>Start에서 복원한 진행을 씬 사이에 전달하고 저장 성공 시에만 상태 후보를 채택한다.</summary>
public static class GameProgressSession
{
    private static ProgressStore store;  // 현재 실행의 저장 작업이며 최초 읽기 때 준비한다.
    private static GameProgress current; // 마지막으로 정상 복원하거나 저장에 성공한 상태다.
    private static bool isLoaded;       // 읽기에 실패한 상태에서 기존 메모리 값을 사용하지 못하게 한다.

    public static GameProgress Current => current;
    public static bool IsLoaded => isLoaded;

    // Domain Reload가 없어도 이전 실행의 메모리 상태와 저장 작업을 새 실행으로 넘기지 않는다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        store = null;
        current = null;
        isLoaded = false;
    }

    // 읽기 성공 이후에만 Waiting에 전달할 진행을 교체한다.
    public static bool TryLoad(out string error)
    {
        isLoaded = false;
        if (store == null) store = new ProgressStore();
        if (!store.TryLoad(out var loaded, out _, out error)) return false;
        current = loaded;
        isLoaded = true;
        return true;
    }

    // 동일 후보 재시도는 재합산 없이 저장만 반복하며, 실패하면 현재 상태를 보존한다.
    public static bool TrySave(GameProgress candidate, out string error)
    {
        if (!isLoaded || store == null)
        {
            error = "진행 데이터를 정상적으로 읽기 전에는 저장할 수 없습니다.";
            return false;
        }
        if (!store.TrySave(candidate, out error)) return false;
        current = candidate;
        return true;
    }
}
