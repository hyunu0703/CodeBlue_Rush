#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>결과 내역 배치와 순차 표시, 중단 시 복원을 실제 TMP 컴포넌트로 검증한다.</summary>
public sealed class MissionResultStampTests
{
    private GameObject root; // 검증이 끝나면 함께 제거할 결과 본문이다.

    // 저장된 결과창과 같은 글꼴을 사용해 판정과 점수의 실제 폭을 계산한다.
    private MissionResultStamp Create(MissionResult result, float width = 360f)
    {
        root = new GameObject("Stamp Test", typeof(RectTransform), typeof(TextMeshProUGUI));
        var source = root.GetComponent<TextMeshProUGUI>();
        source.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath("6119818e450cedf4fa78f902a374f431"));
        source.fontSizeMax = 26f;
        source.rectTransform.sizeDelta = new Vector2(width, 520f);
        var effect = root.AddComponent<MissionResultStamp>();
        effect.Prepare(source, result);
        return effect;
    }

    // 실패하더라도 다음 검증에 임시 본문을 남기지 않는다.
    [TearDown]
    public void TearDown()
    {
        if (root != null) Object.DestroyImmediate(root);
    }

    // 첨부 예시의 HP 반올림과 기존 감점이 별도 도장 순서에 그대로 반영된다.
    [Test]
    public void Breakdown_UsesScoreRulesAndKeepsDailyScoreOnOneRightAlignedLine()
    {
        Create(new MissionResult(new GameProgress(), 53.7d, 1, 1));
        var stamps = root.GetComponentsInChildren<TMP_Text>().Skip(1).ToArray();
        CollectionAssert.AreEqual(new[] {
            "1일차 후송 결과", "환자 잔여 HP: 53.7%", "+54", "환자 상태: 위험",
            "차량 충돌: 있음", "저속 충돌: 1회", "-1", "고속 충돌: 1회", "-3",
            "시민 충돌: 0명", "-0", "무사고 보너스: 취소", "+0", "당일 실적 50점", "누적 실적: 50점", "남은 점수 150점"
        }, stamps.Select(item => item.text).ToArray());
        var daily = stamps.Single(item => item.text == "당일 실적 50점");
        Assert.That(daily.alignment, Is.EqualTo(TextAlignmentOptions.MidlineRight));
        Assert.That(daily.textWrappingMode, Is.EqualTo(TextWrappingModes.NoWrap));
        Assert.That(stamps.All(item => item.alpha == 0f), Is.True);
    }

    // 평가 당일에도 이번 결과를 포함한 부족량만 표시하고 달성 시에는 남은 점수를 숨긴다.
    [TestCase(1, 0L, 150L)]
    [TestCase(5, 100L, 50L)]
    [TestCase(5, 150L, 0L)]
    [TestCase(5, 200L, 0L)]
    public void Totals_AlignAndOnlyShowRemainingTargetWhenNeeded(int day, long cumulative, long remaining)
    {
        Assert.That(GameProgress.TryRestore(day, cumulative, 1, ProgressStage.Ready, out var progress), Is.True);
        Create(new MissionResult(progress, 53.7d, 1, 1));
        var stamps = root.GetComponentsInChildren<TMP_Text>().Skip(1).ToArray();
        var daily = stamps.Single(item => item.text == "당일 실적 50점");
        var total = stamps.Single(item => item.text == $"누적 실적: {cumulative + 50L}점");
        Assert.That(total.alignment, Is.EqualTo(daily.alignment));
        Assert.That(total.rectTransform.anchorMax.x, Is.EqualTo(daily.rectTransform.anchorMax.x));
        Assert.That(total.rectTransform.anchorMax.y, Is.EqualTo(daily.rectTransform.anchorMin.y).Within(0.0001f));
        if (remaining > 0L)
        {
            var required = stamps.Single(item => item.text == $"남은 점수 {remaining}점");
            Assert.That(required.alignment, Is.EqualTo(daily.alignment));
            Assert.That(required.rectTransform.anchorMax.y, Is.EqualTo(total.rectTransform.anchorMin.y).Within(0.0001f));
        }
        else Assert.That(stamps.Any(item => item.text.StartsWith("남은 점수")), Is.False);
        var line = root.GetComponentInChildren<Image>(true);
        Assert.That(line.rectTransform.anchorMin.y, Is.GreaterThan(daily.rectTransform.anchorMax.y));
        Assert.That(line.rectTransform.anchorMin.x, Is.Zero);
        Assert.That(line.rectTransform.anchorMax.x, Is.EqualTo(1f));
        Assert.That(line.gameObject.activeSelf, Is.False);
    }

    // 좁은 영역의 큰 수치, 사망과 무사고 보너스도 실제 배점과 모순 없이 표시한다.
    [TestCase(100d, 0, 0, 0, "무사고 보너스", "+30", "당일 실적 130점")]
    [TestCase(0d, 1, 1, 1, "사망 처리: 당일 실적 0점", "-5", "당일 실적 0점")]
    [TestCase(1d, 99999, 99999, 99999, "최저 실적: 0점 적용", "-499995", "당일 실적 0점")]
    public void EdgeCases_FitWithoutOverlapping(double health, int low, int high, int citizens, string label, string score, string daily)
    {
        Create(new MissionResult(new GameProgress(), health, low, high, citizens), 240f);
        var stamps = root.GetComponentsInChildren<TMP_Text>().Skip(1).ToArray();
        Assert.That(stamps.Select(item => item.text), Does.Contain(label).And.Contain(score).And.Contain(daily));
        foreach (var stamp in stamps)
        {
            var width = 240f * (stamp.rectTransform.anchorMax.x - stamp.rectTransform.anchorMin.x);
            Assert.That(stamp.GetPreferredValues(stamp.text).x, Is.LessThanOrEqualTo(width + 0.1f));
            Assert.That(stamp.rectTransform.anchorMin.y, Is.GreaterThanOrEqualTo(0f));
        }
    }

    // 제목 다음 판정, 그 다음 점수 순서와 화면 비활성화 시 완성 상태 복원을 확인한다.
    [UnityTest]
    public IEnumerator Animation_RevealsLabelBeforeScoreAndRestoresOnDisable()
    {
        yield return new EnterPlayMode();
        var effect = Create(new MissionResult(new GameProgress(), 53.7d, 1, 1));
        var stamps = root.GetComponentsInChildren<TMP_Text>().Skip(1).ToArray();
        var routine = effect.StartCoroutine(effect.Play());
        Assert.That(stamps[0].alpha, Is.EqualTo(1f));
        Assert.That(stamps[1].alpha, Is.Zero);
        var timeout = Time.realtimeSinceStartup + 3f;
        while (stamps[1].alpha == 0f && Time.realtimeSinceStartup < timeout) yield return null;
        Assert.That(stamps[1].alpha, Is.EqualTo(1f));
        Assert.That(stamps[2].alpha, Is.Zero);
        Assert.That(stamps[1].rectTransform.localScale.x, Is.LessThan(1f));
        effect.StopCoroutine(routine);
        root.SetActive(false);
        Assert.That(stamps.All(item => item.alpha == 1f && item.rectTransform.localScale == Vector3.one), Is.True);
        Assert.That(root.GetComponentInChildren<Image>(true).gameObject.activeSelf, Is.True);
        Object.Destroy(root);
        root = null;
        yield return new ExitPlayMode();
    }
}
#endif
