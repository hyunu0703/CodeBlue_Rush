using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>기존 결과 본문 영역에 판정과 점수를 나누어 배치하고 순서대로 도장 연출을 표시한다.</summary>
[DisallowMultipleComponent]
public sealed class MissionResultStamp : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float stampSeconds = 0.175f; // 기존 재생 속도의 80%로 한 항목이 커지는 시간(초)이다.
    [SerializeField, Min(0f)] private float intervalSeconds = 0.1f; // 다음 판정 또는 점수를 찍기 전 쉬는 시간(초)이다.
    [SerializeField, Range(0.1f, 0.9f)] private float startScale = 0.45f; // 도장을 찍기 시작할 때의 크기 비율이다.
    private readonly List<TMP_Text> stamps = new List<TMP_Text>(); // 판정 다음 점수 순서로 재생할 본문 조각이다.
    private TMP_Text source;                     // 씬에 저장된 글꼴과 본문 영역의 기준이다.
    private float row;                           // 구역 사이 여백을 포함한 현재 줄 위치다.
    private const float RowCount = 14f;           // 사망·하한 보정과 남은 점수까지 들어가는 최대 줄 수다.
    private Vector2 previousSize;                // 화면 크기가 달라질 때만 글자 크기를 다시 맞춘다.
    private Image separator;                     // 판정 내역과 실적 합계를 나누는 수평선이다.
    private TMP_Text dailyStamp;                 // 구분선을 공개하는 시점인 당일 실적 조각이다.

    // 씬의 기존 참조를 유지하고 결과 표시 시점에만 본문 조각을 준비한다.
    public void Prepare(TMP_Text template, MissionResult result)
    {
        source = template;
        source.enabled = false;
        foreach (var stamp in stamps)
        {
            stamp.gameObject.SetActive(false);
            Destroy(stamp.gameObject);
        }
        stamps.Clear();
        row = 0f;
        AddRow($"{result.OriginalProgress.CurrentDay}일차 후송 결과");
        row += 0.5f;
        var healthScore = (int)Math.Round(result.HealthPercent, MidpointRounding.AwayFromZero);
        AddRow($"환자 잔여 HP: {PerformanceRules.FormatHealth(result.HealthPercent)}", $"+{healthScore}");
        var state = PerformanceRules.GetPatientState(result.HealthPercent);
        AddRow("환자 상태: " + (state == PatientState.Stable ? "안정" : state == PatientState.AtRisk ? "위험"
            : state == PatientState.Critical ? "위독" : "사망"));
        row += 0.5f;
        var hasVehicleCollision = result.LowCollisionCount > 0 || result.HighCollisionCount > 0;
        AddRow("차량 충돌: " + (hasVehicleCollision ? "있음" : "없음"));
        AddRow($"저속 충돌: {result.LowCollisionCount}회", $"-{(long)result.LowCollisionCount * PerformanceRules.LowPenalty}");
        AddRow($"고속 충돌: {result.HighCollisionCount}회", $"-{(long)result.HighCollisionCount * PerformanceRules.HighPenalty}");
        row += 0.5f;
        AddRow($"시민 충돌: {result.CitizenCollisionCount}명", $"-{(long)result.CitizenCollisionCount * PerformanceRules.CitizenPenalty}");
        var safe = result.HealthPercent > 0d && !hasVehicleCollision && result.CitizenCollisionCount == 0;
        AddRow("무사고 보너스" + (safe ? "" : ": 취소"), safe ? $"+{PerformanceRules.SafeBonus}" : "+0");
        // 사망과 하한 적용은 단순 가감 합계와 달라지는 이유를 결과에도 명시한다.
        if (state == PatientState.Dead) AddRow("사망 처리: 당일 실적 0점");
        else if (result.DailyScore == 0) AddRow("최저 실적: 0점 적용");
        row += 0.5f;
        PrepareSeparator();
        AddRow($"당일 실적 {result.DailyScore}점", rightAligned: true);
        dailyStamp = stamps[stamps.Count - 1];
        AddRow($"누적 실적: {result.NextProgress.CumulativeScore}점", rightAligned: true);
        if (result.NextProgress.RequiredScore > 0L)
            AddRow($"남은 점수 {result.NextProgress.RequiredScore}점", rightAligned: true);
        FitText();
    }

    // 전체 문장을 함께 확대하고 판정의 연출이 끝난 다음 우측 점수를 재생한다.
    public IEnumerator Play()
    {
        foreach (var stamp in stamps)
        {
            while (ApplicationPause.IsPaused) yield return null;
            if (stamp == dailyStamp) separator.gameObject.SetActive(true);
            stamp.alpha = 1f;
            var elapsed = 0f;
            while (elapsed < stampSeconds)
            {
                elapsed += ApplicationPause.ActiveDeltaTime;
                var progress = Mathf.Clamp01(elapsed / stampSeconds);
                var scale = Mathf.Lerp(startScale, 1f, 1f - Mathf.Pow(1f - progress, 3f));
                stamp.rectTransform.localScale = Vector3.one * scale;
                yield return null;
            }
            stamp.rectTransform.localScale = Vector3.one;
            elapsed = 0f;
            while (elapsed < intervalSeconds)
            {
                elapsed += ApplicationPause.ActiveDeltaTime;
                yield return null;
            }
        }
    }

    // 당일 실적 바로 위 여백에 본문 너비의 얇은 직선을 배치하고 합계 공개까지 숨긴다.
    private void PrepareSeparator()
    {
        if (separator == null)
        {
            var item = new GameObject("Result Separator", typeof(RectTransform), typeof(Image));
            item.transform.SetParent(source.transform, false);
            separator = item.GetComponent<Image>();
            separator.color = new Color(source.color.r, source.color.g, source.color.b, 0.55f);
            separator.raycastTarget = false;
        }
        var rect = separator.rectTransform;
        var height = 1f - (row - 0.12f) / RowCount;
        rect.anchorMin = new Vector2(0f, height);
        rect.anchorMax = new Vector2(1f, height);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, 1.5f);
        separator.gameObject.SetActive(false);
    }

    // 점수가 있는 줄은 좌우 영역을 나누고 마지막 실적은 문장 전체를 우측에 붙인다.
    private void AddRow(string label, string score = null, bool rightAligned = false)
    {
        AddStamp(label, 0f, score == null ? 1f : 0.78f, rightAligned);
        if (score != null)
        {
            AddStamp(score, 0.8f, 1f, true);
            stamps[stamps.Count - 1].color = new Color(0.95f, 0.16f, 0.2f, 0f);
        }
        row += 1f;
    }

    // 원본 글꼴과 색을 이어받되 각 조각이 독립된 중심을 기준으로 확대되도록 만든다.
    private void AddStamp(string text, float left, float right, bool rightAligned)
    {
        var item = new GameObject("Result Stamp", typeof(RectTransform), typeof(TextMeshProUGUI));
        item.transform.SetParent(source.transform, false);
        var stamp = item.GetComponent<TextMeshProUGUI>();
        stamp.font = source.font;
        stamp.fontSharedMaterial = source.fontSharedMaterial;
        stamp.fontStyle = source.fontStyle;
        stamp.color = source.color;
        stamp.raycastTarget = false;
        stamp.textWrappingMode = TextWrappingModes.NoWrap;
        stamp.overflowMode = TextOverflowModes.Overflow;
        stamp.alignment = rightAligned ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
        stamp.text = text;
        stamp.alpha = 0f;
        var rect = stamp.rectTransform;
        rect.anchorMin = new Vector2(left, 1f - (row + 1f) / RowCount);
        rect.anchorMax = new Vector2(right, 1f - row / RowCount);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one * startScale;
        stamps.Add(stamp);
    }

    // 긴 점수와 작은 화면에서도 줄바꿈이나 좌우 겹침 없이 공통 글자 크기를 적용한다.
    private void FitText()
    {
        previousSize = source.rectTransform.rect.size;
        if (previousSize.x <= 0f || previousSize.y <= 0f) return;
        var size = Mathf.Min(source.fontSizeMax, previousSize.y / RowCount * 0.72f);
        foreach (var stamp in stamps)
        {
            stamp.fontSize = size;
            var width = previousSize.x * (stamp.rectTransform.anchorMax.x - stamp.rectTransform.anchorMin.x);
            var preferred = stamp.GetPreferredValues(stamp.text).x;
            if (preferred > width) size *= width / preferred;
        }
        foreach (var stamp in stamps) stamp.fontSize = size;
    }

    private void LateUpdate()
    {
        if (source != null && source.rectTransform.rect.size != previousSize) FitText();
    }

    private void OnDisable()
    {
        // 중단 후 다시 켜진 결과창에는 읽지 못한 항목 없이 완성된 결과를 보여준다.
        if (separator != null) separator.gameObject.SetActive(true);
        foreach (var stamp in stamps)
        {
            stamp.alpha = 1f;
            stamp.rectTransform.localScale = Vector3.one;
        }
    }
}
