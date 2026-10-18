using System.Buffers.Binary;
using System.Text;

namespace MeepleBoard.Services.Calculators;

/// <summary>
/// Removes identifying metadata from a photo before it is stored or served (approved 18/10/2026): EXIF (GPS, camera,
/// serial numbers, dates, owner, embedded thumbnails), XMP, IPTC/Photoshop blocks, comments and text chunks. The pixels
/// are copied as they are — no recompression — and the orientation is kept in a minimal EXIF block holding only that tag,
/// so the photo is shown the right way up. Colour data needed to display it (JPEG ICC profile and Adobe block, PNG colour
/// chunks) stays. Only the formats the app sends are accepted: JPEG, PNG and WebP.
/// </summary>
public static class PhotoMetadata
{
    public const string UnsupportedFormat = "Formato de imagem não suportado. Usa JPEG, PNG ou WebP.";
    public const string Unreadable = "Não foi possível ler esta imagem.";

    public sealed record Result(byte[] Content, string Format, int Orientation);

    public static Result Strip(byte[] input)
    {
        if (input.Length > 3 && input[0] == 0xFF && input[1] == 0xD8 && input[2] == 0xFF) return Jpeg(input);
        if (input.Length > 8 && input.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return Png(input);
        if (input.Length > 12 && Ascii(input, 0, 4) == "RIFF" && Ascii(input, 8, 4) == "WEBP") return WebP(input);
        throw new ArgumentException(UnsupportedFormat);
    }

    // ── JPEG ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    static Result Jpeg(byte[] input)
    {
        var output = new MemoryStream(input.Length);
        output.Write(input, 0, 2); // SOI
        var orientation = 1;
        var kept = new List<(int Start, int Length)>();
        var i = 2;
        try
        {
            while (true)
            {
                if (input[i] != 0xFF) throw new ArgumentException(Unreadable);
                var marker = input[i + 1];
                if (marker == 0xFF) { i++; continue; } // fill byte
                if (marker == 0xD9) { kept.Add((i, 2)); break; } // EOI: anything after it (appended images, data) is dropped
                var length = BinaryPrimitives.ReadUInt16BigEndian(input.AsSpan(i + 2, 2));
                var segmentEnd = i + 2 + length;
                if (length < 2 || segmentEnd > input.Length) throw new ArgumentException(Unreadable);
                var payload = input.AsSpan(i + 4, length - 2);
                var keep = marker switch
                {
                    0xE0 => true,                                           // JFIF
                    0xE2 => Ascii(payload, 0, 12) == "ICC_PROFILE\0",       // colour profile only (not MPF/FlashPix)
                    0xEE => true,                                           // Adobe colour transform
                    0xFE => false,                                          // comment
                    >= 0xE1 and <= 0xEF => false,                           // EXIF, XMP, IPTC, Photoshop, other APPn
                    _ => true,
                };
                if (marker == 0xE1 && Ascii(payload, 0, 6) == "Exif\0\0") orientation = Orientation(payload[6..]) ?? orientation;
                if (keep) kept.Add((i, segmentEnd - i));
                i = segmentEnd;
                if (marker == 0xDA)
                {
                    // Entropy-coded data runs until the next marker that is not a stuffed byte nor a restart marker.
                    var start = i;
                    while (i + 1 < input.Length && !(input[i] == 0xFF && input[i + 1] != 0x00 && (input[i + 1] < 0xD0 || input[i + 1] > 0xD7))) i++;
                    if (i + 1 >= input.Length) throw new ArgumentException(Unreadable);
                    kept.Add((start, i - start));
                }
            }
        }
        catch (IndexOutOfRangeException) { throw new ArgumentException(Unreadable); }
        catch (ArgumentOutOfRangeException) { throw new ArgumentException(Unreadable); }

        // Orientation first, right after SOI (and after JFIF when present), as readers expect.
        var minimal = orientation != 1 ? MinimalExif(orientation) : null;
        var wroteExif = minimal == null;
        foreach (var (start, length) in kept)
        {
            if (!wroteExif && input[start + 1] != 0xE0) { WriteJpegApp1(output, minimal!); wroteExif = true; }
            output.Write(input, start, length);
        }
        return new Result(output.ToArray(), "jpeg", orientation);
    }

    static void WriteJpegApp1(Stream output, byte[] tiff)
    {
        var payload = Encoding.ASCII.GetBytes("Exif\0\0").Concat(tiff).ToArray();
        output.WriteByte(0xFF); output.WriteByte(0xE1);
        var length = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)(payload.Length + 2));
        output.Write(length); output.Write(payload);
    }

    // ── PNG ──────────────────────────────────────────────────────────────────────────────────────────────────────────
    static readonly HashSet<string> PngKept = ["IHDR", "PLTE", "IDAT", "IEND", "tRNS", "cHRM", "gAMA", "iCCP", "sBIT", "sRGB", "bKGD", "pHYs", "acTL", "fcTL", "fdAT"];

    static Result Png(byte[] input)
    {
        var output = new MemoryStream(input.Length);
        output.Write(input, 0, 8);
        var orientation = 1;
        var chunks = new List<(string Type, int Start, int Length)>();
        var i = 8;
        try
        {
            while (i + 12 <= input.Length)
            {
                var length = (int)BinaryPrimitives.ReadUInt32BigEndian(input.AsSpan(i, 4));
                var type = Ascii(input, i + 4, 4);
                if (length < 0 || i + 12 + length > input.Length) throw new ArgumentException(Unreadable);
                if (type == "eXIf") orientation = Orientation(input.AsSpan(i + 8, length)) ?? orientation;
                if (PngKept.Contains(type)) chunks.Add((type, i, 12 + length)); // text, time, eXIf and private chunks go
                i += 12 + length;
                if (type == "IEND") break;
            }
        }
        catch (ArgumentOutOfRangeException) { throw new ArgumentException(Unreadable); }
        if (chunks.Count == 0 || chunks[0].Type != "IHDR" || chunks[^1].Type != "IEND") throw new ArgumentException(Unreadable);
        var wroteExif = orientation == 1;
        foreach (var (type, start, length) in chunks)
        {
            if (!wroteExif && type is "IDAT" or "fdAT" or "acTL") { WritePngChunk(output, "eXIf", MinimalExif(orientation)); wroteExif = true; }
            output.Write(input, start, length);
        }
        return new Result(output.ToArray(), "png", orientation);
    }

    static void WritePngChunk(Stream output, string type, byte[] data)
    {
        var header = new byte[8]; BinaryPrimitives.WriteUInt32BigEndian(header, (uint)data.Length); Encoding.ASCII.GetBytes(type).CopyTo(header, 4);
        output.Write(header); output.Write(data);
        var crc = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(header.AsSpan(4).ToArray().Concat(data).ToArray()));
        output.Write(crc);
    }

    // ── WebP ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    static Result WebP(byte[] input)
    {
        var riffSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(4, 4));
        if (riffSize + 8 > input.Length || riffSize < 4) throw new ArgumentException(Unreadable);
        var orientation = 1;
        var chunks = new List<(string Type, int Start, int Length)>();
        var i = 12;
        while (i + 8 <= riffSize + 8)
        {
            var type = Ascii(input, i, 4);
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(i + 4, 4));
            var padded = size + (size & 1);
            if (size < 0 || i + 8 + size > input.Length) throw new ArgumentException(Unreadable);
            if (type == "EXIF") orientation = Orientation(StripExifHeader(input.AsSpan(i + 8, size))) ?? orientation;
            if (type is not ("EXIF" or "XMP ")) chunks.Add((type, i, Math.Min(8 + padded, input.Length - i)));
            i += 8 + padded;
        }
        if (chunks.Count == 0) throw new ArgumentException(Unreadable);
        var body = new MemoryStream();
        var extended = chunks[0].Type == "VP8X";
        foreach (var (type, start, length) in chunks)
        {
            if (type == "VP8X")
            {
                var vp8x = input.AsSpan(start, length).ToArray();
                vp8x[8] = (byte)(vp8x[8] & ~0x0C | (orientation != 1 ? 0x08 : 0)); // XMP bit off; EXIF bit only for the minimal block
                body.Write(vp8x);
            }
            else body.Write(input, start, length);
        }
        if (orientation != 1 && extended)
        {
            var exif = MinimalExif(orientation);
            var header = new byte[8]; Encoding.ASCII.GetBytes("EXIF").CopyTo(header, 0); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)exif.Length);
            body.Write(header); body.Write(exif); if ((exif.Length & 1) == 1) body.WriteByte(0);
        }
        var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes("RIFF"));
        var size2 = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(size2, (uint)(body.Length + 4)); output.Write(size2);
        output.Write(Encoding.ASCII.GetBytes("WEBP")); body.Position = 0; body.CopyTo(output);
        return new Result(output.ToArray(), "webp", extended ? orientation : 1);
    }

    static ReadOnlySpan<byte> StripExifHeader(ReadOnlySpan<byte> data) => data.Length > 6 && Ascii(data, 0, 6) == "Exif\0\0" ? data[6..] : data;

    // ── EXIF orientation ─────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>The Orientation tag (0x0112) of a TIFF block, 1–8, or null when absent or unreadable.</summary>
    static int? Orientation(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8) return null;
        var little = tiff[0] == (byte)'I' && tiff[1] == (byte)'I';
        if (!little && !(tiff[0] == (byte)'M' && tiff[1] == (byte)'M')) return null;
        ushort U16(ReadOnlySpan<byte> s, int o) => little ? BinaryPrimitives.ReadUInt16LittleEndian(s[o..]) : BinaryPrimitives.ReadUInt16BigEndian(s[o..]);
        uint U32(ReadOnlySpan<byte> s, int o) => little ? BinaryPrimitives.ReadUInt32LittleEndian(s[o..]) : BinaryPrimitives.ReadUInt32BigEndian(s[o..]);
        var ifd = (int)U32(tiff, 4);
        if (ifd < 8 || ifd + 2 > tiff.Length) return null;
        int count = U16(tiff, ifd);
        for (var e = 0; e < count && ifd + 2 + e * 12 + 12 <= tiff.Length; e++)
        {
            var entry = ifd + 2 + e * 12;
            if (U16(tiff, entry) == 0x0112) { int value = U16(tiff, entry + 8); return value is >= 1 and <= 8 ? value : null; }
        }
        return null;
    }

    /// <summary>A TIFF block with a single tag: Orientation.</summary>
    static byte[] MinimalExif(int orientation)
    {
        var tiff = new byte[26];
        tiff[0] = (byte)'I'; tiff[1] = (byte)'I'; tiff[2] = 42; tiff[4] = 8;
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(10), 0x0112);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(12), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(14), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(18), (ushort)orientation);
        return tiff; // next IFD offset (bytes 22–25) stays 0
    }

    static string Ascii(ReadOnlySpan<byte> data, int start, int length) =>
        data.Length >= start + length ? Encoding.Latin1.GetString(data.Slice(start, length)) : "";

    static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
