using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Hermes.Desktop;

internal sealed class ComputerWorkerClient : IDisposable
{
    private Process? process;
    private bool dragging;
    private readonly SemaphoreSlim serial = new(1, 1);
    internal bool Running => process != null;
    internal async Task<ComputerFrame> Execute(ComputerWorkerRequest request, CancellationToken cancellation)
    {
        if (!await serial.WaitAsync(0, cancellation)) throw new IOException("다른 컴퓨터 요청이 진행 중입니다.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(12));
        Process? child = null;
        try
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            info.ArgumentList.Add("--computer-worker");
            child = Process.Start(info) ?? throw new IOException("컴퓨터 작업 프로세스를 시작하지 못했습니다."); process = child; dragging = request.Action.Action == "drag";
            ComputerNative.AllowSetForegroundWindow((uint)child.Id);
            using var cancel = timeout.Token.Register(() => Stop());
            var output = ReadBounded(child.StandardOutput, timeout.Token); var errors = ReadBounded(child.StandardError, timeout.Token);
            await child.StandardInput.WriteAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token); child.StandardInput.Close();
            await child.WaitForExitAsync(timeout.Token);
            var reply = JsonSerializer.Deserialize<ComputerWorkerReply>(await output) ?? throw new IOException("컴퓨터 작업 결과 없음"); await errors;
            if (!reply.Success || reply.Frame == null) throw new IOException(reply.Error.Length == 0 ? "컴퓨터 요청 실패" : reply.Error);
            return reply.Frame;
        }
        catch (OperationCanceledException) { Stop(); throw new IOException(cancellation.IsCancellationRequested ? "컴퓨터 제어가 중단됐습니다." : "창 응답 시간이 초과되어 작업 프로세스를 종료했습니다."); }
        finally
        {
            if (child != null) { if (!child.HasExited) try { child.Kill(true); } catch (InvalidOperationException) { } child.Dispose(); }
            if (dragging) ComputerNative.ReleaseDrag(); dragging = false; process = null; serial.Release();
        }
    }
    private static async Task<string> ReadBounded(StreamReader reader, CancellationToken cancellation)
    {
        var text = new StringBuilder(); var buffer = new char[8192]; int count;
        while ((count = await reader.ReadAsync(buffer, cancellation)) > 0) { text.Append(buffer, 0, count); if (text.Length > 8_000_000) throw new IOException("컴퓨터 작업 결과 크기 제한"); }
        return text.ToString();
    }
    internal void Stop()
    {
        var child = process;
        if (child != null) try { if (!child.HasExited) child.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        if (dragging) ComputerNative.ReleaseDrag();
    }
    public void Dispose() => Stop();
}
