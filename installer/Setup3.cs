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
[assembly: AssemblyVersion("0.7.1.0")]
[assembly: AssemblyFileVersion("0.7.1.0")]
internal static class Setup
{
    internal const string Version="0.7.1";
    internal static string Target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","HermesDevDesk",Version);
    [STAThread] static int Main(string[] args)
    {
        try {
            if(args.Length==2&&args[0]=="/verify") { using(var z=Payload()){ var m=Manifest(z); Verify(z,m); File.WriteAllText(args[1],"PASS: "+m.Count+" embedded files SHA256 verified. No install.\n");} return 0;}
            if(args.Length==2&&args[0]=="/extract"){ Install(args[1],CancellationToken.None,null);return 0;}
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length==3&&args[0]=="/test-suite"){Tests(args[1],args[2]);return 0;}
            if(args.Length>0)throw new IOException("Use /verify REPORT, /extract ABSOLUTE_PATH or /test-suite ISOLATED_PATH REPORT");
            Application.Run(new SetupForm(Target,false));return 0;
        }catch(Exception e){if(args.Length>1&&(args[0]=="/verify"||args[0]=="/test-suite"))File.WriteAllText(args[args.Length-1],"FAIL "+e);else if(args.Length==0)MessageBox.Show(e.Message);return 1;}
    }
    static ZipArchive Payload(){var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("HermesPayload.zip");if(s==null)throw new IOException("Payload missing");return new ZipArchive(s,ZipArchiveMode.Read);}
    static string Hash(Stream s){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
    static string Identity(ZipArchive z){using(var s=z.GetEntry(".hermes-manifest.tsv").Open())return "HermesDevDesk-"+Version+"-"+Hash(s);}
    static void Root(string r){if(!Path.IsPathRooted(r)||r.StartsWith(@"\\")||Path.GetFullPath(r).TrimEnd('\\')==Path.GetPathRoot(r).TrimEnd('\\'))throw new IOException("Local non-root absolute destination required");for(var p=Path.GetFullPath(r);!String.IsNullOrEmpty(p);p=Path.GetDirectoryName(p))if((Directory.Exists(p)||File.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Reparse path blocked");}
    static void Name(string n){if(String.IsNullOrEmpty(n)||Path.IsPathRooted(n)||n.Contains(":")||n.Contains("\\"))throw new IOException("Unsafe archive path");foreach(var s in n.TrimEnd('/').Split('/')){var stem=s.Split('.')[0].ToUpperInvariant();if(s.Length==0||s=="."||s==".."||s.EndsWith(".")||s.Trim()!=s||stem=="CON"||stem=="AUX"||stem=="NUL"||stem=="PRN"||System.Text.RegularExpressions.Regex.IsMatch(stem,"^(COM|LPT)[1-9]$"))throw new IOException("Unsafe archive segment");}}
    static string FilePath(string r,string n){Name(n);var p=Path.GetFullPath(Path.Combine(r,n));if(!p.StartsWith(r.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Escaped destination");Root(p);return p;}
    static Dictionary<string,string> Manifest(ZipArchive z){var entry=z.GetEntry(".hermes-manifest.tsv");if(entry==null)throw new IOException("Manifest missing");var m=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);using(var r=new StreamReader(entry.Open()))while(!r.EndOfStream){var l=r.ReadLine();if(String.IsNullOrEmpty(l))continue;var f=l.Split('\t');if(f.Length!=2||f[0].Length!=64)throw new IOException("Invalid manifest");Name(f[1]);m.Add(f[1],f[0]);}if(!m.ContainsKey("HermesDevDesk.exe")||m.Count<10)throw new IOException("Incomplete payload");return m;}
    static void Verify(ZipArchive z,Dictionary<string,string> m){var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long bytes=0;foreach(var e in z.Entries){Name(e.FullName);if(!seen.Add(e.FullName))throw new IOException("Duplicate archive entry");bytes+=e.Length;if(bytes>1024L*1024*1024)throw new IOException("Payload too large");if(e.Name.Length==0||e.FullName==".hermes-manifest.tsv")continue;string hash;if(!m.TryGetValue(e.FullName,out hash))throw new IOException("Unlisted file");using(var s=e.Open())if(Hash(s)!=hash)throw new IOException("Payload hash mismatch");}foreach(var n in m.Keys)if(!seen.Contains(n))throw new IOException("Missing file");}
    internal static void Install(string r,CancellationToken ct,Action<int,string> progress){
        Root(r);r=Path.GetFullPath(r);using(var z=Payload()){var m=Manifest(z);Verify(z,m);ct.ThrowIfCancellationRequested();var marker=Path.Combine(r,".hermes-owned");var id=Identity(z);
        Root(marker);if(Directory.Exists(r)&&Directory.GetFileSystemEntries(r).Length>0&&(!File.Exists(marker)||File.ReadAllText(marker).Trim()!=id))throw new IOException("Nonempty unowned directory or another version; choose another path");
        foreach(var pair in m){var p=FilePath(r,pair.Key);if(Directory.Exists(p))throw new IOException("Directory conflicts with file");if(File.Exists(p))using(var s=File.OpenRead(p))if(Hash(s)!=pair.Value)throw new IOException("Modified file preserved; choose another destination: "+pair.Key);}
        var manifest=Path.Combine(r,".hermes-manifest.tsv");Root(manifest);if(File.Exists(manifest)){using(var existing=File.OpenRead(manifest))using(var expected=z.GetEntry(".hermes-manifest.tsv").Open())if(Hash(existing)!=Hash(expected))throw new IOException("Ownership manifest changed");}
        Directory.CreateDirectory(r);if(!File.Exists(marker))File.WriteAllText(marker,id,new UTF8Encoding(false));if(!File.Exists(manifest))using(var s=z.GetEntry(".hermes-manifest.tsv").Open())using(var f=new FileStream(manifest,FileMode.CreateNew))s.CopyTo(f);
        int count=0;foreach(var pair in m){ct.ThrowIfCancellationRequested();var p=FilePath(r,pair.Key);if(!File.Exists(p)){Directory.CreateDirectory(Path.GetDirectoryName(p));var tmp=Path.Combine(Path.GetDirectoryName(p),".hermes-"+Guid.NewGuid().ToString("N")+".partial");try{using(var s=z.GetEntry(pair.Key).Open())using(var f=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var buffer=new byte[1024*1024];int n;while((n=s.Read(buffer,0,buffer.Length))!=0){ct.ThrowIfCancellationRequested();f.Write(buffer,0,n);}}using(var f=File.OpenRead(tmp))if(Hash(f)!=pair.Value)throw new IOException("Written hash mismatch");ct.ThrowIfCancellationRequested();File.Move(tmp,p);}finally{if(File.Exists(tmp))File.Delete(tmp);}}count++;if(progress!=null)progress(count*100/m.Count,pair.Key);}}
    }
    internal static void Remove(string r,CancellationToken ct,Action<int,string> progress){Root(r);r=Path.GetFullPath(r);using(var z=Payload()){var m=Manifest(z);Verify(z,m);var marker=FilePath(r,".hermes-owned");if(!File.Exists(marker)||File.ReadAllText(marker).Trim()!=Identity(z))throw new IOException("This installer does not own that destination");int count=0;foreach(var pair in m){ct.ThrowIfCancellationRequested();var p=FilePath(r,pair.Key);if(File.Exists(p)){bool same;using(var f=File.OpenRead(p))same=Hash(f)==pair.Value;if(same)File.Delete(p);}count++;if(progress!=null)progress(count*100/m.Count,pair.Key);}}}
    internal static void ValidateInstalled(string r){Root(r);r=Path.GetFullPath(r);using(var z=Payload()){var m=Manifest(z);var marker=FilePath(r,".hermes-owned");if(!File.Exists(marker)||File.ReadAllText(marker).Trim()!=Identity(z))throw new IOException("Install this exact version first");foreach(var pair in m){var p=FilePath(r,pair.Key);if(!File.Exists(p))throw new IOException("Incomplete installation: "+pair.Key);using(var s=File.OpenRead(p))if(Hash(s)!=pair.Value)throw new IOException("Modified app file: "+pair.Key);}}}
    static void Wait(SetupForm f){while(f.Last!=null&&!f.Last.IsCompleted){Application.DoEvents();Thread.Sleep(10);}Application.DoEvents();}
    static void Tests(string root,string report){
        Root(root);if(Directory.Exists(root)&&Directory.GetFileSystemEntries(root).Length!=0)throw new IOException("Test root must be empty");Directory.CreateDirectory(root);var lines=new List<string>();var path=Path.Combine(root,"install");
        using(var f=new SetupForm(path,true)){f.Show();f.InstallButton.PerformClick();var first=f.Last;f.InstallButton.PerformClick();if(first!=f.Last)throw new IOException("Duplicate click");Wait(f);if(f.Error!=null||!File.Exists(Path.Combine(path,"HermesDevDesk.exe")))throw new IOException("Install failed "+f.Error);lines.Add("PASS programmatic install/repeated-click guard");
        f.LaunchButton.PerformClick();Wait(f);if(f.Error!=null)throw new IOException("Installed app launch failed: "+f.Error);lines.Add("PASS launch button runs installed WPF app with isolated smoke data");
        f.InstallButton.PerformClick();Wait(f);if(f.Error!=null)throw new IOException("Repeat install failed");lines.Add("PASS repeat same-version installation");
        File.WriteAllText(Path.Combine(path,"README.md"),"MODIFIED");File.WriteAllText(Path.Combine(path,"user.txt"),"KEEP");f.InstallButton.PerformClick();Wait(f);if(f.Error==null)throw new IOException("Modified file overwritten");lines.Add("PASS modified conflict error, no overwrite");
        f.RemoveButton.PerformClick();Wait(f);if(f.Error!=null||File.Exists(Path.Combine(path,"HermesDevDesk.exe"))||File.ReadAllText(Path.Combine(path,"README.md"))!="MODIFIED"||File.ReadAllText(Path.Combine(path,"user.txt"))!="KEEP")throw new IOException("Removal preservation failed");lines.Add("PASS remove handler preserves modified/user files");f.Close();}
        path=Path.Combine(root,"cancel");using(var f=new SetupForm(path,true)){f.Show();f.CancelAfterProgress=true;f.InstallButton.PerformClick();Wait(f);if(!f.Cancelled||Directory.GetFiles(path,"*.partial",SearchOption.AllDirectories).Length>0)throw new IOException("Cancel failed");lines.Add("PASS cancel handler and private partial cleanup");
        f.CancelAfterProgress=false;f.InstallButton.PerformClick();Wait(f);if(f.Error!=null)throw new IOException("Cancel resume failed");lines.Add("PASS recover cancelled installation");
        f.RemoveButton.PerformClick();Wait(f);f.InstallButton.PerformClick();Wait(f);if(f.Error!=null)throw new IOException("Reinstall failed");lines.Add("PASS reinstall after removal");f.Close();}
        path=Path.Combine(root,"unowned");Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"keep.txt"),"KEEP");try{Install(path,CancellationToken.None,null);throw new Exception("Unowned accepted");}catch(IOException){if(File.ReadAllText(Path.Combine(path,"keep.txt"))!="KEEP")throw;lines.Add("PASS unowned destination preserved");}
        lines.Add("Programmatic WinForms PerformClick tests only. Native mouse click verification blocked externally. Explicit isolated paths; no default installation, shortcut, registry, firewall, credential or global setting changes.");File.WriteAllLines(report,lines,new UTF8Encoding(false));
    }
}
internal sealed class SetupForm:Form{
    internal Button InstallButton=new Button{Text="설치 / 재개"},RemoveButton=new Button{Text="이 버전 제거"},CancelControl=new Button{Text="작업 취소",Enabled=false};
    internal Button LaunchButton=new Button{Text="설치된 앱 실행"};
    internal Task Last;internal string Error;internal bool Cancelled,CancelAfterProgress;bool test;CancellationTokenSource cancellation;TextBox path=new TextBox();Label status=new Label();ProgressBar progress=new ProgressBar();
    internal SetupForm(string target,bool testing){test=testing;Text="Hermes DevDesk 0.7.0 설치";Width=700;Height=440;StartPosition=FormStartPosition.CenterScreen;MaximizeBox=false;
        Controls.Add(new Label{Left=24,Top=20,Width=640,Height=100,Text="독립 Windows 개발 에이전트 앱 · 회사 Azure API 기본\n.NET 런타임과 공식 CLI 포함. 기존 버전과 이력은 보존합니다.\n관리자 권한·방화벽·전역 설정 변경 없이 선택한 폴더에 설치합니다.\n회사 주소/배포명/키는 설치 후 앱에 직접 입력하세요."});
        path.SetBounds(24,130,530,28);path.Text=target;var browse=new Button{Text="폴더 선택"};browse.SetBounds(565,128,100,30);browse.Click+=delegate{using(var d=new FolderBrowserDialog())if(d.ShowDialog(this)==DialogResult.OK)path.Text=d.SelectedPath;};
        progress.SetBounds(24,177,640,22);status.SetBounds(24,210,640,65);InstallButton.SetBounds(24,290,190,38);RemoveButton.SetBounds(237,290,190,38);CancelControl.SetBounds(450,290,215,38);var close=new Button{Text="닫기"};close.SetBounds(550,340,115,32);close.Click+=delegate{Close();};LaunchButton.SetBounds(24,340,190,32);
        Controls.AddRange(new Control[]{path,browse,progress,status,InstallButton,RemoveButton,CancelControl,LaunchButton,close});
        LaunchButton.Click+=delegate{if(Last==null||Last.IsCompleted)Last=Launch();};
        InstallButton.Click+=delegate{if(Last==null||Last.IsCompleted)Last=Run(false);};RemoveButton.Click+=delegate{if(Last==null||Last.IsCompleted){if(!test&&MessageBox.Show(this,"해시가 일치하는 앱 파일만 제거합니다. 이력·키·수정한 파일은 보존합니다. 계속할까요?","제거",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;Last=Run(true);}};
        CancelControl.Click+=delegate{if(cancellation!=null)cancellation.Cancel();};FormClosing+=delegate(object s,FormClosingEventArgs e){if(Last!=null&&!Last.IsCompleted){e.Cancel=true;CancelControl.PerformClick();}};status.Text="비어 있거나 이 설치파일이 소유하는 폴더에 설치하세요. 앱은 자동 실행하지 않습니다.";
    }
    async Task Run(bool remove){Error=null;Cancelled=false;cancellation=new CancellationTokenSource();var target=path.Text;InstallButton.Enabled=RemoveButton.Enabled=path.Enabled=false;CancelControl.Enabled=true;progress.Value=0;status.Text="무결성 검사 중…";
        try{Action<int,string> update=(n,file)=>{BeginInvoke((Action)(()=>{progress.Value=n;status.Text=file;if(test&&CancelAfterProgress)CancelControl.PerformClick();}));if(test&&CancelAfterProgress)Thread.Sleep(40);};
        await Task.Run(()=>{if(remove)Setup.Remove(target,cancellation.Token,update);else Setup.Install(target,cancellation.Token,update);});status.Text=remove?"제거 완료. 사용자 데이터는 보존했습니다.":"설치 완료: "+Path.Combine(target,"HermesDevDesk.exe");}
        catch(OperationCanceledException){Cancelled=true;status.Text="취소됨. 같은 경로에 설치 / 재개로 복구하세요.";}catch(Exception e){Error=e.Message;status.Text="실패: "+e.Message;if(!test)MessageBox.Show(this,e.Message,"복구 가능한 오류");}
        finally{cancellation.Dispose();cancellation=null;InstallButton.Enabled=RemoveButton.Enabled=path.Enabled=true;CancelControl.Enabled=false;}
    }
    async Task Launch()
    {
        Error=null;InstallButton.Enabled=RemoveButton.Enabled=LaunchButton.Enabled=path.Enabled=false;
        try
        {
            var target=Path.GetFullPath(path.Text);
            await Task.Run(()=>Setup.ValidateInstalled(target));
            var exe=Path.Combine(target,"HermesDevDesk.exe");
            var info=new System.Diagnostics.ProcessStartInfo(exe){WorkingDirectory=target,UseShellExecute=false};
            var data=Path.Combine(Path.GetDirectoryName(target),"launch-data-"+Guid.NewGuid().ToString("N"));
            if(test){info.Arguments="--ui-smoke --data-dir \""+data+"\"";info.WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden;}
            using(var p=System.Diagnostics.Process.Start(info))
            {
                if(p==null)throw new IOException("App process failed to start");
                if(test){await Task.Run(()=>{if(!p.WaitForExit(30000))throw new IOException("Installed app smoke timeout");if(p.ExitCode!=0||!File.Exists(Path.Combine(data,"ui-smoke.txt")))throw new IOException("Installed app smoke failed");});}
            }
            status.Text="앱 실행 요청 완료";
        }
        catch(Exception e){Error=e.Message;status.Text="실행 실패: "+e.Message;if(!test)MessageBox.Show(this,e.Message);}
        finally{InstallButton.Enabled=RemoveButton.Enabled=LaunchButton.Enabled=path.Enabled=true;}
    }
}

