using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>진행 상태를 하나의 버전 있는 데이터로 읽고 저장하며, 실패를 새 게임으로 바꾸지 않는다.</summary>
public sealed class ProgressStore
{
    private readonly Func<string> readText;    // null만 저장 없음으로 취급하는 실제 읽기 작업이다.
    private readonly Action<string> writeText; // 하나의 데이터 묶음을 교체하는 실제 저장 작업이다.
    private bool hasLoaded;                   // 정상 읽기 또는 저장 없음 확인 전의 덮어쓰기를 차단한다.

    /// <summary>필수 숫자를 문자열로 보관해 누락과 유효한 0을 구분하는 저장 형식이다.</summary>
    [Serializable]
    private sealed class SaveData
    {
        public int version;             // 지원하는 저장 형식 번호이며 현재 값은 1이다.
        public string currentDay;       // 다음 수행 일차로, 누락되면 읽기 오류다.
        public string cumulativeScore;  // 완료한 날의 정수 실적 합계다.
        public string evaluationPeriod; // 아직 완료하지 않은 평가의 구간 번호다.
        public string stage;            // 일반 진행·평가 대기·해고 중 하나의 이름이다.
    }

    // 실제 실행 플랫폼에 맞는 로컬 저장을 선택한다. 저장이 없을 때 즉시 새 파일을 만들지 않는다.
    public ProgressStore()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        readText = ReadBrowser;
        writeText = WriteBrowser;
#else
        var path = Path.Combine(Application.persistentDataPath, "progress-v1.json");
        readText = () => ReadFile(path);
        writeText = text => WriteFile(path, text);
#endif
    }

    // 실제 사용자 저장과 분리한 파일 경로에도 같은 저장·복원 처리를 적용한다.
    public ProgressStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("저장 경로가 필요합니다.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        readText = () => ReadFile(fullPath);
        writeText = text => WriteFile(fullPath, text);
    }

    // 플랫폼 작업만 교체해 읽기·쓰기 실패를 재현하며 진행 규칙과 직렬화는 그대로 검증한다.
    public ProgressStore(Func<string> readText, Action<string> writeText)
    {
        this.readText = readText ?? throw new ArgumentNullException(nameof(readText));
        this.writeText = writeText ?? throw new ArgumentNullException(nameof(writeText));
    }

    // 저장 없음과 손상·접근 오류를 구분하고, 정상 확인 후에만 이후 저장을 허용한다.
    public bool TryLoad(out GameProgress progress, out bool hasSave, out string error)
    {
        progress = null;
        hasSave = false;
        error = null;
        hasLoaded = false;
        try
        {
            var text = readText();
            if (text == null)
            {
                progress = new GameProgress();
                hasLoaded = true;
                return true;
            }
            if (text.Length == 0 || text.Length > 4096) throw new FormatException("저장 데이터 크기가 올바르지 않습니다.");
            var data = JsonUtility.FromJson<SaveData>(text);
            if (data == null || data.version != 1
                || !int.TryParse(data.currentDay, NumberStyles.None, CultureInfo.InvariantCulture, out var day)
                || !long.TryParse(data.cumulativeScore, NumberStyles.None, CultureInfo.InvariantCulture, out var score)
                || !int.TryParse(data.evaluationPeriod, NumberStyles.None, CultureInfo.InvariantCulture, out var period)
                || !Enum.TryParse(data.stage, false, out ProgressStage stage)
                || data.stage != stage.ToString()
                || !GameProgress.TryRestore(day, score, period, stage, out progress))
                throw new FormatException("저장된 진행 상태가 올바르지 않습니다.");
            hasLoaded = true;
            hasSave = true;
            return true;
        }
        catch (Exception exception)
        {
            progress = null;
            error = exception.Message;
            return false;
        }
    }

    // 정상 읽기 이후 유효한 후보만 저장한다. 성공 여부만 반환하고 현재 게임 상태는 바꾸지 않는다.
    public bool TrySave(GameProgress candidate, out string error)
    {
        error = null;
        if (!hasLoaded)
        {
            error = "진행 데이터를 정상적으로 읽기 전에는 저장할 수 없습니다.";
            return false;
        }
        if (candidate == null)
        {
            error = "저장할 진행 상태가 없습니다.";
            return false;
        }
        try
        {
            var data = new SaveData
            {
                version = 1,
                currentDay = candidate.CurrentDay.ToString(CultureInfo.InvariantCulture),
                cumulativeScore = candidate.CumulativeScore.ToString(CultureInfo.InvariantCulture),
                evaluationPeriod = candidate.EvaluationPeriod.ToString(CultureInfo.InvariantCulture),
                stage = candidate.Stage.ToString()
            };
            writeText(JsonUtility.ToJson(data));
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    // File.Exists가 숨길 수 있는 접근 오류를 보존하고 실제 미존재만 null로 반환한다.
    private static string ReadFile(string path)
    {
        try { return File.ReadAllText(path, Encoding.UTF8); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    // 같은 디렉터리의 임시 파일을 완전히 쓴 뒤 교체해 기존 정상 저장을 보호한다.
    private static void WriteFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var temporaryPath = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(text);
                writer.Flush();
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporaryPath, path, null);
            else File.Move(temporaryPath, path);
        }
        finally
        {
            // 실패 시 임시 파일 정리는 원래 저장이나 원래 예외를 손상시키지 않는다.
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    // JavaScript가 할당한 UTF-8 문자열로 저장 내용 또는 실패 응답을 전달받는다.
    [DllImport("__Internal")] private static extern string CodeBlueProgressRead();
    // 전체 JSON 쓰기가 끝난 뒤 JavaScript의 성공·실패 응답을 전달받는다.
    [DllImport("__Internal")] private static extern string CodeBlueProgressWrite(string text);

    // 브라우저 저장 없음과 접근 실패를 접두부로 구분한다.
    private static string ReadBrowser()
    {
        var response = CodeBlueProgressRead();
        if (response == "M") return null;
        if (!string.IsNullOrEmpty(response) && response[0] == 'D') return response.Substring(1);
        throw new IOException("브라우저 저장 데이터를 읽지 못했습니다.");
    }

    // localStorage 쓰기가 예외 없이 완료된 응답을 받은 뒤에만 저장 성공으로 처리한다.
    private static void WriteBrowser(string text)
    {
        if (CodeBlueProgressWrite(text) != "S") throw new IOException("브라우저에 진행 데이터를 저장하지 못했습니다.");
    }
#endif
}
