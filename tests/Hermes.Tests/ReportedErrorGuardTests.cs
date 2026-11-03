using System.Text.Json;
using Hermes.Core;
internal static class ReportedErrorGuardTests
{
    internal static async Task Run(string root, Action<bool,string> check)
    {
        foreach(var url in new[]{"https://example.invalid/path?q=1","HTTP://EXAMPLE.INVALID:8080/","https://example.invalid/a%20b","localhost:3000","http://[::1]:3000/"})
            check(BrowserTools.ValidateUrl(url).UserInfo.Length==0,"supported credential-free URL remains valid: "+url);
        foreach(var url in new[]{"https://user:synthetic@example.invalid/","file:///C:/fixture","javascript:alert(1)",""})
        { try{BrowserTools.ValidateUrl(url);throw new Exception("Unsafe URL accepted");}catch(HermesException){check(true,"userinfo or unsupported URL remains blocked");} }
        Directory.CreateDirectory(root);await using var desk=new DeskController(Path.Combine(root,"data"),a=>a(),true);var project=desk.Register(root);var task=desk.NewTask(project,"Owned resume guards");var p=desk.BeginProcess(task,"Preserve authorized continuation");p.State="중단됨";task.Status="중단됨";
        async Task Guard(string expected){try{await desk.ResumeSavedProcessAsync(p.Id);throw new Exception("Guard missing");}catch(HermesException ex){check(ex.Message.Contains(expected),"resume guard preserves "+expected);}}
        p.UserStopped=true;await Guard("명시적으로");p.UserStopped=false;p.BlockKind="approval";await Guard("명시적으로");
        p.BlockKind="";await Guard("같은 모델");
        p.Operations.Add(new(){Key="uncertain",State="unknown"});
        try{await desk.ResumeSavedProcessAsync(p.Id,true);throw new Exception("Unknown operation replay accepted");}catch(HermesException ex){check(ex.Message.Contains("미확인"),"explicit confirmation cannot replay an unknown side effect");}
    }
}
