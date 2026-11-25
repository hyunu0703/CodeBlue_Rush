using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public sealed partial class FirebaseSession
{
    /// <summary>한 UID의 두 카운터와 이미 처리한 노출 ID를 같은 ETag로 교체하는 자료다.</summary>
    [Serializable]
    private sealed class AdStatsData
    {
        public long interstitial;      // 서버에 반영된 전면 광고의 누적 횟수다.
        public long rewarded;          // 서버에 반영된 보상형 광고의 누적 횟수다.
        public string processed = "";  // 종류를 포함한 ID를 쉼표로 끝내며 뒤에만 추가한다.
    }

    private const long MaximumJsonInteger = 9007199254740991L; // Realtime Database가 정확히 표현할 정수 범위다.
    private readonly Dictionary<string, HashSet<string>> volatileAdImpressions = new Dictionary<string, HashSet<string>>(); // 로컬 쓰기 실패 시 현재 실행에서 보존할 항목이다.

    // 화면이 열렸을 때만 호출하며 다음 씬이나 같은 손님 로그인에서 재시도할 ID를 만든다.
    internal void RecordAdImpression(DummyAds.Kind kind)
    {
        if (!IsLoggedIn) return;
        var scope = FirebaseAdStore.Scope(settings.DatabaseUrl, Uid);
        var id = (kind == DummyAds.Kind.Interstitial ? "i" : "r") + Guid.NewGuid().ToString("N");
        if (!SaveAdImpression(scope, id, true))
        {
            if (!volatileAdImpressions.TryGetValue(scope, out var pending))
                volatileAdImpressions.Add(scope, pending = new HashSet<string>(StringComparer.Ordinal));
            pending.Add(id);
        }
        RetrySync();
    }

    // 저장 오류가 화면 전환을 막지 않도록 기록하고 동일 ID는 메모리 또는 디스크에 남긴다.
    private bool SaveAdImpression(string scope, string id, bool pending)
    {
        try
        {
            FirebaseAdStore.SetPending(scope, id, pending);
            return true;
        }
        catch (Exception)
        {
            Debug.LogWarning("광고 노출 기록의 로컬 저장소를 갱신하지 못했습니다.", this);
            return false;
        }
    }

    // 다른 손님이 남긴 기록은 읽지 않고 로컬 쓰기에 실패한 항목도 다시 보관한다.
    private HashSet<string> ReadAdImpressions()
    {
        var pending = new HashSet<string>(StringComparer.Ordinal);
        if (!IsLoggedIn) return pending;
        var scope = FirebaseAdStore.Scope(settings.DatabaseUrl, Uid);
        try
        {
            foreach (var id in FirebaseAdStore.ReadPending(scope))
                if (FirebaseAdStore.IsImpressionId(id)) pending.Add(id);
        }
        catch (Exception)
        {
            Debug.LogWarning("광고 노출 기록의 로컬 저장소를 읽지 못했습니다.", this);
        }
        if (!volatileAdImpressions.TryGetValue(scope, out var cached)) return pending;
        foreach (var id in cached)
        {
            pending.Add(id);
            SaveAdImpression(scope, id, true);
        }
        return pending;
    }

    // 보존하던 ID만 서버 확인 뒤 제거하고 동기화 도중 새로 들어온 기록은 다음 시도에 남긴다.
    private void AcknowledgeAdImpressions(string scope, HashSet<string> pending)
    {
        volatileAdImpressions.TryGetValue(scope, out var cached);
        foreach (var id in pending)
        {
            SaveAdImpression(scope, id, false);
            cached?.Remove(id);
        }
        if (cached?.Count == 0) volatileAdImpressions.Remove(scope);
    }

    // 기존 카운터를 보존하며 같은 ID는 응답 유실 뒤에도 한 번만 더한다.
    private static bool TryMergeAdImpressions(string json, HashSet<string> pending, out AdStatsData data, out bool changed)
    {
        data = null;
        changed = false;
        if (json.Trim() == "null") data = new AdStatsData();
        else
        {
            try
            {
                // 숫자나 문자열이 아닌 값과 지원하지 않는 필드를 덮어쓰지 않는다.
                var fields = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match field in Regex.Matches(json, @"""(?<name>(?:[^""\\]|\\.)*)""\s*:\s*(?:(?<count>[0-9]+)|""(?<ids>[ir0-9a-f,]*)"")?\s*(?=[,}])"))
                {
                    var name = field.Groups["name"].Value;
                    if (!fields.Add(name)) return false;
                    if (name == "processed")
                    {
                        if (!field.Groups["ids"].Success) return false;
                    }
                    else if ((name != "interstitial" && name != "rewarded") || !field.Groups["count"].Success) return false;
                }
                if (fields.Count == 0 || fields.Count != Regex.Matches(json, @"""(?:[^""\\]|\\.)*""\s*:").Count) return false;
                data = JsonUtility.FromJson<AdStatsData>(json);
            }
            catch (Exception) { return false; }
        }
        if (data == null || data.interstitial < 0 || data.rewarded < 0 || data.interstitial > MaximumJsonInteger
            || data.rewarded > MaximumJsonInteger) return false;
        var processed = data.processed ?? "";
        var known = new HashSet<string>(StringComparer.Ordinal);
        if (processed.Length != 0)
        {
            if (processed[processed.Length - 1] != ',') return false;
            foreach (var id in processed.Substring(0, processed.Length - 1).Split(','))
                if (!FirebaseAdStore.IsImpressionId(id) || !known.Add(id)) return false;
        }
        var appended = new StringBuilder(processed);
        foreach (var id in pending)
        {
            if (!FirebaseAdStore.IsImpressionId(id) || !known.Add(id)) continue;
            if (id[0] == 'i') data.interstitial++;
            else data.rewarded++;
            appended.Append(id).Append(',');
            changed = true;
        }
        if (data.interstitial > MaximumJsonInteger || data.rewarded > MaximumJsonInteger) return false;
        data.processed = appended.ToString();
        return true;
    }

    // 같은 인증 전송 루틴에서 ETag 충돌과 만료 토큰을 처리하고 성공하지 않은 항목을 유지한다.
    private IEnumerator SyncAdStats()
    {
        var pending = ReadAdImpressions();
        if (pending.Count == 0) yield break;
        var uid = Uid;
        var scope = FirebaseAdStore.Scope(settings.DatabaseUrl, uid);
        var refreshed = false;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (Time.realtimeSinceStartupAsDouble >= expiresAt)
            {
                if (refreshed) break;
                refreshed = true;
                yield return Authenticate();
                if (Uid != uid || Time.realtimeSinceStartupAsDouble >= expiresAt) break;
            }
            var url = settings.DatabaseUrl + "/adstats/" + Uri.EscapeDataString(uid) + ".json?auth=" + Uri.EscapeDataString(token);
            string etag;
            AdStatsData data;
            bool changed;
            using (var get = UnityWebRequest.Get(url))
            {
                get.SetRequestHeader("X-Firebase-ETag", "true");
                yield return Send(get);
                if (get.responseCode == 401 && !refreshed) { expiresAt = 0; continue; }
                if (get.result != UnityWebRequest.Result.Success
                    || !TryMergeAdImpressions(get.downloadHandler.text, pending, out data, out changed)) break;
                etag = get.GetResponseHeader("ETag");
            }
            if (changed)
            {
                if (string.IsNullOrEmpty(etag)) break;
                using var put = UnityWebRequest.Put(url, JsonUtility.ToJson(data));
                put.SetRequestHeader("Content-Type", "application/json");
                put.SetRequestHeader("If-Match", etag);
                yield return Send(put);
                if (put.responseCode == 412) continue;
                if (put.responseCode == 401 && !refreshed) { expiresAt = 0; continue; }
                if (put.result != UnityWebRequest.Result.Success) break;
            }
            AcknowledgeAdImpressions(scope, pending);
            Debug.Log($"광고 노출 동기화 완료: 전면 {data.interstitial}, 보상형 {data.rewarded}", this);
            yield break;
        }
        Debug.LogWarning("광고 노출 동기화에 실패했습니다. 미전송 기록을 유지합니다.", this);
    }
}
