using System;
using System.IO;
using System.Runtime.InteropServices;
#if !UNITY_WEBGL || UNITY_EDITOR
using System.Security.Cryptography;
using System.Text;
#endif
using UnityEngine;

/// <summary>광고마다 독립된 항목을 사용해 탭이나 Editor가 겹쳐도 다른 미전송 기록을 덮어쓰지 않는다.</summary>
internal static class FirebaseAdStore
{
    // URL과 UID를 파일명에 직접 넣지 않고 해당 조합의 미전송 항목만 조회한다.
    internal static string Scope(string database, string uid)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return Uri.EscapeDataString(database) + ":" + Uri.EscapeDataString(uid);
#else
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(database + "\n" + uid))).Replace("-", "");
#endif
    }

    // 종류 한 글자와 UUID를 한 항목으로 저장하며 사용자 입력을 경로로 사용하지 않는다.
    internal static bool IsImpressionId(string id) =>
        id != null && id.Length == 33 && (id[0] == 'i' || id[0] == 'r')
        && Guid.TryParseExact(id.Substring(1), "N", out _);

    // 기록 전송 전에 항목을 하나 생성하고 확인 응답을 받으면 같은 항목만 제거한다.
    internal static void SetPending(string scope, string id, bool pending)
    {
        if (!IsImpressionId(id)) throw new ArgumentException("잘못된 광고 노출 ID입니다.", nameof(id));
#if UNITY_WEBGL && !UNITY_EDITOR
        if (CodeBlueAdStore(scope, id, pending ? 1 : 0) != "S")
            throw new IOException("광고 노출 저장소를 갱신할 수 없습니다.");
#else
        var directory = GetDirectory(scope);
        var path = Path.Combine(directory, id);
        if (pending)
        {
            Directory.CreateDirectory(directory);
            using var marker = File.Open(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
            marker.Flush(true);
        }
        else File.Delete(path);
#endif
    }

    // 현재 DB와 손님에 해당하는 독립 저장 항목을 읽는다.
    internal static string[] ReadPending(string scope)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var response = CodeBlueAdStore(scope, "", 2);
        if (string.IsNullOrEmpty(response) || response[0] != 'S')
            throw new IOException("광고 노출 저장소를 읽을 수 없습니다.");
        return response.Substring(1).Split(',', StringSplitOptions.RemoveEmptyEntries);
#else
        var directory = GetDirectory(scope);
        if (!Directory.Exists(directory)) return Array.Empty<string>();
        var files = Directory.GetFiles(directory);
        return Array.ConvertAll(files, Path.GetFileName);
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    // 브라우저에서 항목 제거·추가·열거를 동기 실행하고 성공 여부를 받는다.
    [DllImport("__Internal")] private static extern string CodeBlueAdStore(string scope, string id, int operation);
#else
    // 진행 및 인증 저장을 변경하지 않고 광고 미전송 파일만 별도 폴더로 보낸다.
    private static string GetDirectory(string scope) =>
        Path.Combine(Application.persistentDataPath, "firebase-adstats-v1", scope);
#endif
}
