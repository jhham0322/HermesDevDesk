namespace Hermes.Core;
public static class GitWorkbench
{
    public static string RemoteName(string name)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"\A[a-zA-Z][a-zA-Z0-9._-]{0,63}\z")) throw new IOException("Git 원격 이름은 영문으로 시작하는 1~64자 이름입니다.");
        return name;
    }
    public static string RemoteUrl(string url)
    {
        url = url.Trim();
        if (url.Length is 0 or > 2048 || url.Any(char.IsControl) || url.StartsWith('-')) throw new IOException("원격 서버 주소를 확인하세요.");
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "ssh")
        {
            if (uri.Scheme == "https" && uri.UserInfo.Length > 0 || uri.UserInfo.Contains(':') || uri.Query.Length > 0 || uri.Fragment.Length > 0) throw new IOException("서버 주소에 비밀번호/토큰/query를 넣지 마세요. Git Credential Manager 또는 SSH 인증을 사용하세요.");
            return url;
        }
        if (System.Text.RegularExpressions.Regex.IsMatch(url, @"\A[a-zA-Z0-9._-]+@[a-zA-Z0-9.-]+:[a-zA-Z0-9/._~-]+\z")) return url;
        if (Path.IsPathFullyQualified(url) && Directory.Exists(url)) { Safety.RejectReparseAncestors(url); return Path.GetFullPath(url); }
        throw new IOException("HTTPS, SSH(git@server:path), 또는 로컬 저장소의 절대 폴더 경로를 입력하세요.");
    }
    public static async Task ConfigureRemote(string root, string name, string url)
    {
        await GitRunner.RepositoryRoot(root); name = RemoteName(name); url = RemoteUrl(url);
        var current = await GitRunner.Run(root, "remote", "get-url", name);
        var result = await GitRunner.Run(root, current.Exit == 0 ? ["remote", "set-url", name, url] : ["remote", "add", name, url]);
        if (result.Exit != 0) throw new IOException(result.Error);
    }
    public static async Task<string> Sync(string root, string remote, bool push)
    {
        await GitRunner.RepositoryRoot(root); remote = RemoteName(remote);
        var address = await GitRunner.Run(root, "remote", "get-url", remote); if (address.Exit != 0) throw new IOException(address.Error); RemoteUrl(address.Output.Trim());
        var branch = await GitRunner.Run(root, "branch", "--show-current"); if (branch.Exit != 0 || branch.Output.Trim().Length == 0) throw new IOException("일반 로컬 branch에서 실행하세요.");
        var result = await GitRunner.Run(root, push ? ["push", "--", remote, "HEAD"] : ["fetch", "--", remote]);
        if (result.Exit != 0) throw new IOException(result.Error);
        return Safety.Redact(result.Output + result.Error);
    }
    public static async Task Stage(string root, string path, bool stage)
    {
        await GitRunner.RepositoryRoot(root); Safety.Resolve(root, path);
        var result = await GitRunner.Run(root, stage ? ["add", "--", path] : ["restore", "--staged", "--", path]);
        if (result.Exit != 0) throw new IOException(result.Error);
    }
    public static async Task<string> Commit(string root, string message)
    {
        await GitRunner.RepositoryRoot(root);
        message = message.Trim(); if (message.Length is 0 or > 5000) throw new IOException("커밋 메시지를 입력하세요.");
        var staged = await GitRunner.Run(root, "diff", "--cached", "--name-only", "-z");
        if (staged.Exit != 0) throw new IOException(staged.Error);
        var paths = staged.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (paths.Length == 0) throw new IOException("스테이징한 변경이 없습니다.");
        foreach (var path in paths) Safety.Resolve(root, path);
        var result = await GitRunner.Run(root, "commit", "-m", message);
        if (result.Exit != 0) throw new IOException(result.Error);
        return Safety.Redact(result.Output);
    }
}
