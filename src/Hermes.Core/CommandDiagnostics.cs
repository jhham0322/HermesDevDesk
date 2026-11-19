using System.Text.Json;
using System.Text.RegularExpressions;
namespace Hermes.Core;
internal static class CommandDiagnostics
{
    internal static string RetryIdentity(string command,string cwd,string projectRoot)
        =>string.Equals(Path.GetFullPath(cwd).TrimEnd('\\'),Path.GetFullPath(projectRoot).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)?command:System.Text.Json.JsonSerializer.Serialize(new{command,cwd=Path.GetFullPath(cwd).ToUpperInvariant()});
    // Only declared dependencies are probed. Never infer input files from arbitrary
    // shell text: a missing path may intentionally be created by that command.
    internal static string Preflight(string projectRoot, JsonElement args)
    {
        try
        {
            var cwd=args.Text("cwd");
            cwd=Safety.ProjectRoot(cwd.Length==0?projectRoot:Path.GetFullPath(cwd,projectRoot));
            using(var entries=Directory.EnumerateFileSystemEntries(cwd).GetEnumerator())entries.MoveNext();
            if(args.Property("inputFiles") is { } inputs)
            {
                if(inputs.ValueKind!=JsonValueKind.Array||inputs.GetArrayLength()>100)throw new HermesException("inputFiles must be an array of at most 100 declared input paths.");
                foreach(var value in inputs.EnumerateArray())
                {
                    if(value.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(value.GetString()))throw new HermesException("Each inputFiles entry must be a nonempty path.");
                    Readable(Path.GetFullPath(value.GetString()!,cwd));
                }
            }
            var tool=args.Text("buildTool");
            if(tool.Length>0)
            {
                var explicitPath=Path.IsPathRooted(tool)||tool.Contains('/')||tool.Contains('\\');
                var paths=explicitPath?new[]{Path.GetFullPath(tool,cwd)}:(Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator).Where(p=>p.Length>0).Select(p=>Path.Combine(p.Trim('"'),tool));
                var extensions=Path.HasExtension(tool)?new[]{""}:new[]{""}.Concat((Environment.GetEnvironmentVariable("PATHEXT")??".EXE;.CMD;.BAT").Split(';'));
                var found=paths.SelectMany(p=>extensions.Select(e=>p+e)).FirstOrDefault(File.Exists);
                if(found==null)throw new HermesException("Declared buildTool was not found. Correct the path before retrying.");
                Readable(found);
            }
            return cwd;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {throw new HermesException("[COMMAND_PREFLIGHT] No command was executed. Correct declared cwd/inputFiles/buildTool before retrying. "+ExecutionMemory.Clean(ex.Message,1000));}
    }
    private static void Readable(string path)
    {
        if(path.Split('\\','/').Any(p=>p is ".codex" or ".aws" or ".ssh" or "credentials" or "secrets")||Safety.IsSecret(Path.GetFileName(path)))throw new HermesException("Sensitive declared input paths are not probed.");
        Safety.RejectReparseAncestors(path);
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
    }
    internal static string Display(string output,int limit)
    {
        // Scan the complete received output before preview truncation. Do not infer
        // errors that an upstream engine has already omitted.
        Match match;
        try{match=Regex.Match(output,@"(?im)^[ \t]*[^\r\n]*\.java:\d+:(?:\d+:)?[ \t]*error:[^\r\n]*",RegexOptions.None,TimeSpan.FromMilliseconds(200));}
        catch(RegexMatchTimeoutException){return ExecutionMemory.Clean(output,limit);}
        if(!match.Success)return ExecutionMemory.Clean(output,limit);
        var end=match.Index+match.Length;
        for(var lines=0;lines<12&&end<output.Length;lines++){var next=output.IndexOf('\n',end+1);end=next<0?output.Length:next;}
        var block=output.Substring(match.Index,Math.Min(end-match.Index,64000));
        var details=block.Split('\n').Where(l=>l.TrimStart().StartsWith("symbol:",StringComparison.Ordinal)||l.TrimStart().StartsWith("location:",StringComparison.Ordinal)).Take(2);
        var diagnostic=ExecutionMemory.Clean(match.Value,1000)+"\n"+string.Join("\n",details.Select(l=>ExecutionMemory.Clean(l,500)));
        diagnostic=diagnostic[..Math.Min(diagnostic.Length,Math.Min(3000,limit/2))];
        var prefix="[Observed compiler diagnostic]\n"+diagnostic+"\n[Output preview]\n";
        return prefix+ExecutionMemory.Clean(output,Math.Max(0,limit-prefix.Length));
    }
}
