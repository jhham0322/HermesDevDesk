namespace Hermes.Core;
public static class ApprovalPolicy
{
    public static string? BlockReason(ServerRequest request, string root, string? turnId, bool writable, bool explicitlyAllowCommandEscalation = false)
    {
        var p = request.Params;
        if (request.Method == "mcpServer/elicitation/request")
        {
            if (string.IsNullOrEmpty(turnId) || p.Text("turnId") != turnId || p.Text("serverName") != McpConfiguration.Name) return "현재 로컬 MCP 도구의 확인 요청이 아닙니다.";
            if (p.Text("mode") != "form" || p.Property("requestedSchema")?.Property("properties")?.Property("allow")?.Text("type") != "boolean") return "외부 URL/인증/민감 입력 form은 지원하지 않습니다.";
            return null;
        }
        if (string.IsNullOrEmpty(turnId) || p.Text("turnId") != turnId) return "현재 작업의 요청이 아닙니다.";
        if (request.Method == "item/tool/requestUserInput") return null;
        if (request.Method == "item/fileChange/requestApproval")
        {
            if (!writable) return "읽기 전용 작업입니다.";
            var grant = p.Text("grantRoot");
            if (!string.IsNullOrEmpty(grant) && !Safety.Within(root, grant)) return "프로젝트 밖 쓰기 요청입니다.";
            return null;
        }
        if (request.Method == "item/commandExecution/requestApproval")
        {
            if (p.Property("additionalPermissions") is { ValueKind: not System.Text.Json.JsonValueKind.Null } || p.Property("networkApprovalContext") is { ValueKind: not System.Text.Json.JsonValueKind.Null }) return "추가 권한/네트워크 확대는 지원하지 않습니다.";
            var cwd = p.Text("cwd");
            if (string.IsNullOrEmpty(cwd) || !Safety.Within(root, cwd)) return "명령 작업 경로를 확인할 수 없습니다.";
            try { Safety.RejectReparseAncestors(cwd); } catch (IOException e) { return e.Message; }
            // No unsandboxed escalation acceptance. Approval cannot be assumed to preserve the sandbox.
            return explicitlyAllowCommandEscalation ? null : "명령 승인으로 샌드박스 밖 실행이 가능해질 수 있습니다. 이 명령에 한하여 별도 확인해야 합니다.";
        }
        return "지원하지 않는 권한/외부 도구 요청입니다.";
    }
}
