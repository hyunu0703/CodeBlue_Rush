#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>사용자 저장과 분리한 실제 파일·Waiting 씬에서 평가와 저장 재시도 경계를 검증한다.</summary>
public sealed class WaitingProgressTests
{
    private string directory;                  // 이 테스트만 소유한 임시 저장 디렉터리다.
    private string path;                       // 실제 진행 파일과 분리한 저장 경로다.
    private string originalText;               // 실패 시 보존해야 할 저장 원본이다.
    private int writes;                        // 중복 입력의 실제 쓰기 횟수를 관찰한다.
    private Action onWrite;                    // 저장 도중의 버튼 재진입을 재현한다.
    private ProgressStore store;               // 실제 파일 입출력을 감싸 관찰할 저장 객체다.
    private WaitingMissionController waiting;  // 실제 씬의 평가·요청 흐름이다.

    // 프로젝트의 리로드 정책을 유지하며 테스트 소유 경로만 준비한다.
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return new EnterPlayMode(!EditorSettings.enterPlayModeOptionsEnabled
            || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0);
        directory = Path.Combine(Application.temporaryCachePath, "WaitingProgress-" + Guid.NewGuid().ToString("N"));
        path = Path.Combine(directory, "progress.json");
        writes = 0;
        onWrite = null;
    }

    // 플레이 모드와 테스트 소유 파일만 정리한다.
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    // 일반 일차의 안내·거절·수락은 기존 요청 흐름을 유지하고 저장을 발생시키지 않는다.
    [UnityTest]
    public IEnumerator Ready_ShowsCumulativeGoalAndPreservesOfferFlow()
    {
        yield return LoadProgress(3, 100, 1, ProgressStage.Ready);
        Assert.That(Field<TMP_Text>(waiting, "progressText").text, Does.Contain("3일차"));
        Assert.That(Field<TMP_Text>(waiting, "progressText").text, Does.Contain("누적 실적: 100점"));
        Assert.That(Field<TMP_Text>(waiting, "progressText").text, Does.Contain("다음 누적 목표: 200점"));
        Assert.That(Field<TMP_Text>(waiting, "progressText").text, Does.Contain("부족 실적: 100점"));
        yield return Offer();
        waiting.OpenBriefing(); waiting.RejectMission();
        yield return Offer();
        waiting.OpenBriefing(); waiting.AcceptMission();
        Assert.That(Field<Button>(waiting, "dispatchButton").gameObject.activeSelf, Is.True);
        Assert.That(writes, Is.Zero);
    }

    // 동점 통과에서 일차와 누적 실적을 유지하고 다음 목표를 정확히 한 번 올린다.
    [UnityTest]
    public IEnumerator EqualGoals_AtSixElevenSixteen_ContinueExactlyOnce()
    {
        var days = new[] { 6, 11, 16 };
        var scores = new long[] { 200, 420, 660 };
        var targets = new long[] { 420, 660, 920 };
        for (var index = 0; index < days.Length; index++)
        {
            onWrite = null;
            yield return LoadProgress(days[index], scores[index], index + 1, ProgressStage.EvaluationPending);
            var before = writes;
            AssertBlocked();
            Assert.That(Field<TMP_Text>(waiting, "evaluationText").text, Does.Contain("실적 평가 통과"));
            Assert.That(GameProgressSession.Current.EvaluationPeriod, Is.EqualTo(index + 1));
            onWrite = () => ActionButton().onClick.Invoke();
            ActionButton().onClick.Invoke(); ActionButton().onClick.Invoke();
            Assert.That(writes, Is.EqualTo(before + 1));
            Assert.That(GameProgressSession.Current.CurrentDay, Is.EqualTo(days[index]));
            Assert.That(GameProgressSession.Current.CumulativeScore, Is.EqualTo(scores[index]));
            Assert.That(GameProgressSession.Current.CumulativeTarget, Is.EqualTo(targets[index]));
            Assert.That(GameProgressSession.Current.Stage, Is.EqualTo(ProgressStage.Ready));
            yield return Reopen();
            Assert.That(Field<GameObject>(waiting, "evaluationPanel").activeSelf, Is.False);
            Assert.That(GameProgressSession.Current.CumulativeTarget, Is.EqualTo(targets[index]));
            Assert.That(writes, Is.EqualTo(before + 1));
        }
    }

    // 다음 목표까지 초과한 누적 실적도 차감하지 않으며 다음 일차나 평가를 건너뛰지 않는다.
    [UnityTest]
    public IEnumerator Surplus_ContinuesSameDayAndShowsZeroDeficit()
    {
        yield return LoadProgress(6, 650, 1, ProgressStage.EvaluationPending);
        ActionButton().onClick.Invoke();
        Assert.That(GameProgressSession.Current.CurrentDay, Is.EqualTo(6));
        Assert.That(GameProgressSession.Current.CumulativeScore, Is.EqualTo(650));
        Assert.That(GameProgressSession.Current.EvaluationPeriod, Is.EqualTo(2));
        Assert.That(Field<TMP_Text>(waiting, "progressText").text, Does.Contain("부족 실적: 0점"));
        yield return Offer();
    }

    // 미달은 해고를 먼저 저장하며 재실행 이후에도 초기화 없이 같은 해고를 표시한다.
    [UnityTest]
    public IEnumerator FailedEvaluation_PersistsFiredBeforeAllowingRestart()
    {
        yield return LoadProgress(6, 199, 1, ProgressStage.EvaluationPending);
        Assert.That(writes, Is.EqualTo(1));
        Assert.That(GameProgressSession.Current.Stage, Is.EqualTo(ProgressStage.Fired));
        Assert.That(Field<TMP_Text>(waiting, "evaluationLabel").text, Is.EqualTo("다시 시작"));
        AssertBlocked();
        yield return Reopen();
        Assert.That(writes, Is.EqualTo(1));
        Assert.That(GameProgressSession.Current.CurrentDay, Is.EqualTo(6));
        Assert.That(GameProgressSession.Current.CumulativeScore, Is.EqualTo(199));
        Assert.That(GameProgressSession.Current.Stage, Is.EqualTo(ProgressStage.Fired));
    }

    // 해고 기록 저장 재시도의 연속 입력은 해고 후 재시작까지 자동으로 이어지지 않는다.
    [UnityTest]
    public IEnumerator FiringWriteFailure_RetryTwiceDoesNotRestartGame()
    {
        yield return LoadProgress(6, 199, 1, ProgressStage.EvaluationPending, true);
        var candidate = Field<GameProgress>(waiting, "pendingProgress");
        Assert.That(GameProgressSession.Current.Stage, Is.EqualTo(ProgressStage.EvaluationPending));
        Assert.That(File.ReadAllText(path), Is.EqualTo(originalText));
        waiting.enabled = false; waiting.enabled = true;
        Assert.That(writes, Is.EqualTo(1));
        Assert.That(Field<GameProgress>(waiting, "pendingProgress"), Is.SameAs(candidate));
        ActionButton().onClick.Invoke();
        Assert.That(writes, Is.EqualTo(1));
        Directory.Delete(path + ".tmp");
        onWrite = () => { ActionButton().onClick.Invoke(); RetryButton().onClick.Invoke(); };
        RetryButton().onClick.Invoke(); RetryButton().onClick.Invoke();
        Assert.That(writes, Is.EqualTo(2));
        Assert.That(GameProgressSession.Current.Stage, Is.EqualTo(ProgressStage.Fired));
        Assert.That(GameProgressSession.Current.CurrentDay, Is.EqualTo(6));
        Assert.That(GameProgressSession.Current.CumulativeScore, Is.EqualTo(199));
        Assert.That(RetryButton().gameObject.activeSelf, Is.False);
        AssertBlocked();
        Canvas.ForceUpdateCanvases();
        yield return null;
        var events = UnityEngine.EventSystems.EventSystem.current;
        var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        events.RaycastAll(new UnityEngine.EventSystems.PointerEventData(events)
            { position = RectTransformUtility.WorldToScreenPoint(null, RetryButton().transform.position) }, hits);
        Assert.That(hits.Count, Is.GreaterThan(0));
        Assert.That(hits[0].gameObject.transform.IsChildOf(ActionButton().transform), Is.False,
            "재시도 위치의 다음 클릭이 다시 시작 버튼에 닿으면 안 된다.");
        ActionButton().onClick.Invoke(); ActionButton().onClick.Invoke();
        Assert.That(writes, Is.EqualTo(3));
        Assert.That(GameProgressSession.Current.CurrentDay, Is.EqualTo(1));
        Assert.That(GameProgressSession.Current.CumulativeScore, Is.Zero);
        Assert.That(GameProgressSession.Current.CumulativeTarget, Is.EqualTo(200));
    }

    // 통과 저장 실패에서는 원본·후보·이전 목표를 유지하고 재시도 성공 후에만 새 목표를 사용한다.
    [UnityTest]
    public IEnumerator ContinueWriteFailure_PreservesCandidateAndAvoidsDoubleTargetIncrease()
    {
        yield return LoadProgress(6, 250, 1, ProgressStage.EvaluationPending);
        var original = GameProgressSession.Current;
        var candidate = Field<GameProgress>(waiting, "pendingProgress");
        Directory.CreateDirectory(path + ".tmp");
        ActionButton().onClick.Invoke(); RetryButton().onClick.Invoke();
        Assert.That(writes, Is.EqualTo(2));
        Assert.That(GameProgressSession.Current, Is.SameAs(original));
        Assert.That(File.ReadAllText(path), Is.EqualTo(originalText));
        Assert.That(Field<TMP_Text>(waiting, "saveStatus").text, Does.Contain("다시 시도"));
        AssertBlocked();
        waiting.enabled = false; waiting.enabled = true;
        Assert.That(Field<GameProgress>(waiting, "pendingProgress"), Is.SameAs(candidate));
        Directory.Delete(path + ".tmp");
        onWrite = () => RetryButton().onClick.Invoke();
        RetryButton().onClick.Invoke(); RetryButton().onClick.Invoke(); ActionButton().onClick.Invoke();
        Assert.That(writes, Is.EqualTo(3));
        Assert.That(GameProgressSession.Current.CurrentDay, Is.EqualTo(6));
        Assert.That(GameProgressSession.Current.CumulativeScore, Is.EqualTo(250));
        Assert.That(GameProgressSession.Current.CumulativeTarget, Is.EqualTo(420));
        yield return Offer();
    }

    // 저장된 해고를 처음 표시할 때는 쓰지 않으며 재시작 실패는 해고 파일을 보존한다.
    [UnityTest]
    public IEnumerator RestartWriteFailure_RetainsFiredUntilResetSaveSucceeds()
    {
        yield return LoadProgress(6, 199, 1, ProgressStage.Fired);
        Assert.That(writes, Is.Zero);
        Directory.CreateDirectory(path + ".tmp"); ActionButton().onClick.Invoke();
        Assert.That(GameProgressSession.Current.Stage, Is.EqualTo(ProgressStage.Fired));
        Assert.That(File.ReadAllText(path), Is.EqualTo(originalText));
        AssertBlocked();
        yield return Reopen();
        Assert.That(GameProgressSession.Current.Stage, Is.EqualTo(ProgressStage.Fired));
        Assert.That(writes, Is.EqualTo(1));
        Directory.Delete(path + ".tmp");
        onWrite = () => ActionButton().onClick.Invoke();
        ActionButton().onClick.Invoke(); ActionButton().onClick.Invoke();
        Assert.That(writes, Is.EqualTo(2));
        Assert.That(GameProgressSession.Current.CurrentDay, Is.EqualTo(1));
        Assert.That(GameProgressSession.Current.CumulativeScore, Is.Zero);
        Assert.That(GameProgressSession.Current.EvaluationPeriod, Is.EqualTo(1));
        Assert.That(GameProgressSession.Current.CumulativeTarget, Is.EqualTo(200));
        yield return Reopen();
        Assert.That(Field<GameObject>(waiting, "evaluationPanel").activeSelf, Is.False);
        Assert.That(writes, Is.EqualTo(2));
    }

    // 통과 확인 전 종료와 통과 저장 후 종료는 같은 일차에서도 서로 다른 목표로 복원한다.
    [UnityTest]
    public IEnumerator PendingBeforeContinue_ReopensAtOriginalGoal()
    {
        yield return LoadProgress(6, 250, 1, ProgressStage.EvaluationPending);
        yield return Reopen();
        Assert.That(GameProgressSession.Current.CurrentDay, Is.EqualTo(6));
        Assert.That(GameProgressSession.Current.CumulativeTarget, Is.EqualTo(200));
        Assert.That(writes, Is.Zero);
        AssertBlocked();
        ActionButton().onClick.Invoke();
        yield return Reopen();
        Assert.That(GameProgressSession.Current.CurrentDay, Is.EqualTo(6));
        Assert.That(GameProgressSession.Current.CumulativeTarget, Is.EqualTo(420));
        Assert.That(writes, Is.EqualTo(1));
    }

    // 저장 중 비활성화돼도 재활성화 시 저장된 목표로 요청 대기를 재개한다.
    [UnityTest]
    public IEnumerator DisabledDuringSave_ResumesWithoutSavingAgain()
    {
        yield return LoadProgress(6, 250, 1, ProgressStage.EvaluationPending);
        onWrite = () => waiting.enabled = false;
        ActionButton().onClick.Invoke();
        Assert.That(GameProgressSession.Current.Stage, Is.EqualTo(ProgressStage.Ready));
        Assert.That(writes, Is.EqualTo(1));
        waiting.enabled = true;
        yield return Offer();
        Assert.That(writes, Is.EqualTo(1));
        Assert.That(GameProgressSession.Current.CumulativeTarget, Is.EqualTo(420));
    }

    // 평가·저장 실패 안내와 실제 버튼의 배치·레이캐스트를 여러 패널 크기에서 검사한다.
    [UnityTest]
    public IEnumerator EvaluationLayout_KeepsContentAndRetryInsidePanel()
    {
        yield return LoadProgress(6, 250, 1, ProgressStage.EvaluationPending);
        Directory.CreateDirectory(path + ".tmp"); ActionButton().onClick.Invoke();
        var root = Field<RectTransform>(waiting, "safeAreaRoot");
        var panel = (RectTransform)Field<GameObject>(waiting, "evaluationPanel").transform;
        foreach (var size in new[] { new Vector2(480, 854), new Vector2(320, 568), new Vector2(854, 480) })
        {
            panel.sizeDelta = size - root.rect.size;
            Canvas.ForceUpdateCanvases();
            foreach (var name in new[] { "evaluationText", "saveStatus" })
            {
                var text = Field<TMP_Text>(waiting, name); text.ForceMeshUpdate();
                Assert.That(text.isTextOverflowing, Is.False, name + " " + size);
            }
            var corners = new Vector3[4]; ((RectTransform)RetryButton().transform).GetWorldCorners(corners);
            foreach (var corner in corners) Assert.That(panel.rect.Contains(panel.InverseTransformPoint(corner)), Is.True);
            var actionCorners = new Vector3[4]; ((RectTransform)ActionButton().transform).GetWorldCorners(actionCorners);
            Assert.That(panel.InverseTransformPoint(corners[0]).y,
                Is.GreaterThan(panel.InverseTransformPoint(actionCorners[1]).y),
                "재시도와 주 입력의 위치가 겹치면 실제 연속 클릭이 다른 동작을 실행할 수 있다.");
        }
        panel.sizeDelta = Vector2.zero; Canvas.ForceUpdateCanvases();
        yield return null;
        var events = UnityEngine.EventSystems.EventSystem.current;
        var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        events.RaycastAll(new UnityEngine.EventSystems.PointerEventData(events)
            { position = RectTransformUtility.WorldToScreenPoint(null, RetryButton().transform.position) }, hits);
        Assert.That(hits.Count, Is.GreaterThan(0));
        Assert.That(hits[0].gameObject.transform.IsChildOf(RetryButton().transform), Is.True,
            "재시도 버튼을 가린 UI: " + hits[0].gameObject.name);
    }

    // Waiting 직접 실행에서 읽기 손상을 저장 없음으로 바꾸거나 덮어쓰지 않는다.
    [UnityTest]
    public IEnumerator CorruptSave_DirectEntryReturnsToStartWithoutWriting()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, "{}");
        typeof(GameProgressSession).GetMethod("ResetState", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        typeof(GameProgressSession).GetField("store", BindingFlags.Static | BindingFlags.NonPublic)
            .SetValue(null, new ProgressStore(path));
        SceneManager.LoadScene("Waiting");
        var end = Time.realtimeSinceStartupAsDouble + 10;
        while (SceneManager.GetActiveScene().name != "Start")
        {
            Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(end)); yield return null;
        }
        Assert.That(GameProgressSession.IsLoaded, Is.False);
        Assert.That(File.ReadAllText(path), Is.EqualTo("{}"));
    }

    // 유효한 저장 이력을 준비하고 실제 파일 입출력에 중복 쓰기 관찰만 덧붙인다.
    private IEnumerator LoadProgress(int day, long score, int period, ProgressStage stage, bool fail = false)
    {
        Assert.That(GameProgress.TryRestore(day, score, period, stage, out var source), Is.True);
        var actual = new ProgressStore(path);
        Assert.That(actual.TryLoad(out _, out _, out _), Is.True);
        Assert.That(actual.TrySave(source, out var error), Is.True, error);
        originalText = File.ReadAllText(path);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var read = (Func<string>)typeof(ProgressStore).GetField("readText", flags).GetValue(actual);
        var write = (Action<string>)typeof(ProgressStore).GetField("writeText", flags).GetValue(actual);
        store = new ProgressStore(read, text => { writes++; onWrite?.Invoke(); write(text); });
        if (fail) Directory.CreateDirectory(path + ".tmp");
        yield return Reopen();
    }

    // 씬과 세션을 새로 만들어 실제 파일에서 복원하며 사용자 저장은 읽지 않는다.
    private IEnumerator Reopen()
    {
        typeof(GameProgressSession).GetMethod("ResetState", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        typeof(GameProgressSession).GetField("store", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, store);
        SceneManager.LoadScene("Waiting"); yield return null;
        waiting = UnityEngine.Object.FindFirstObjectByType<WaitingMissionController>();
        Assert.That(waiting, Is.Not.Null);
        Set(waiting, "offerDelaySeconds", .05f);
        waiting.enabled = false; waiting.enabled = true;
    }

    // 요청 알림이 도착할 때까지 기다리며 평가 중 요청이 생기는 오류와 구분한다.
    private IEnumerator Offer()
    {
        var end = Time.realtimeSinceStartupAsDouble + 3;
        while (!Field<Button>(waiting, "missionButton").gameObject.activeSelf)
        {
            Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(end)); yield return null;
        }
    }

    // 공개 호출로도 평가·해고·저장 실패 상태에서 출동을 승인할 수 없어야 한다.
    private void AssertBlocked()
    {
        waiting.OpenBriefing(); waiting.AcceptMission(); waiting.Dispatch();
        Assert.That(Field<Coroutine>(waiting, "offerRoutine"), Is.Null);
        Assert.That(Field<Button>(waiting, "missionButton").gameObject.activeSelf, Is.False);
        Assert.That(Field<Button>(waiting, "dispatchButton").gameObject.activeSelf, Is.False);
        Assert.That(Field<GameObject>(waiting, "evaluationPanel").activeSelf, Is.True);
    }

    // 실제로 연결된 평가 입력을 사용한다.
    private Button ActionButton() => Field<Button>(waiting, "evaluationButton");

    // 실패했던 저장만 다시 실행하는 별도 버튼을 사용한다.
    private Button RetryButton() => Field<Button>(waiting, "retryButton");

    // 디버그용 공개 API를 추가하지 않고 테스트에서 필요한 기존 참조만 읽는다.
    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    // 실제 사용자 씬 값을 저장하지 않고 테스트 인스턴스의 대기 시간만 줄인다.
    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
}
#endif
