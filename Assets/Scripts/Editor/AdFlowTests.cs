#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>실제 GamePlay 결과 버튼과 광고 화면으로 노출, 보상, 저장 실패와 앱 이탈을 검증한다.</summary>
public sealed class AdFlowTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic; // 씬에 연결된 입력과 검사 지점에만 접근한다.
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;     // 실행 세션과 동기화 판정을 검사한다.
    private MissionController mission;       // GamePlay 씬의 실제 결과 컨트롤러다.
    private DummyAds ads;                     // 씬에 연결된 광고 공급자와 실제 전체 화면이다.
    private GameProgress original;           // 결과를 표시하기 전 정상적으로 저장된 해당 일차다.
    private int successfulWrites;            // 실패를 주입한 뒤 실제로 채택된 저장 횟수다.
    private bool failWrite;                  // 파일 교체 실패와 같은 예외를 일으킨다.

    // 테스트 중 실제 사용자 진행 파일과 Firebase에는 쓰지 않는다.
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return new EnterPlayMode(!EditorSettings.enterPlayModeOptionsEnabled
            || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0);
        successfulWrites = 0;
        failWrite = false;
        PrepareProgress(2);
        yield return SceneManager.LoadSceneAsync("GamePlay");
        mission = UnityEngine.Object.FindFirstObjectByType<MissionController>();
        ads = Get<DummyAds>(mission, "ads");
        ads.enabled = false;
        Set(ads, "askSeconds", .01f);
        foreach (var network in Get<Array>(ads, "networks"))
            network.GetType().GetField("fillRate").SetValue(network, 1f);
        ads.enabled = true;
        ShowResult();
        yield return new WaitForSecondsRealtime(.08f);
    }

    // 배속과 소리가 광고 테스트 뒤 Editor의 다른 테스트에 남지 않게 한다.
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (ads != null) ads.enabled = false;
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    // 실제 전면 광고의 노출 뒤 앱 이탈을 주입하여 타이머와 배속의 두 정지 사유를 검증한다.
    [UnityTest]
    public IEnumerator Interstitial_RequiresSaveAndWaitsForCloseAcrossFocusLoss()
    {
        var confirm = Get<Button>(mission, "confirmButton");
        confirm.onClick.Invoke();
        confirm.onClick.Invoke();
        Assert.That(successfulWrites, Is.EqualTo(1));
        Assert.That(GameProgressSession.Current.CurrentDay, Is.EqualTo(3));
        var overlay = Get<GameObject>(ads, "overlay");
        var close = Get<Button>(ads, "closeButton");
        Assert.That(overlay.activeSelf, Is.True);
        Assert.That(close.gameObject.activeSelf, Is.False);
        Assert.That(Time.timeScale, Is.Zero);
        Assert.That(AudioListener.pause, Is.True);

        var pause = UnityEngine.Object.FindFirstObjectByType<ApplicationPause>();
        Invoke(pause, "SetFocus", false);
        var remaining = Get<float>(ads, "remaining");
        yield return new WaitForSecondsRealtime(.12f);
        Assert.That(Get<float>(ads, "remaining"), Is.EqualTo(remaining));
        Invoke(pause, "SetFocus", true);
        yield return null;
        yield return null;
        Assert.That(ApplicationPause.IsPaused, Is.False);
        Assert.That(Time.timeScale, Is.Zero);
        yield return WaitUntilTime(() => close.gameObject.activeSelf, 5);
        close.onClick.Invoke();
        close.onClick.Invoke();
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(AudioListener.pause, Is.False);
        yield return WaitUntilTime(() => SceneManager.GetActiveScene().name == "Waiting", 5);
        Assert.That(successfulWrites, Is.EqualTo(1));
    }

    // 조기 종료와 완주를 같은 결과에서 수행하고, 저장 재시도가 보상을 잃거나 반복하지 않는지 검사한다.
    [UnityTest]
    public IEnumerator Rewarded_EarlyCloseAndSaveFailureKeepOneEarnedSkip()
    {
        var button = Get<Button>(mission, "rewardButton");
        var confirm = Get<Button>(mission, "confirmButton");
        var close = Get<Button>(ads, "closeButton");
        Assert.That(button.gameObject.activeInHierarchy, Is.True);
        button.onClick.Invoke();
        Assert.That(confirm.interactable, Is.False);
        close.onClick.Invoke();
        Assert.That(Get<bool>(mission, "rewardEarned"), Is.False);
        yield return WaitUntilTime(() => button.gameObject.activeInHierarchy, 3);
        button.onClick.Invoke();
        yield return WaitUntilTime(() => Get<float>(ads, "remaining") <= 0f, 5);
        close.onClick.Invoke();
        Assert.That(Get<bool>(mission, "rewardEarned"), Is.True);
        Assert.That(button.gameObject.activeSelf, Is.False);
        Assert.That(Get<TMPro.TMP_Text>(mission, "rewardStatus").gameObject.activeInHierarchy, Is.True);
        failWrite = true;
        confirm.onClick.Invoke();
        Assert.That(successfulWrites, Is.Zero);
        Assert.That(GameProgressSession.Current, Is.SameAs(original));
        Assert.That(Get<bool>(mission, "rewardEarned"), Is.True);
        failWrite = false;
        confirm.onClick.Invoke();
        confirm.onClick.Invoke();
        Assert.That(successfulWrites, Is.EqualTo(1));
        Assert.That(Get<GameObject>(ads, "overlay").activeSelf, Is.False);
        Assert.That(GameProgressSession.Current.CurrentDay, Is.EqualTo(3));
        yield return WaitUntilTime(() => SceneManager.GetActiveScene().name == "Waiting", 5);
        var waiting = UnityEngine.Object.FindFirstObjectByType<WaitingMissionController>();
        var request = Get<Button>(waiting, "missionButton");
        // 씬 초기화가 끝난 뒤 인증 판정만 허용해 테스트 계정으로도 REST와 사용자 저장에는 접근하지 않는다.
        var identity = FirebaseSession.Instance;
        Set(identity, "token", "editor-test");
        try
        {
            Set(waiting, "observedProgress", GameProgressSession.Current);
            Invoke(waiting, "BeginWaiting");
            Assert.That(request.gameObject.activeInHierarchy, Is.True);
            waiting.OpenBriefing();
            waiting.RejectMission();
            Assert.That(request.gameObject.activeSelf, Is.False);
            var rejectedAt = Time.realtimeSinceStartupAsDouble;
            yield return WaitUntilTime(() => request.gameObject.activeInHierarchy, 7);
            Assert.That(Time.realtimeSinceStartupAsDouble - rejectedAt, Is.GreaterThanOrEqualTo(4.9d));
        }
        finally
        {
            Set(identity, "token", null);
        }
    }

    // 평가 대기 판정과 이미 준비된 광고가 있던 No Fill에서도 결과 확인을 허용한다.
    [UnityTest]
    public IEnumerator NoFillAndEvaluation_HideRewardWithoutBlockingConfirm()
    {
        var button = Get<Button>(mission, "rewardButton");
        PrepareProgress(5);
        ShowResult();
        Assert.That(button.gameObject.activeSelf, Is.False);
        PrepareProgress(2);
        ShowResult();
        Assert.That(button.gameObject.activeSelf, Is.True);
        Set(ads, "noFill", true);
        yield return WaitUntilTime(() => !button.gameObject.activeSelf, 3);
        Assert.That(button.gameObject.activeSelf, Is.False);
        Get<Button>(mission, "confirmButton").onClick.Invoke();
        Assert.That(successfulWrites, Is.EqualTo(1));
        Assert.That(Get<GameObject>(ads, "overlay").activeSelf, Is.False);
        yield return WaitUntilTime(() => SceneManager.GetActiveScene().name == "Waiting", 5);
    }

    // 같은 세션에서 1일차 조건과 30초 경계값을 실제 판정 함수로 확인한다.
    [UnityTest]
    public IEnumerator Interstitial_FrequencySurvivesSceneBoundaries()
    {
        Assert.That((bool)AdSession("CanShowInterstitial", 1, 100d), Is.False);
        Assert.That((bool)AdSession("CanShowInterstitial", 2, 100d), Is.True);
        AdSession("RecordInterstitial", 100d);
        Assert.That((bool)AdSession("CanShowInterstitial", 3, 129.99d), Is.False);
        Assert.That((bool)AdSession("CanShowInterstitial", 3, 130d), Is.True);
        yield break;
    }

    // Inspector의 100회 시뮬레이션으로 두 워터폴 순서와 비딩의 결과를 Console에 남긴다.
    [UnityTest]
    public IEnumerator Mediation_ComparesBothWaterfallOrdersAndBidding()
    {
        var networks = Get<Array>(ads, "networks");
        var listed = new[] { .5f, .6f, .8f };
        for (var index = 0; index < networks.Length; index++)
        {
            var network = networks.GetValue(index);
            network.GetType().GetField("fillRate").SetValue(network, listed[index]);
        }
        Set(ads, "askSeconds", .5f);
        Debug.Log("광고 시뮬레이션: 워터폴 A→B→C");
        Invoke(ads, "Simulate");
        Array.Reverse(networks);
        Debug.Log("광고 시뮬레이션: 워터폴 C→B→A");
        Invoke(ads, "Simulate");
        Set(ads, "useBidding", true);
        Debug.Log("광고 시뮬레이션: 비딩");
        Invoke(ads, "Simulate");
        yield break;
    }

    // 실제 로컬 저장 담당에 메모리 쓰기를 주입해 플레이어 저장과 테스트를 분리한다.
    private void PrepareProgress(int day)
    {
        var store = new ProgressStore(() => null, _ =>
        {
            if (failWrite) throw new IOException("광고 흐름 검증용 저장 실패");
            successfulWrites++;
        });
        typeof(GameProgressSession).GetField("store", Static).SetValue(null, store);
        Assert.That(GameProgressSession.TryLoad(out var error), Is.True, error);
        Assert.That(GameProgress.TryRestore(day, 50, 1, ProgressStage.Ready, out original), Is.True);
        Assert.That(GameProgressSession.TrySave(original, out error), Is.True, error);
        successfulWrites = 0;
    }

    // 시뮬레이션의 출동 운전 대신 인계 직후 확정된 결과를 주입하고 실제 결과 버튼을 실행한다.
    private void ShowResult()
    {
        Set(mission, "result", new MissionResult(original, 60, 0, 0));
        var stage = mission.GetType().GetField("stage", Instance);
        stage.SetValue(mission, Enum.Parse(stage.FieldType, "Result"));
        Invoke(mission, "RefreshView");
    }

    // 조건이 성립하지 않을 때 무한 대기 대신 관찰한 제한 시간을 보고한다.
    private static IEnumerator WaitUntilTime(Func<bool> condition, double seconds)
    {
        var deadline = Time.realtimeSinceStartupAsDouble + seconds;
        while (!condition())
        {
            Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline));
            yield return null;
        }
    }

    // Editor 테스트를 위해 씬 필드를 공개 API로 추가하지 않는다.
    private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, Instance).GetValue(owner);

    // 인증과 저장의 실제 사용자 상태를 쓰지 않고 필요한 씬 조건만 바꾼다.
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Instance).SetValue(owner, value);

    // 앱 이탈 처리와 표시 갱신은 제품의 메서드를 그대로 호출한다.
    private static void Invoke(object owner, string name, params object[] args) =>
        owner.GetType().GetMethod(name, Instance).Invoke(owner, args);

    // 씬 사이에서 보존하는 내부 판정은 공개 API 없이 경계값을 확인한다.
    private static object AdSession(string name, params object[] args) =>
        typeof(GameProgressSession).Assembly.GetType("MissionAdSession").GetMethod(name, Static).Invoke(null, args);
}

