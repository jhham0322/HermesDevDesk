namespace Hermes.Core;

public sealed record ErrorRecord(string Key, string Tool, string Purpose, string Error, int Occurrences, string LastSeen, int ImprovementCount, string Status, string Notes)
{
    public override string ToString() => $"{Status} · {Tool} · {Occurrences}회 · 개선 {ImprovementCount}건 · {Error[..Math.Min(100, Error.Length)]}";
}
public sealed record RecoveryRecord(long Id, string ErrorKey, string Tool, long MethodId, long EvidenceId, string Method, string Notes, string At)
{
    public override string ToString() => $"#{Id} · {Tool} · 성공 근거 #{EvidenceId} · {Method[..Math.Min(100, Method.Length)]}";
}
public sealed partial class ExecutionMemory
{
    public IReadOnlyList<ErrorRecord> Errors(string scope, string query = "", int limit = 30)
    {
        lock (sync)
        {
            using var db = Open(); var terms = Terms(query).Take(40).ToArray();
            using var command = Command(db, """
                SELECT a.error_key,a.tool,a.purpose,a.outcome,COUNT(*),MAX(a.at),
                  (SELECT COUNT(*) FROM recoveries r WHERE r.scope=a.scope AND r.error_key=a.error_key AND r.tool=a.tool),
                  COALESCE(n.status,'미해결'),COALESCE(n.notes,'')
                FROM attempts a LEFT JOIN error_notes n ON n.scope=a.scope AND n.error_key=a.error_key AND n.tool=a.tool WHERE a.scope=$s AND a.succeeded=0
                """ + (terms.Length > 0 ? " AND a.error_key IN (SELECT x.error_key FROM attempts x JOIN error_search f ON f.rowid=x.id WHERE x.scope=$s AND error_search MATCH $q)" : "") +
                " GROUP BY a.error_key,a.tool ORDER BY MAX(a.id) DESC LIMIT $limit", ("$s", scope), ("$limit", Math.Clamp(limit, 1, 100)));
            if (terms.Length > 0) command.Parameters.AddWithValue("$q", string.Join(" OR ", terms.Select(t => "\"" + t.Replace("\"", "\"\"") + "\"")));
            using var row = command.ExecuteReader(); var result = new List<ErrorRecord>();
            while (row.Read()) result.Add(new(row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3), row.GetInt32(4), row.GetString(5), row.GetInt32(6), row.GetString(7), row.GetString(8)));
            return result;
        }
    }
    public IReadOnlyList<RecoveryRecord> Recoveries(string scope, string query = "", int limit = 30)
    {
        lock (sync)
        {
            using var db = Open();
            var keys = query.Length == 0 ? null : Errors(scope, query, 100).Select(e => e.Key).ToHashSet();
            var terms = Terms(query).ToHashSet();
            using var command = Command(db, "SELECT id,error_key,tool,method_id,evidence_id,method,notes,at FROM recoveries WHERE scope=$s ORDER BY id DESC", ("$s", scope));
            using var row = command.ExecuteReader(); var result = new List<RecoveryRecord>();
            while (row.Read() && result.Count < Math.Clamp(limit, 1, 100))
                if (keys == null || keys.Contains(row.GetString(1)) || Terms(row.GetString(5) + " " + row.GetString(6)).Any(terms.Contains)) result.Add(new(row.GetInt64(0), row.GetString(1), row.GetString(2), row.GetInt64(3), row.GetInt64(4), row.GetString(5), row.GetString(6), row.GetString(7)));
            return result;
        }
    }
    public void UpdateError(string scope, string key, string tool, string notes, string status)
    {
        if (status is not ("미해결" or "해결 확인")) throw new HermesException("지원하지 않는 오류 상태입니다.");
        lock (sync)
        {
            using var db = Open(); using var exists = Command(db, "SELECT COUNT(*) FROM attempts WHERE scope=$s AND error_key=$key AND tool=$tool AND succeeded=0", ("$s", scope), ("$key", key), ("$tool", tool));
            if (Convert.ToInt32(exists.ExecuteScalar()) == 0) throw new HermesException("현재 프로젝트의 오류를 선택하세요.");
            Execute(db, "INSERT INTO error_notes(scope,error_key,tool,status,notes) VALUES($s,$key,$tool,$status,$notes) ON CONFLICT(scope,error_key,tool) DO UPDATE SET status=$status,notes=$notes", ("$s", scope), ("$key", key), ("$tool", tool), ("$status", status), ("$notes", Clean(notes, 4000)));
        }
    }
    public void UpdateRecovery(string scope, long id, string notes)
    {
        lock (sync)
        {
            using var db = Open(); Execute(db, "BEGIN IMMEDIATE");
            try
            {
                using var proof = Command(db, "SELECT COUNT(*) FROM recoveries r JOIN attempts a ON a.id=r.evidence_id AND a.scope=r.scope AND a.tool=r.tool AND a.method=r.method AND a.succeeded=1 WHERE r.scope=$s AND r.id=$id", ("$s", scope), ("$id", id));
                if (Convert.ToInt32(proof.ExecuteScalar()) != 1) throw new HermesException("해당 개선 방법의 성공 실행 근거가 없습니다.");
                Execute(db, "UPDATE recoveries SET notes=$n WHERE scope=$s AND id=$id", ("$n", Clean(notes, 4000)), ("$s", scope), ("$id", id));
                Execute(db, "INSERT INTO recovery_notes(recovery_id,notes,at) VALUES($id,$n,$at)", ("$id", id), ("$n", Clean(notes, 4000)), ("$at", DateTimeOffset.UtcNow.ToString("O")));
                Execute(db, "COMMIT");
            }
            catch { Execute(db, "ROLLBACK"); throw; }
        }
    }
    public string RecoveryContext(string scope, string task, ExecutionEvidence evidence)
    {
        if (evidence.Succeeded) return "";
        lock (sync)
        {
            using var db = Open(); using var count = Command(db, "SELECT COUNT(*) FROM attempts WHERE scope=$s AND task=$t AND tool=$tool AND error_key=$e AND succeeded=0", ("$s", scope), ("$t", task), ("$tool", evidence.Tool), ("$e", evidence.ErrorKey));
            if (Convert.ToInt32(count.ExecuteScalar()) < 2) return "";
        }
        return "\n[자동 개선 전환] 같은 오류가 2회 이상 발생했습니다. 동일 방법을 반복하지 말고 원인을 진단한 뒤 명령·인자·도구·접근 방법을 실질적으로 바꾸세요. 아래 과거 근거를 참고해 대안을 실행하고 목표를 검증하세요. 성공한 대안은 오류별 개선 목록에 자동 연결됩니다.\n" + Context(scope, evidence.Purpose + " " + evidence.Outcome);
    }
}
