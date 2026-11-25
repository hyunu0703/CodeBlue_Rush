using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>씬 전환을 넘어 익명 인증과 UID별 최고 기록 제출을 이어가며 로컬 진행에는 서버 값을 덮어쓰지 않는다.</summary>
public sealed partial class FirebaseSession : MonoBehaviour
{
    /// <summary>진행 초기화와 독립적으로 유지할 한 손님의 인증·미전송 저장 단위다.</summary>
    [Serializable]
    private sealed class LocalData
    {
        public int version = 1;             // 현재 지원하는 인증 저장 형식이다.
        public string database;             // 다른 Firebase 프로젝트로의 기록 오전송을 차단한다.
        public string uid;                  // refresh token과 미전송 점수의 소유자다.
        public string refreshToken;         // 같은 손님으로 재로그인할 갱신 자격이며 암호화되지 않는다.
        public string pending = "-1";       // -1은 미전송 기록 없음, 나머지는 정수 최고 후보다.
    }

    /// <summary>익명 가입과 갱신의 서로 다른 응답 이름을 함께 읽는다.</summary>
    [Serializable]
    private sealed class AuthResponse
    {
        public string localId, user_id;             // 가입·갱신 응답의 UID다.
        public string idToken, id_token;            // 메모리에서만 사용하는 ID token이다.
        public string refreshToken, refresh_token;  // 가입·갱신 뒤 저장할 최신 갱신 자격이다.
        public string expiresIn, expires_in;        // 토큰이 유효한 시간(초)이다.
    }

    /// <summary>서버 공개 경로에는 표시 이름과 정수 최고 실적만 기록한다.</summary>
    [Serializable]
    private sealed class ScoreData
    {
        public string name; // 서버에 공개할 손님 이름이다.
        public long score;  // int 범위를 줄이지 않은 누적 최고 실적이다.
    }

    private static FirebaseSession instance;             // 실행 중 하나만 존재하는 통신 관리자다.
    private FirebaseSettings settings;                   // Resources/FirebaseSettings의 공개 설정이다.
    private LocalData local;                             // 마지막 정상 읽기·쓰기의 인증 저장 단위다.
    private bool localLoaded;                            // 손상 저장을 새 손님으로 덮어쓰지 않도록 막는다.
    private string token;                                // 로그아웃·종료 때 버리는 현재 ID token이다.
    private double expiresAt;                            // 단조 증가 실행 시간 기준 갱신 시각이다.
    private bool authenticating;                         // 로그인 요청의 중복 실행을 막는다.
    private bool syncing;                                // 점수 요청을 직렬 실행한다.
    private bool syncAgain;                              // 전송 중 들어온 다음 재시도 요청이다.
    private long volatilePending = -1;                   // 저장 실패 시에도 실행 중 높은 후보를 보존한다.
    private readonly HashSet<UnityWebRequest> requests = new HashSet<UnityWebRequest>(); // 종료 때 중단할 요청이다.

    public event Action Changed;                         // 화면이 인증·동기화 상태 변경 때만 갱신하도록 알린다.
    public bool IsLoggedIn => !string.IsNullOrEmpty(token);
    public bool IsBusy => authenticating || syncing;
    public string Uid => IsLoggedIn ? local.uid : string.Empty;
    public string DisplayName => IsLoggedIn ? "손님 " + Uid.Substring(0, Math.Min(6, Uid.Length)) : "로그인 필요";
    public long? BestScore { get; private set; }
    public string Status { get; private set; } = "게스트 로그인 후 시작할 수 있습니다.";