/// <summary>통신 응답이 유실되어도 같은 ID를 다시 더하지 않는 본문 병합을 검증한다.</summary>
public sealed class AdStatsMergeTests
{
    // 실제 서버 본문을 다시 읽어 이미 처리한 전면 노출과 새 보상형 노출을 함께 보낸다.
    [Test]
    public void RetriedImpression_IsCountedOnlyOnce()
    {
        var full = "i" + Guid.NewGuid().ToString("N");
        var reward = "r" + Guid.NewGuid().ToString("N");
        var merge = typeof(FirebaseSession).GetMethod("TryMergeAdImpressions", BindingFlags.Static | BindingFlags.NonPublic);
        var first = new object[] { "null", new HashSet<string> { full }, null, false };
        Assert.That(merge.Invoke(null, first), Is.True);
        var repeat = new object[] { JsonUtility.ToJson(first[2]), new HashSet<string> { full, reward }, null, false };
        Assert.That(merge.Invoke(null, repeat), Is.True);
        Assert.That(repeat[3], Is.True);
        var json = JsonUtility.ToJson(repeat[2]);
        Assert.That(json, Does.Contain("\"interstitial\":1").And.Contain("\"rewarded\":1"));
        var acknowledged = new object[] { json, new HashSet<string> { full, reward }, null, false };
        Assert.That(merge.Invoke(null, acknowledged), Is.True);
        Assert.That(acknowledged[3], Is.False);
    }

    // 서버 응답이 예상한 카운터 형식과 다르면 기존 값을 덮어쓰지 않는다.
    [TestCase("{\"interstitial\":-1}")]
    [TestCase("{\"rewarded\":\"2\"}")]
    [TestCase("{\"interstitial\":1,\"extra-field\":false}")]
    [TestCase("{\"processed\":\"r00000000000000000000000000000000\"}")]
    public void InvalidAdStatsPayload_IsRejected(string json)
    {
        var merge = typeof(FirebaseSession).GetMethod("TryMergeAdImpressions", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(merge.Invoke(null, new object[] { json, new HashSet<string>(), null, false }), Is.False);
    }
}
#endif
