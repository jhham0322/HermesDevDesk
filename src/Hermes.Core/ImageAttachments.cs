using System.Buffers.Binary;
using System.Security.Cryptography;
namespace Hermes.Core;
public sealed record ProjectImage(string RelativePath, string Sha256, long Bytes, int Width, int Height, string MimeType) { public override string ToString() => $"{RelativePath} · {Width}×{Height} · {Bytes / 1024.0:N1}KB"; }
public sealed record AttachmentRecord(ProjectImage Image, DateTime At);
public static class ImageAttachments
{
    public const int MaxImageBytes = 2 * 1024 * 1024;
    public const int MaxTotalBytes = 3 * 1024 * 1024;
    public static ProjectImage Read(string root, string relative)
    {
        var path = Safety.Resolve(root, relative); var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 20 or > MaxImageBytes) throw new IOException("PNG/JPEG 이미지는 파일당 2MB 이하이어야 합니다.");
        var bytes = File.ReadAllBytes(path); if (bytes.Length > MaxImageBytes) throw new IOException("이미지 크기가 변경되었습니다.");
        int width = 0, height = 0; string mime;
        if (bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        { ValidatePng(bytes); width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)); height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)); mime = "image/png"; if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)) throw new IOException("이미지 확장자/내용 불일치"); }
        else if (bytes[0] == 255 && bytes[1] == 216)
        {
            mime = "image/jpeg"; var ext = Path.GetExtension(path); if (!ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)) throw new IOException("이미지 확장자/내용 불일치");
            if (bytes[^2] != 255 || bytes[^1] != 217) throw new IOException("손상된 JPEG 종료");
            int pos = 2;
            while (pos + 4 <= bytes.Length)
            {
                if (bytes[pos++] != 255) throw new IOException("손상된 JPEG marker");
                while (pos < bytes.Length && bytes[pos] == 255) pos++;
                if (pos >= bytes.Length) break; var marker = bytes[pos++];
                if (marker is 0xD9 or 0xDA) break; if (marker is 0x01 or >= 0xD0 and <= 0xD7) continue;
                if (pos + 2 > bytes.Length) break; var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(pos, 2));
                if (length < 2 || pos + length > bytes.Length) throw new IOException("손상된 JPEG segment");
                if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
                { if (length < 7) throw new IOException("손상된 JPEG frame"); height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(pos + 3, 2)); width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(pos + 5, 2)); break; }
                pos += length;
            }
        }
        else throw new IOException("PNG/JPEG 이미지 형식이 아닙니다.");
        if (width is < 1 or > 8192 || height is < 1 or > 8192 || (long)width * height > 16_000_000) throw new IOException("이미지 해상도 제한: 최대 16MP, 한 변 8192px");
        return new(relative, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, width, height, mime);
    }
    private static void ValidatePng(byte[] bytes)
    {
        int position = 8; bool data = false, end = false;
        while (position + 12 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(position, 4));
            if (length < 0 || length > bytes.Length - position - 12) throw new IOException("손상된 PNG chunk");
            uint crc = uint.MaxValue;
            foreach (var value in bytes.AsSpan(position + 4, length + 4)) { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0); }
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position + 8 + length, 4))) throw new IOException("PNG CRC 불일치");
            var type = bytes.AsSpan(position + 4, 4); if (position == 8 && (!type.SequenceEqual("IHDR"u8) || length != 13)) throw new IOException("PNG IHDR 오류");
            if (type.SequenceEqual("IDAT"u8)) data = true;
            position += length + 12;
            if (type.SequenceEqual("IEND"u8)) { end = length == 0; break; }
        }
        if (!data || !end || position != bytes.Length) throw new IOException("완결된 PNG 데이터 필요");
    }
    public static List<ProjectImage> Validate(string root, IEnumerable<ProjectImage> selected)
    {
        var images = selected.ToList();
        if (images.Count > 4 || images.Sum(i => i.Bytes) > MaxTotalBytes || images.Select(i => i.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != images.Count) throw new IOException("이미지 최대 4개 / 합계 3MB / 중복 없음");
        foreach (var image in images) if (Read(root, image.RelativePath) != image) throw new IOException("선택 이후 이미지가 변경되었습니다. 다시 첨부하세요.");
        return images;
    }
}