    public static FirebaseSession Instance
    {
        get
        {
            if (instance == null)
            {
                var root = new GameObject("Firebase Session");
                instance = root.AddComponent<FirebaseSession>();
                DontDestroyOnLoad(root);
            }
            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        instance = null;
    }

    private void Awake()
    {
        settings = Resources.Load<FirebaseSettings>("FirebaseSettings");
    }

    private void OnDestroy()
    {
        CancelRequests();
        token = null;
        if (instance == this) instance = null;
    }

    // 중단된 코루틴의 finally 실행 여부에 의존하지 않고 네이티브 요청도 정리한다.
    private void CancelRequests()
    {
        var pending = new List<UnityWebRequest>(requests);
        foreach (var request in pending) request.Abort();
        StopAllCoroutines();
        requests.Clear();
        foreach (var request in pending) request.Dispose();
    }

    // 내부 오류 응답이나 토큰 포함 URL 대신 사용자용 상태만 화면에 전달한다.
    private void SetStatus(string message)
    {
        Status = message;
        Changed?.Invoke();
    }

    // HTTPS 프로젝트 설정과 기존 인증 저장을 확인한 뒤에만 요청을 허용한다.
    private bool Prepare()
    {
        if (settings == null || string.IsNullOrWhiteSpace(settings.ApiKey)
            || !Uri.TryCreate(settings.DatabaseUrl, UriKind.Absolute, out var address)
            || address.Scheme != "https" || address.AbsolutePath != "/" || !string.IsNullOrEmpty(address.Query)
            || !string.IsNullOrEmpty(address.Fragment) || !string.IsNullOrEmpty(address.UserInfo))
        {
            SetStatus("FirebaseSettings의 API Key와 HTTPS Database URL을 설정해주세요.");
            return false;
        }
        if (localLoaded) return true;
        try
        {
            var json = FirebaseLocalStore.Read();
            var restored = json == null ? new LocalData { database = settings.DatabaseUrl } : JsonUtility.FromJson<LocalData>(json);
            if (restored == null || restored.version != 1 || restored.database != settings.DatabaseUrl
                || !long.TryParse(restored.pending, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var pending)
                || pending < -1 || pending > 9007199254740991L
                || (string.IsNullOrEmpty(restored.uid) != string.IsNullOrEmpty(restored.refreshToken))
                || (pending >= 0 && string.IsNullOrEmpty(restored.uid)))
                throw new FormatException();
            local = restored;
            localLoaded = true;
            return true;
        }
        catch (Exception)
        {
            SetStatus("인증 저장을 읽지 못했습니다. 기존 데이터를 유지합니다. 저장 권한·파일을 확인 후 재시도해주세요.");
            return false;
        }
    }

    // 쓰기가 완료된 후보만 채택하고 실패에서는 원래 토큰·소유자·미전송 값을 보존한다.
    private bool SaveLocal(LocalData candidate)
    {
        try
        {
            FirebaseLocalStore.Write(JsonUtility.ToJson(candidate));
            local = candidate;
            return true;
        }
        catch (Exception)
        {
            SetStatus("인증·미전송 기록의 로컬 저장 실패. 저장 권한을 확인하고 동기화를 재시도해주세요.");
            return false;
        }
    }

    // 미전송 점수를 바꿀 때 현재 인증 자격과 프로젝트 소유권을 함께 유지한다.
    private LocalData WithPending(long pending)
    {
        return new LocalData { database = local.database, uid = local.uid,
            refreshToken = local.refreshToken, pending = pending.ToString(CultureInfo.InvariantCulture) };
    }

    // 최초 가입 또는 같은 손님의 토큰 갱신만 수행하며 통신 실패로 계정을 교체하지 않는다.
    public void Login()
    {
        if (IsBusy || IsLoggedIn || !Prepare()) return;
        StartCoroutine(Authenticate());
    }

    // 요청은 씬 객체 대신 서비스가 소유해 씬 이동 때 코루틴이 끊기지 않게 한다.
    private IEnumerator Send(UnityWebRequest request)
    {
        request.timeout = 20;
        requests.Add(request);
        yield return request.SendWebRequest();
        requests.Remove(request);
    }

    // 가입·갱신 응답이 유효하고 로컬 복원 자격 저장이 성공해야 로그인을 확정한다.
    private IEnumerator Authenticate()
    {
        authenticating = true;
        SetStatus("로그인 중…");
        var fresh = string.IsNullOrEmpty(local.refreshToken);
        UnityWebRequest request;
        if (fresh)
        {
            request = new UnityWebRequest("https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=" + Uri.EscapeDataString(settings.ApiKey), "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{\"returnSecureToken\":true}"));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
        }
        else
        {
            request = UnityWebRequest.Post("https://securetoken.googleapis.com/v1/token?key=" + Uri.EscapeDataString(settings.ApiKey),
                new Dictionary<string, string> { { "grant_type", "refresh_token" }, { "refresh_token", local.refreshToken } });
        }
        using (request)
        {
            yield return Send(request);
            if (request.result != UnityWebRequest.Result.Success)
            {
                authenticating = false;
                SetStatus(request.responseCode == 400 || request.responseCode == 403
                    ? "인증 거부: 익명 인증·API Key·기존 계정 상태를 확인 후 재시도해주세요. 저장된 자격은 유지됩니다."
                    : "로그인 통신 실패. 연결을 확인하고 재시도해주세요. 기존 손님 정보는 유지됩니다.");
                yield break;
            }
            AuthResponse response = null;
            try { response = JsonUtility.FromJson<AuthResponse>(request.downloadHandler.text); }
            catch (Exception) { }
            var uid = fresh ? response?.localId : response?.user_id;
            var id = fresh ? response?.idToken : response?.id_token;
            var refresh = fresh ? response?.refreshToken : response?.refresh_token;
            var expiry = fresh ? response?.expiresIn : response?.expires_in;
            if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(refresh)
                || (!fresh && uid != local.uid)
                || !double.TryParse(expiry, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
            {
                authenticating = false;
                SetStatus("인증 응답을 확인하지 못했습니다. 기존 손님을 유지하고 재시도해주세요.");
                yield break;
            }
            var candidate = WithPending(long.Parse(local.pending, CultureInfo.InvariantCulture));
            candidate.uid = uid;
            candidate.refreshToken = refresh;
            // 가입 성공 뒤 디스크 실패 시 같은 실행에서 재가입하지 않도록 자격을 메모리에 남긴다.
            if (!SaveLocal(candidate))
            {
                local = candidate;
                authenticating = false;
                yield break;
            }
            token = id;
            expiresAt = Time.realtimeSinceStartupAsDouble + Math.Max(0, seconds - 60);
        }
        authenticating = false;
        SetStatus("로그인 완료");
        if (!syncing && ReadAdImpressions().Count > 0) RetrySync();
    }

    // 서버 실패와 무관하게 높은 후보를 먼저 로컬에 남기며 초기화 직전에도 재사용한다.
    public bool PreserveScore(long score)
    {
        if (!IsLoggedIn || score < 0 || score > 9007199254740991L) return false;
        volatilePending = Math.Max(volatilePending, score);
        var pending = Math.Max(long.Parse(local.pending, CultureInfo.InvariantCulture), volatilePending);
        return SaveLocal(WithPending(pending));
    }

    // 진행 저장 성공 후 또는 Waiting 복원 시 호출하며 실행 중 높은 후보는 다음 요청으로 합친다.
    public void Submit(long score)
    {
        if (!IsLoggedIn || !PreserveScore(score)) return;
        RetrySync();
    }

    // 무한 자동 재시도 없이 사용자 입력 또는 다음 정상 진입에서 대기 기록을 재전송한다.
    public void RetrySync()
    {
        if (!IsLoggedIn || authenticating) return;
        if (syncing) { syncAgain = true; return; }
        StartCoroutine(Sync());
    }

    // 공개 JSON이 자료형·필수 필드 계약을 만족하는지 확인하고 읽기 오류를 0점으로 바꾸지 않는다.
    private static bool TryReadScore(string json, out ScoreData data)
    {
        data = null;
        if (json.Trim() == "null") return true;
        try
        {
            // 필수 숫자 필드 누락이 JsonUtility의 기본 0점으로 통과하지 않게 한다.
            data = JsonUtility.FromJson<ScoreData>(json);
            return data != null && !string.IsNullOrEmpty(data.name) && data.name.Length <= 64
                && System.Text.RegularExpressions.Regex.IsMatch(json, "\"score\"\\s*:\\s*(0|[1-9][0-9]*)\\s*[,}]")
                && data.score >= 0 && data.score <= 9007199254740991L;
        }
        catch (Exception) { return false; }
    }

    // ETag 충돌과 인증 만료에 한정된 횟수로 대응하며 성공한 후보 이하만 대기 기록에서 지운다.
    private IEnumerator Sync()
    {
        syncing = true;
        syncAgain = false;
        SetStatus("최고 실적 동기화 중…");
        var completed = false;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (Time.realtimeSinceStartupAsDouble >= expiresAt)
            {
                yield return Authenticate();
                if (Time.realtimeSinceStartupAsDouble >= expiresAt) break;
            }
            var target = Math.Max(volatilePending, long.Parse(local.pending, CultureInfo.InvariantCulture));
            if (target >= 0 && !PreserveScore(target)) break;
            var url = settings.DatabaseUrl + "/scores/" + Uri.EscapeDataString(Uid) + ".json?auth=" + Uri.EscapeDataString(token);
            string etag;
            ScoreData old;
            using (var get = UnityWebRequest.Get(url))
            {
                get.SetRequestHeader("X-Firebase-ETag", "true");
                yield return Send(get);
                if (get.responseCode == 401 && attempt == 0) { expiresAt = 0; continue; }
                if (get.result != UnityWebRequest.Result.Success)
                {
                    SetStatus("최고 실적 읽기 실패. 연결·Firebase 규칙 확인 후 동기화를 재시도해주세요.");
                    break;
                }
                if (!TryReadScore(get.downloadHandler.text, out old))
                {
                    SetStatus("서버 기록 형식 오류. 기존 기록을 덮어쓰지 않았습니다.");
                    break;
                }
                etag = get.GetResponseHeader("ETag");
                BestScore = old?.score ?? 0;
                if (target < 0 || (old != null && target <= old.score))
                {
                    if (target >= 0 && !Acknowledge(target)) break;
                    completed = true;
                    break;
                }
            }
            if (string.IsNullOrEmpty(etag))
            {
                SetStatus("ETag 헤더를 읽지 못했습니다. 브라우저 CORS 설정을 확인 후 재시도해주세요.");
                break;
            }
            var body = JsonUtility.ToJson(new ScoreData { name = DisplayName, score = target });
            using (var put = UnityWebRequest.Put(url, body))
            {
                put.SetRequestHeader("Content-Type", "application/json");
                put.SetRequestHeader("If-Match", etag);
                yield return Send(put);
                if (put.responseCode == 412) continue;
                if (put.responseCode == 401 && attempt == 0) { expiresAt = 0; continue; }
                if (put.result != UnityWebRequest.Result.Success)
                {
                    SetStatus("최고 실적 전송 실패. 미전송 기록을 유지합니다. 연결·규칙 확인 후 재시도해주세요.");
                    break;
                }
                BestScore = target;
                if (!Acknowledge(target)) break;
            }
            if (Math.Max(volatilePending, long.Parse(local.pending, CultureInfo.InvariantCulture)) < 0)
            {
                completed = true;
                break;
            }
        }
        yield return SyncAdStats();
        syncing = false;
        if (completed) SetStatus("최고 실적 동기화 완료");
        else if (Status == "최고 실적 동기화 중…" || Status == "로그인 완료") SetStatus("동기화 대기 중. 재시도해주세요.");
        else Changed?.Invoke();
        if (syncAgain) RetrySync();
    }

    // 전송 중 더 높은 후보가 추가됐으면 그대로 남기고 확인된 값 이하만 지운다.
    private bool Acknowledge(long score)
    {
        var pending = Math.Max(volatilePending, long.Parse(local.pending, CultureInfo.InvariantCulture));
        if (!SaveLocal(WithPending(pending <= score ? -1 : pending))) return false;
        if (volatilePending <= score) volatilePending = -1;
        return true;
    }

    // 진행 초기화 전 남은 후보를 보존하고 메모리 인증만 비운다. 갱신 자격은 지우지 않는다.
    public bool Logout()
    {
        if (authenticating || (GameProgressSession.IsLoaded && !PreserveScore(GameProgressSession.Current.CumulativeScore))) return false;
        CancelRequests();
        syncing = false;
        syncAgain = false;
        token = null;
        BestScore = null;
        SetStatus("로그아웃했습니다. 다시 로그인하면 같은 손님으로 복원합니다.");
        return true;
    }
}
