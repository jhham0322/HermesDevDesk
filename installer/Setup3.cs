using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
[assembly: AssemblyVersion("0.8.68.0")]
[assembly: AssemblyFileVersion("0.8.68.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
internal static class Setup
{
    internal const string Version="0.8.68";
    internal static string Target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","HermesDevDesk","App");
    [STAThread] static int Main(string[] args)
    {
        AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling",false);
        AppContext.SetSwitch("Switch.System.IO.BlockLongPaths",false);
        try {
            if(args.Length==2&&args[0]=="/info"){File.WriteAllText(args[1],"{\"version\":\""+Version+"\",\"defaultTarget\":\""+Target.Replace("\\","\\\\")+"\"}");return 0;}
            if(args.Length==2&&args[0]=="/verify") { using(var z=Payload()){ var m=Manifest(z); Verify(z,m); File.WriteAllText(args[1],"PASS: "+m.Count+" embedded files SHA256 verified. No install.\n");} return 0;}
            if(args.Length==2&&args[0]=="/extract"){ Install(args[1],CancellationToken.None,null);return 0;}
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length==3&&args[0]=="/test-suite"){Tests(args[1],args[2]);return 0;}
            if(args.Length==3&&args[0]=="/cleanup-test"){CleanupTests(args[1],args[2]);return 0;}
            if(args.Length>0)throw new IOException("Use /verify REPORT, /extract ABSOLUTE_PATH or /test-suite ISOLATED_PATH REPORT");
            Application.Run(new SetupForm(Target,false));return 0;
        }catch(Exception e){if(args.Length>1&&(args[0]=="/verify"||args[0]=="/test-suite"||args[0]=="/cleanup-test"))File.WriteAllText(args[args.Length-1],"FAIL "+e);else if(args.Length==2&&args[0]=="/extract")File.WriteAllText(Path.GetFullPath(args[1])+".install-error.txt",e.ToString());else if(args.Length==0)MessageBox.Show(e.Message);return 1;}
    }
    static ZipArchive Payload(){var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("HermesPayload.zip");if(s==null)throw new IOException("Payload missing");return new ZipArchive(s,ZipArchiveMode.Read);}
    static string Hash(Stream s){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
    static string Identity(ZipArchive z){using(var s=z.GetEntry(".hermes-manifest.tsv").Open())return "HermesDevDesk-"+Version+"-"+Hash(s);}
    static void Root(string r){if(!Path.IsPathRooted(r)||r.StartsWith(@"\\")||Path.GetFullPath(r).TrimEnd('\\')==Path.GetPathRoot(r).TrimEnd('\\'))throw new IOException("Local non-root absolute destination required");for(var p=Path.GetFullPath(r);!String.IsNullOrEmpty(p);p=Path.GetDirectoryName(p))if((Directory.Exists(p)||File.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Reparse path blocked");}
    static void Name(string n){if(String.IsNullOrEmpty(n)||Path.IsPathRooted(n)||n.Contains(":")||n.Contains("\\"))throw new IOException("Unsafe archive path");foreach(var s in n.TrimEnd('/').Split('/')){var stem=s.Split('.')[0].ToUpperInvariant();if(s.Length==0||s=="."||s==".."||s.EndsWith(".")||s.Trim()!=s||stem=="CON"||stem=="AUX"||stem=="NUL"||stem=="PRN"||System.Text.RegularExpressions.Regex.IsMatch(stem,"^(COM|LPT)[1-9]$"))throw new IOException("Unsafe archive segment");}}
    static string FilePath(string r,string n){Name(n);var p=Path.GetFullPath(Path.Combine(r,n));if(!p.StartsWith(r.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Escaped destination");Root(p);return p;}
    static Dictionary<string,string> Manifest(ZipArchive z){var entry=z.GetEntry(".hermes-manifest.tsv");if(entry==null)throw new IOException("Manifest missing");var m=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);using(var r=new StreamReader(entry.Open()))while(!r.EndOfStream){var l=r.ReadLine();if(String.IsNullOrEmpty(l))continue;var f=l.Split('\t');if(f.Length!=2||f[0].Length!=64)throw new IOException("Invalid manifest");Name(f[1]);m.Add(f[1],f[0]);}if(!m.ContainsKey("HermesDevDesk.exe")||m.Count<10)throw new IOException("Incomplete payload");return m;}
    static void Verify(ZipArchive z,Dictionary<string,string> m){
        var info=z.GetEntry("release-info.json");if(info==null)throw new IOException("Release version metadata missing");
        using(var reader=new StreamReader(info.Open()))if(!reader.ReadToEnd().Contains("\"version\": \""+Version+"\""))throw new IOException("Installer/payload version mismatch");
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long bytes=0;foreach(var e in z.Entries){Name(e.FullName);if(!seen.Add(e.FullName))throw new IOException("Duplicate archive entry");bytes+=e.Length;if(bytes>1024L*1024*1024)throw new IOException("Payload too large");if(e.Name.Length==0||e.FullName==".hermes-manifest.tsv")continue;string hash;if(!m.TryGetValue(e.FullName,out hash))throw new IOException("Unlisted file");using(var s=e.Open())if(Hash(s)!=hash)throw new IOException("Payload hash mismatch");}foreach(var n in m.Keys)if(!seen.Contains(n))throw new IOException("Missing file");}
    static bool OwnsInstallation(string root){
        Root(root);var marker=FilePath(root,".hermes-owned");var manifest=FilePath(root,".hermes-manifest.tsv");if(!File.Exists(marker)||!File.Exists(manifest))return false;
        var identity=File.ReadAllText(marker).Trim();if(!System.Text.RegularExpressions.Regex.IsMatch(identity,@"^HermesDevDesk-\d+\.\d+\.\d+-[a-f0-9]{64}$"))return false;
        using(var stream=File.OpenRead(manifest))return identity.EndsWith("-"+Hash(stream),StringComparison.Ordinal);
    }
    static bool ReplaceableInstallation(string root){
        if(OwnsInstallation(root))return true;
        var exe=FilePath(root,"HermesDevDesk.exe");
        if(!File.Exists(exe))return false;
        var info=System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
        return String.Equals(info.ProductName,"HermesDevDesk",StringComparison.OrdinalIgnoreCase)&&String.Equals(info.OriginalFilename,"HermesDevDesk.dll",StringComparison.OrdinalIgnoreCase);
    }
    static void DeleteValidatedTree(string root){
        root=Path.GetFullPath(root);Root(root);
        var directories=new List<string>();CollectDirectories(root,directories);
        foreach(var file in Directory.GetFiles(root,"*",SearchOption.AllDirectories)){Root(file);File.SetAttributes(file,FileAttributes.Normal);}
        foreach(var directory in directories)File.SetAttributes(directory,FileAttributes.Directory);
        File.SetAttributes(root,FileAttributes.Directory);Directory.Delete(root,true);
    }
    static void WaitForExclusiveRead(string file,CancellationToken ct){
        for(var attempt=0;;attempt++){ct.ThrowIfCancellationRequested();try{using(var check=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.None)){}return;}catch(IOException error){var code=error.HResult&65535;if(attempt>=39||(code!=32&&code!=33))throw;Thread.Sleep(250);}}
    }
    internal static void Install(string r,CancellationToken ct,Action<int,string> progress){
        r=Path.GetFullPath(r);Root(r);r=r.TrimEnd(Path.DirectorySeparatorChar);
        string lockId;
        using(var name=new MemoryStream(Encoding.UTF8.GetBytes(r.ToUpperInvariant())))lockId=Hash(name);
        using(var gate=new Mutex(false,@"Local\HermesDevDesk.Install."+lockId)){
            try{if(!gate.WaitOne(0))throw new IOException("이 폴더에 다른 설치가 진행 중입니다.");}
            catch(AbandonedMutexException){}
            try{
                InstallClean(r,ct,progress);
                CleanupInterruptedInstallations(r);
            }finally{gate.ReleaseMutex();}
        }
    }
    static void InstallClean(string target,CancellationToken ct,Action<int,string> progress){
        using(var archive=Payload()){
            var manifest=Manifest(archive);
            Verify(archive,manifest);
            ct.ThrowIfCancellationRequested();
            Root(target);
            if(Directory.Exists(target)&&Directory.GetFileSystemEntries(target).Length>0){
                if(!ReplaceableInstallation(target))
                    throw new IOException("Hermes 설치 폴더로 확인되지 않아 비우지 않았습니다: "+target);
                CollectDirectories(target,new List<string>());
                ClosePreviousApp(target);
                ReleaseFileLocks(target,ct);
                foreach(var file in Directory.GetFiles(target,"*",SearchOption.AllDirectories))
                    WaitForExclusiveRead(file,ct);
                ct.ThrowIfCancellationRequested();
                DeleteValidatedTree(target);
            }
            ct.ThrowIfCancellationRequested();
            if(progress!=null)progress(0,"기존 설치 파일 정리 완료 · 새 파일 설치 준비");
            InstallPayload(target,archive,manifest,ct,progress);
        }
        ValidateInstalled(target);
    }
    static void InstallPayload(string target,ZipArchive archive,Dictionary<string,string> manifest,
        CancellationToken ct,Action<int,string> progress){
        ct.ThrowIfCancellationRequested();
        Root(target);
        if(Directory.Exists(target)&&Directory.GetFileSystemEntries(target).Length!=0)
            throw new IOException("설치 폴더에 새 파일이 생겨 작업을 중단했습니다: "+target);
        Directory.CreateDirectory(target);
        var marker=FilePath(target,".hermes-owned");
        var manifestPath=FilePath(target,".hermes-manifest.tsv");
        using(var input=archive.GetEntry(".hermes-manifest.tsv").Open())
        using(var output=new FileStream(manifestPath,FileMode.CreateNew))input.CopyTo(output);
        using(var output=new StreamWriter(new FileStream(marker,FileMode.CreateNew),new UTF8Encoding(false)))
            output.Write(Identity(archive));

        var buffer=new byte[1024*1024];
        var count=0;
        foreach(var pair in manifest){
            ct.ThrowIfCancellationRequested();
            var path=FilePath(target,pair.Key);
            var directory=Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            var temporary=Path.Combine(directory,".hermes-"+Guid.NewGuid().ToString("N")+".partial");
            try{
                using(var input=archive.GetEntry(pair.Key).Open())
                using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                    int length;
                    while((length=input.Read(buffer,0,buffer.Length))!=0){
                        ct.ThrowIfCancellationRequested();
                        output.Write(buffer,0,length);
                    }
                }
                using(var input=File.OpenRead(temporary))
                    if(Hash(input)!=pair.Value)throw new IOException("Written hash mismatch");
                ct.ThrowIfCancellationRequested();
                File.Move(temporary,path);
            }finally{if(File.Exists(temporary))File.Delete(temporary);}
            count++;
            if(progress!=null)progress(count*100/manifest.Count,pair.Key);
        }
    }
    internal static void Remove(string r,CancellationToken ct,Action<int,string> progress){Root(r);r=Path.GetFullPath(r);using(var z=Payload()){var m=Manifest(z);Verify(z,m);var marker=FilePath(r,".hermes-owned");if(!File.Exists(marker)||File.ReadAllText(marker).Trim()!=Identity(z))throw new IOException("This installer does not own that destination");ClosePreviousApp(r);ReleaseFileLocks(r,ct);int count=0;foreach(var pair in m){ct.ThrowIfCancellationRequested();var p=FilePath(r,pair.Key);if(File.Exists(p)){bool same;using(var f=File.OpenRead(p))same=Hash(f)==pair.Value;if(same)File.Delete(p);}count++;if(progress!=null)progress(count*100/m.Count,pair.Key);}}}
    internal static void ValidateInstalled(string r){Root(r);r=Path.GetFullPath(r);
        foreach(var name in new[]{"HermesDevDesk.exe","HermesDevDesk.dll"})if(System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(r,name)).FileVersion!=Version+".0")throw new IOException("Installed app version differs from installer: "+name);
using(var z=Payload()){var m=Manifest(z);var marker=FilePath(r,".hermes-owned");if(!File.Exists(marker)||File.ReadAllText(marker).Trim()!=Identity(z))throw new IOException("Install this exact version first");foreach(var pair in m){var p=FilePath(r,pair.Key);if(!File.Exists(p))throw new IOException("Incomplete installation: "+pair.Key);using(var s=File.OpenRead(p))if(Hash(s)!=pair.Value)throw new IOException("Modified app file: "+pair.Key);}}}
    static void CleanupInterruptedInstallations(string current){
        var parent=Path.GetDirectoryName(current);Root(parent);
        var prefix=Path.GetFileName(current);
        foreach(var folder in Directory.GetDirectories(parent)){
            var absolute=Path.GetFullPath(folder);
            if(!String.Equals(Path.GetDirectoryName(absolute),parent,StringComparison.OrdinalIgnoreCase))continue;
            var name=Path.GetFileName(absolute);
            var start=name.StartsWith(prefix+".staging-",StringComparison.OrdinalIgnoreCase)?prefix+".staging-":
                name.StartsWith(prefix+".previous-",StringComparison.OrdinalIgnoreCase)?prefix+".previous-":null;
            Guid suffix;
            if(start==null||!Guid.TryParseExact(name.Substring(start.Length),"N",out suffix))continue;
            if((File.GetAttributes(absolute)&FileAttributes.ReparsePoint)!=0)continue;
            if(!ReplaceableInstallation(absolute))continue;
            RemovePreviousInstallation(absolute);
        }
    }
    static void RemovePreviousInstallation(string path){
        CollectDirectories(path,new List<string>());
        ClosePreviousApp(path);
        ReleaseFileLocks(path,CancellationToken.None);
        DeleteValidatedTree(path);
    }
    internal static int CleanupPreviousVersions(string installationsRoot,string current){
        Root(installationsRoot);Root(current);installationsRoot=Path.GetFullPath(installationsRoot);current=Path.GetFullPath(current);int removed=0;
        if(!Directory.Exists(installationsRoot))return 0;
        foreach(var folder in Directory.GetDirectories(installationsRoot)){
            var absolute=Path.GetFullPath(folder);System.Version oldVersion;
            if(!String.Equals(Path.GetDirectoryName(absolute),installationsRoot,StringComparison.OrdinalIgnoreCase)||String.Equals(absolute,current,StringComparison.OrdinalIgnoreCase)||!System.Version.TryParse(Path.GetFileName(absolute),out oldVersion)||oldVersion>new System.Version(Version))continue;
            if((File.GetAttributes(absolute)&FileAttributes.ReparsePoint)!=0)continue;
            var marker=FilePath(absolute,".hermes-owned");var manifest=FilePath(absolute,".hermes-manifest.tsv");
            if(!File.Exists(marker)||!File.Exists(manifest))continue;
            var expected="HermesDevDesk-"+Path.GetFileName(absolute)+"-";using(var stream=File.OpenRead(manifest))expected+=Hash(stream);
            if(File.ReadAllText(marker).Trim()!=expected)continue;
            RemovePreviousInstallation(absolute);
            removed++;
        }
        return removed;
    }
    static void CollectDirectories(string root,List<string> directories){foreach(var entry in Directory.GetFileSystemEntries(root)){Root(entry);if(Directory.Exists(entry)){CollectDirectories(entry,directories);directories.Add(entry);}}}
    static void StopProcess(System.Diagnostics.Process process){
        if(process.Id==System.Diagnostics.Process.GetCurrentProcess().Id)throw new IOException("설치 프로그램 자신이 파일을 사용 중입니다.");
        try{
            if(process.HasExited)return;
            if(process.CloseMainWindow()&&process.WaitForExit(1500))return;
            process.Kill();
            if(!process.WaitForExit(10000))throw new IOException("프로세스 강제 종료 대기 실패: "+process.Id);
        }catch(InvalidOperationException){}
        catch(System.ComponentModel.Win32Exception e){throw new IOException("설치 파일 점유 프로세스를 종료하지 못했습니다: PID="+process.Id,e);}
    }
    static void ClosePreviousApp(string folder){
        folder=Path.GetFullPath(folder).TrimEnd('\\')+"\\";
        foreach(var process in System.Diagnostics.Process.GetProcesses())using(process){
            if(process.Id==System.Diagnostics.Process.GetCurrentProcess().Id)continue;
            string executable;try{var module=process.MainModule;if(module==null)continue;executable=module.FileName;}catch(System.ComponentModel.Win32Exception){continue;}catch(InvalidOperationException){continue;}
            if(!Path.GetFullPath(executable).StartsWith(folder,StringComparison.OrdinalIgnoreCase))continue;
            StopProcess(process);
        }
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct RmUniqueProcess{public int ProcessId;public System.Runtime.InteropServices.ComTypes.FILETIME StartTime;}
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential,CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
    struct RmProcessInfo{
        public RmUniqueProcess Process;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr,SizeConst=256)]public string AppName;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr,SizeConst=64)]public string ServiceName;
        public int ApplicationType;public uint AppStatus;public uint SessionId;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]public bool Restartable;
    }
    [System.Runtime.InteropServices.DllImport("rstrtmgr.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int RmStartSession(out uint handle,int flags,StringBuilder key);
    [System.Runtime.InteropServices.DllImport("rstrtmgr.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int RmRegisterResources(uint handle,uint count,string[] files,uint applications,IntPtr app,uint services,IntPtr names);
    [System.Runtime.InteropServices.DllImport("rstrtmgr.dll")]
    static extern int RmGetList(uint handle,out uint needed,ref uint count,[System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] RmProcessInfo[] processes,ref uint reasons);
    [System.Runtime.InteropServices.DllImport("rstrtmgr.dll")]
    static extern int RmEndSession(uint handle);
    static void ReleaseFileLocks(string root,CancellationToken ct){
        Root(root);var directories=new List<string>();CollectDirectories(root,directories);
        var files=Directory.GetFiles(root,"*",SearchOption.AllDirectories);
        if(files.Length==0)return;
        uint handle;int status=RmStartSession(out handle,0,new StringBuilder(33));
        if(status!=0)throw new IOException("파일 점유 확인 시작 실패: "+status);
        try{
            for(int offset=0;offset<files.Length;offset+=100){
                ct.ThrowIfCancellationRequested();var count=Math.Min(100,files.Length-offset);var batch=new string[count];Array.Copy(files,offset,batch,0,count);
                status=RmRegisterResources(handle,(uint)count,batch,0,IntPtr.Zero,0,IntPtr.Zero);if(status!=0)throw new IOException("파일 점유 등록 실패: "+status);
            }
            for(int attempt=0;attempt<4;attempt++){
                uint needed=0,count=0,reasons=0;status=RmGetList(handle,out needed,ref count,null,ref reasons);
                if(status==0)return;
                if(status!=234)throw new IOException("파일 점유 확인 실패: "+status);
                var processes=new RmProcessInfo[needed];count=needed;
                status=RmGetList(handle,out needed,ref count,processes,ref reasons);
                if(status==234)continue;
                if(status!=0)throw new IOException("파일 점유 목록 확인 실패: "+status);
                for(int index=0;index<count;index++){
                    ct.ThrowIfCancellationRequested();var info=processes[index];
                    if(info.ApplicationType==3||info.ApplicationType==1000)throw new IOException("시스템 서비스가 설치 파일을 사용 중입니다: "+info.AppName);
                    try{using(var process=System.Diagnostics.Process.GetProcessById(info.Process.ProcessId)){
                        long stamp=((long)(uint)info.Process.StartTime.dwHighDateTime<<32)|(uint)info.Process.StartTime.dwLowDateTime;
                        if(process.StartTime.ToFileTimeUtc()!=stamp)continue;
                        StopProcess(process);
                    }}catch(ArgumentException){}catch(InvalidOperationException){}
                }
                return; // Restart Manager may retain exited process entries; exclusive file checks below verify release.
            }
            throw new IOException("설치 파일 점유가 반복됩니다: "+root);
        }finally{RmEndSession(handle);}
    }
    internal static void UpdateShortcut(string target){
        var shortcutPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),"Programs","Hermes DevDesk.lnk");Root(shortcutPath);
        var type=Type.GetTypeFromProgID("WScript.Shell");if(type==null)throw new IOException("바로가기 생성 서비스를 찾지 못했습니다.");
        dynamic shell=Activator.CreateInstance(type);dynamic shortcut=shell.CreateShortcut(shortcutPath);
        try{shortcut.TargetPath=Path.Combine(target,"HermesDevDesk.exe");shortcut.WorkingDirectory=target;shortcut.Description="Hermes DevDesk "+Version;shortcut.Save();
            var knownRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","HermesDevDesk")+"\\";
            foreach(var location in new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),"Programs")})foreach(var name in new[]{"HermesDevDesk.lnk","Hermes DevDesk.lnk"}){
                var link=Path.Combine(location,name);if(!File.Exists(link)||String.Equals(link,shortcutPath,StringComparison.OrdinalIgnoreCase))continue;Root(link);dynamic existing=shell.CreateShortcut(link);
                try{string oldTarget=existing.TargetPath;if(!String.IsNullOrEmpty(oldTarget)&&Path.GetFullPath(oldTarget).StartsWith(knownRoot,StringComparison.OrdinalIgnoreCase)&&String.Equals(Path.GetFileName(oldTarget),"HermesDevDesk.exe",StringComparison.OrdinalIgnoreCase)){existing.TargetPath=Path.Combine(target,"HermesDevDesk.exe");existing.WorkingDirectory=target;existing.Description="Hermes DevDesk "+Version;existing.Save();}}
                finally{System.Runtime.InteropServices.Marshal.FinalReleaseComObject(existing);}
            }
        }
        finally{System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
    }
    internal static void RegisterOfficeLoader(string target){
        var exe=Path.Combine(target,"HermesDevDesk.exe");Root(exe);
        using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))key.SetValue("HermesDevDesk.OfficeAddins","\""+exe+"\" --office-addin-monitor");
        var disabled=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HermesDevDesk","office-addins","disabled");
        if(!File.Exists(disabled))System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe,"--office-addin-monitor"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden}).Dispose();
    }
    internal static void UnregisterOfficeLoader(string target){
        using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)){
            var expected="\""+Path.Combine(target,"HermesDevDesk.exe")+"\" --office-addin-monitor";
            if(key!=null&&String.Equals(key.GetValue("HermesDevDesk.OfficeAddins") as string,expected,StringComparison.OrdinalIgnoreCase)){
                key.DeleteValue("HermesDevDesk.OfficeAddins",false);
                var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HermesDevDesk","office-addins");Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"disabled"),"설치 제거로 중지됨");
            }
        }
    }
    static void CleanupTests(string root,string report){
        Root(root);if(Directory.Exists(root)&&Directory.GetFileSystemEntries(root).Length!=0)throw new IOException("Cleanup test requires an empty isolated directory");Directory.CreateDirectory(root);
        var installations=Path.Combine(root,"Programs","HermesDevDesk");Directory.CreateDirectory(installations);var current=Path.Combine(installations,"App");Directory.CreateDirectory(current);File.WriteAllText(Path.Combine(current,"keep.txt"),"CURRENT");
        Action<string> fixture=delegate(string version){var folder=Path.Combine(installations,version);Directory.CreateDirectory(folder);var rows=new List<string>();foreach(var name in new[]{"HermesDevDesk.exe","HermesDevDesk.dll","README.md"}){var file=Path.Combine(folder,name);File.WriteAllText(file,"OWNED_FIXTURE_"+version);using(var stream=File.OpenRead(file))rows.Add(Hash(stream)+"\t"+name);}var manifest=Path.Combine(folder,".hermes-manifest.tsv");File.WriteAllLines(manifest,rows,new UTF8Encoding(false));using(var stream=File.OpenRead(manifest))File.WriteAllText(Path.Combine(folder,".hermes-owned"),"HermesDevDesk-"+version+"-"+Hash(stream));};
        fixture("0.8.8");fixture("0.8.9");fixture(Version);fixture("99.0.0");var unknown=Path.Combine(installations,"0.1.0");Directory.CreateDirectory(unknown);File.WriteAllText(Path.Combine(unknown,"keep.txt"),"UNOWNED");
        var data=Path.Combine(root,"HermesDevDesk","data");Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"state.json"),"USER_HISTORY");
        var count=CleanupPreviousVersions(installations,current);
        if(count!=3||Directory.Exists(Path.Combine(installations,Version))||Directory.Exists(Path.Combine(installations,"0.8.8"))||Directory.Exists(Path.Combine(installations,"0.8.9"))||!File.Exists(Path.Combine(current,"keep.txt"))||!Directory.Exists(Path.Combine(installations,"99.0.0"))||File.ReadAllText(Path.Combine(data,"state.json"))!="USER_HISTORY"||!File.Exists(Path.Combine(unknown,"keep.txt")))throw new IOException("Old-version cleanup boundary/preservation failed");
        fixture("0.8.9");var old=Path.Combine(installations,"0.8.9");File.WriteAllText(Path.Combine(old,"user.txt"),"KEEP_USER_FILE");CleanupPreviousVersions(installations,current);if(Directory.Exists(old))throw new IOException("Previous installation was not fully removed");
        fixture("0.8.9");var locked=Path.Combine(old,"HermesDevDesk.exe");bool refused=false;using(var stream=new FileStream(locked,FileMode.Open,FileAccess.Read,FileShare.None)){try{CleanupPreviousVersions(installations,current);}catch(IOException){refused=true;}}
        if(!refused||!File.Exists(locked)||!File.Exists(Path.Combine(old,"README.md")))throw new IOException("Locked old version was partially deleted");
        var oldStage=current+".staging-"+Guid.NewGuid().ToString("N");
        var oldBackup=current+".previous-"+Guid.NewGuid().ToString("N");
        var unownedStage=current+".staging-"+Guid.NewGuid().ToString("N");
        fixture("0.8.10");Directory.Move(Path.Combine(installations,"0.8.10"),oldStage);
        fixture("0.8.11");Directory.Move(Path.Combine(installations,"0.8.11"),oldBackup);
        Directory.CreateDirectory(unownedStage);File.WriteAllText(Path.Combine(unownedStage,"keep.txt"),"UNOWNED");
        CleanupInterruptedInstallations(current);
        if(Directory.Exists(oldStage)||Directory.Exists(oldBackup)||!File.Exists(Path.Combine(unownedStage,"keep.txt"))||!File.Exists(Path.Combine(current,"keep.txt")))
            throw new IOException("Interrupted reinstall cleanup removed unrelated files or left installer leftovers");
        fixture("0.8.12");
        var orderTarget=Path.Combine(installations,"0.8.12");
        File.WriteAllText(Path.Combine(orderTarget,"stale.txt"),"REMOVE");
        var observedEmpty=false;
        Install(orderTarget,CancellationToken.None,delegate(int percent,string message){
            if(message!="기존 설치 파일 정리 완료 · 새 파일 설치 준비")return;
            if(Directory.Exists(orderTarget))throw new IOException("The new install started before old files were deleted");
            observedEmpty=true;
        });
        if(!observedEmpty||File.Exists(Path.Combine(orderTarget,"stale.txt")))
            throw new IOException("Delete-first reinstall was not observed");
        File.WriteAllText(report,"PASS old owned versions removed; current/future/unowned/data preserved; previous install and interrupted setup leftovers fully removed; installer-owned lock remains intact; old app directory absent before new files are written. Isolated fixtures only.\n");
    }
    static void Wait(SetupForm f){while(f.Last!=null&&!f.Last.IsCompleted){Application.DoEvents();Thread.Sleep(10);}Application.DoEvents();}
    static void Tests(string root,string report){
        Root(root);if(Directory.Exists(root)&&Directory.GetFileSystemEntries(root).Length!=0)throw new IOException("Test root must be empty");Directory.CreateDirectory(root);var lines=new List<string>();var path=Path.Combine(root,"install");
        using(var f=new SetupForm(path,true)){f.Show();f.InstallButton.PerformClick();var first=f.Last;f.InstallButton.PerformClick();if(first!=f.Last)throw new IOException("Duplicate click");Wait(f);if(f.Error!=null||!File.Exists(Path.Combine(path,"HermesDevDesk.exe")))throw new IOException("Install failed "+f.Error);if(f.AutomaticLaunches!=1)throw new IOException("Successful install must trigger exactly one launch");lines.Add("PASS programmatic install/repeated-click and automatic launch guard");
        f.LaunchButton.PerformClick();Wait(f);if(f.Error!=null)throw new IOException("Installed app launch failed: "+f.Error);lines.Add("PASS launch button runs installed WPF app with isolated smoke data");
        f.InstallButton.PerformClick();Wait(f);if(f.Error!=null)throw new IOException("Repeat install failed");lines.Add("PASS repeat same-version installation");
        File.WriteAllText(Path.Combine(path,"README.md"),"MODIFIED");File.WriteAllText(Path.Combine(path,"user.txt"),"KEEP");f.InstallButton.PerformClick();Wait(f);if(f.Error!=null||File.Exists(Path.Combine(path,"user.txt"))||File.ReadAllText(Path.Combine(path,"README.md"))=="MODIFIED")throw new IOException("Same-folder clean replacement failed");lines.Add("PASS same installation folder fully refreshed");File.WriteAllText(Path.Combine(path,"README.md"),"MODIFIED");File.WriteAllText(Path.Combine(path,"user.txt"),"KEEP");
        f.RemoveButton.PerformClick();Wait(f);if(f.Error!=null||File.Exists(Path.Combine(path,"HermesDevDesk.exe"))||File.ReadAllText(Path.Combine(path,"README.md"))!="MODIFIED"||File.ReadAllText(Path.Combine(path,"user.txt"))!="KEEP")throw new IOException("Removal preservation failed");lines.Add("PASS remove handler preserves modified/user files");f.Close();}
        path=Path.Combine(root,"cancel");using(var f=new SetupForm(path,true)){f.Show();f.CancelAfterProgress=true;f.InstallButton.PerformClick();Wait(f);if(f.AutomaticLaunches!=0||!f.Cancelled||(Directory.Exists(path)&&Directory.GetFiles(path,"*.partial",SearchOption.AllDirectories).Length>0))throw new IOException("Cancel failed");lines.Add("PASS cancellation skips launch and cleans private partial files");
        f.CancelAfterProgress=false;f.InstallButton.PerformClick();Wait(f);if(f.Error!=null)throw new IOException("Cancel resume failed");lines.Add("PASS recover cancelled installation");
        f.RemoveButton.PerformClick();Wait(f);f.InstallButton.PerformClick();Wait(f);if(f.Error!=null)throw new IOException("Reinstall failed");lines.Add("PASS reinstall after removal");f.Close();}
        path=Path.Combine(root,"unowned");Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"keep.txt"),"KEEP");try{Install(path,CancellationToken.None,null);throw new Exception("Unowned accepted");}catch(IOException){if(File.ReadAllText(Path.Combine(path,"keep.txt"))!="KEEP")throw;lines.Add("PASS unowned destination preserved");}
        lines.Add("Programmatic WinForms PerformClick tests only. Native mouse click verification blocked externally. Explicit isolated paths; no default installation, shortcut, registry, firewall, credential or global setting changes.");File.WriteAllLines(report,lines,new UTF8Encoding(false));
    }
}
internal sealed class SetupForm:Form{
    internal Button InstallButton=new Button{Text="새로 설치 / 재설치"},RemoveButton=new Button{Text="이 버전 제거"},CancelControl=new Button{Text="작업 취소",Enabled=false};
    internal Button LaunchButton=new Button{Text="설치한 "+Setup.Version+" 실행"};
    internal Task Last;internal string Error;internal bool Cancelled,CancelAfterProgress;internal int AutomaticLaunches;bool test;CancellationTokenSource cancellation;TextBox path=new TextBox();Label status=new Label();ProgressBar progress=new ProgressBar();
    internal SetupForm(string target,bool testing){test=testing;Text="Hermes DevDesk 0.8.68 설치";Icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);Width=700;Height=440;StartPosition=FormStartPosition.CenterScreen;MaximizeBox=false;
        Controls.Add(new Label{Left=24,Top=20,Width=640,Height=100,Text="독립 Windows 개발 에이전트 앱 · 사내 중계서버 연결\n.NET 런타임과 공식 CLI 포함. 실행 중인 이전 앱과 설치 파일 점유 프로세스를 강제 종료하고 App 폴더를 먼저 삭제한 뒤 새 파일을 설치합니다. 확인된 이전 설치 잔여 폴더도 정리합니다. 이력·설정은 별도 보존합니다.\n관리자 권한·방화벽·전역 설정 변경 없이 선택한 폴더에 설치합니다.\n회사 URL/모델명/API 키는 설치 후 앱에 직접 입력하세요."});
        path.SetBounds(24,130,530,28);path.Text=target;path.ReadOnly=true;var browse=new Button{Text="폴더 선택"};browse.SetBounds(565,128,100,30);browse.Enabled=false;browse.Click+=delegate{using(var d=new FolderBrowserDialog())if(d.ShowDialog(this)==DialogResult.OK)path.Text=d.SelectedPath;};
        progress.SetBounds(24,177,640,22);status.SetBounds(24,210,640,65);InstallButton.SetBounds(24,290,190,38);RemoveButton.SetBounds(237,290,190,38);CancelControl.SetBounds(450,290,215,38);var close=new Button{Text="닫기"};close.SetBounds(550,340,115,32);close.Click+=delegate{Close();};LaunchButton.SetBounds(24,340,190,32);
        Controls.AddRange(new Control[]{path,browse,progress,status,InstallButton,RemoveButton,CancelControl,LaunchButton,close});
        LaunchButton.Click+=delegate{if(Last==null||Last.IsCompleted)Last=Launch();};
        InstallButton.Click+=delegate{if(Last==null||Last.IsCompleted)Last=Run(false);};RemoveButton.Click+=delegate{if(Last==null||Last.IsCompleted){if(!test&&MessageBox.Show(this,"해시가 일치하는 앱 파일만 제거합니다. 이력·키·수정한 파일은 보존합니다. 계속할까요?","제거",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;Last=Run(true);}};
        CancelControl.Click+=delegate{if(cancellation!=null)cancellation.Cancel();};FormClosing+=delegate(object s,FormClosingEventArgs e){if(Last!=null&&!Last.IsCompleted){e.Cancel=true;CancelControl.PerformClick();}};status.Text="비어 있거나 이 설치파일이 소유하는 폴더에 설치하세요. 설치가 완료되면 앱이 자동으로 열립니다.";
    }
    async Task Run(bool remove){Error=null;Cancelled=false;cancellation=new CancellationTokenSource();var target=path.Text;InstallButton.Enabled=RemoveButton.Enabled=LaunchButton.Enabled=path.Enabled=false;CancelControl.Enabled=true;progress.Value=0;status.Text="무결성 검사 중…";
        try{Action<int,string> update=(n,file)=>{BeginInvoke((Action)(()=>{progress.Value=n;status.Text=file;if(test&&CancelAfterProgress)CancelControl.PerformClick();}));if(test&&CancelAfterProgress)Thread.Sleep(40);};
        int removedVersions=0;
        await Task.Run(()=>{if(remove){if(!test)Setup.UnregisterOfficeLoader(target);Setup.Remove(target,cancellation.Token,update);}else {Setup.Install(target,cancellation.Token,update);if(!test){removedVersions=Setup.CleanupPreviousVersions(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","HermesDevDesk"),target);Setup.UpdateShortcut(target);Setup.RegisterOfficeLoader(target);}}});status.Text=remove?"제거 완료. 사용자 데이터는 보존했습니다.":"설치 완료 · 이전 버전 "+removedVersions+"개 정리 · 이력/설정 보존: "+Path.Combine(target,"HermesDevDesk.exe");
        if(!remove&&!cancellation.IsCancellationRequested){
            try{
                if(test)AutomaticLaunches++;
                else await StartInstalledApp(target);
                status.Text="설치 완료 · Hermes DevDesk를 시작했습니다.";
            }catch(Exception e){
                status.Text="설치 완료 · 자동 실행 실패: "+e.Message;
                if(!test)MessageBox.Show(this,status.Text+"\n아래 실행 버튼으로 다시 시도할 수 있습니다.","Hermes DevDesk");
            }
        }}
        catch(OperationCanceledException){Cancelled=true;status.Text="취소됨. 기존 파일 정리 후 취소했다면 다시 설치해야 앱을 실행할 수 있습니다.";}catch(Exception e){Error=e.Message;status.Text="실패: "+e.Message;if(!test)MessageBox.Show(this,e.Message,"복구 가능한 오류");}
        finally{cancellation.Dispose();cancellation=null;InstallButton.Enabled=RemoveButton.Enabled=LaunchButton.Enabled=path.Enabled=true;CancelControl.Enabled=false;}
    }
    async Task Launch()
    {
        Error=null;InstallButton.Enabled=RemoveButton.Enabled=LaunchButton.Enabled=path.Enabled=false;
        try
        {
            var target=Path.GetFullPath(path.Text);
            await Task.Run(()=>Setup.ValidateInstalled(target));
            await StartInstalledApp(target);
            status.Text="앱 실행 요청 완료";
        }
        catch(Exception e){Error=e.Message;status.Text="실행 실패: "+e.Message;if(!test)MessageBox.Show(this,e.Message);}
        finally{InstallButton.Enabled=RemoveButton.Enabled=LaunchButton.Enabled=path.Enabled=true;}
    }
    async Task StartInstalledApp(string target)
    {
        var exe=Path.Combine(target,"HermesDevDesk.exe");
        var info=new System.Diagnostics.ProcessStartInfo(exe){WorkingDirectory=target,UseShellExecute=false};
        var data=Path.Combine(Path.GetDirectoryName(target),"launch-data-"+Guid.NewGuid().ToString("N"));
        if(test){info.Arguments="--ui-smoke --data-dir \""+data+"\"";info.WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden;}
        using(var p=System.Diagnostics.Process.Start(info))
        {
            if(p==null)throw new IOException("App process failed to start");
            if(test){await Task.Run(()=>{if(!p.WaitForExit(30000))throw new IOException("Installed app smoke timeout");if(p.ExitCode!=0||!File.Exists(Path.Combine(data,"ui-smoke.txt")))throw new IOException("Installed app smoke failed");});}
        }
    }

}

