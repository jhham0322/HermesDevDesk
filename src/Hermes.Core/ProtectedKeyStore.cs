using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Hermes.Core;
public sealed class ProtectedKeyStore
{
    private readonly string path;
    public ProtectedKeyStore(string directory) { Directory.CreateDirectory(directory); Safety.RejectReparseAncestors(directory); path = Path.Combine(directory, "provider-key.dpapi"); }
    public bool HasKey => File.Exists(path);
    public void Save(string key)
    {
        key = key.Trim(); if (key.Length is < 8 or > 4096 || key.Any(char.IsControl)) throw new IOException("키 형식/길이를 확인하세요. 키 내용은 오류에 표시하지 않습니다.");
        var bytes = Encoding.UTF8.GetBytes(key);
        try { Safety.RejectReparseAncestors(path); var protectedData = Transform(bytes, false); var temp = path + "." + Guid.NewGuid().ToString("N"); using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) output.Write(protectedData); File.Move(temp, path, true); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public string ReadOwnKey()
    {
        if (!HasKey) throw new IOException("이 앱에 저장된 회사 키가 없습니다.");
        Safety.RejectReparseAncestors(path); if (new FileInfo(path).Length > 65536) throw new IOException("키 보호 파일 크기 제한");
        var clear = Transform(File.ReadAllBytes(path), true);
        try { var key = Encoding.UTF8.GetString(clear); Safety.RegisterSecret(key); return key; }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public void DeleteOwnKey() { if (File.Exists(path)) { Safety.RejectReparseAncestors(path); File.Delete(path); } }
    private static byte[] Transform(byte[] input, bool decrypt)
    {
        if (!OperatingSystem.IsWindows()) throw new IOException("Windows 사용자 DPAPI 보호가 필요합니다.");
        var data = new Blob { Size = input.Length, Data = Marshal.AllocHGlobal(input.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(input, 0, data.Data, input.Length);
            bool ok = decrypt ? CryptUnprotectData(ref data, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) : CryptProtectData(ref data, "Hermes DevDesk company API key", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new IOException("Windows 키 보호 저장소 처리에 실패했습니다. 키 내용은 표시하지 않습니다.");
            var bytes = new byte[output.Size]; Marshal.Copy(output.Data, bytes, 0, bytes.Length); return bytes;
        }
        finally
        {
            for (int i = 0; i < input.Length; i++) Marshal.WriteByte(data.Data, i, 0);
            Marshal.FreeHGlobal(data.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
