using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>인증 복원 정보와 미전송 최고값을 진행 저장과 분리하고 쓰기 실패를 호출부에 전달한다.</summary>
internal static class FirebaseLocalStore
{
    // 브라우저는 동기 localStorage 응답으로, 다른 플랫폼은 파일로 읽기 성공을 확인한다.
    internal static string Read()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var response = CodeBlueFirebaseRead();
        if (response == "M") return null;
        if (!string.IsNullOrEmpty(response) && response[0] == 'D') return response.Substring(1);
        throw new IOException("인증 저장소를 읽을 수 없습니다.");
#else
        try { return File.ReadAllText(Path.Combine(Application.persistentDataPath, "firebase-v1.json"), Encoding.UTF8); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
#endif
    }

    // 같은 저장 단위의 토큰·UID·미전송 값을 함께 교체하고 기존 정상 파일을 보호한다.
    internal static void Write(string json)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (CodeBlueFirebaseWrite(json) != "S") throw new IOException("인증 저장소에 쓸 수 없습니다.");
#else
        var path = Path.Combine(Application.persistentDataPath, "firebase-v1.json");
        var temporary = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern string CodeBlueFirebaseRead(); // JS가 할당한 UTF-8 응답이다.
    [DllImport("__Internal")] private static extern string CodeBlueFirebaseWrite(string json); // 동기 쓰기 결과다.
#endif
}
