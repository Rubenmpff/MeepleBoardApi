// Removing identifying metadata from diary photos (PhotoMetadata): JPEG, PNG and WebP fixtures that carry GPS, camera,
// serial number, dates, author, XMP/IPTC, comments and text chunks, with orientation 6. No database, no storage.
// The same files through the real API, read back by another participant and decoded by ImageIO: verify-photo-metadata.cjs.
using System.Buffers.Binary;
using System.Text;
using MeepleBoard.Services.Calculators;

var passed = 0;
var fixtures = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tools/DeviceTestApi/fixtures"));
byte[] Read(string name) => File.ReadAllBytes(Path.Combine(fixtures, name));
void Assert(bool value, string message) { if (!value) throw new Exception(message); }
void Check(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
string Text(byte[] b) => Encoding.Latin1.GetString(b);
string[] Leaks = ["GPS", "FictionalCam", "FC-1", "SN-998877", "2026:10:17", "Ficticia", "Fictícia", "xmpmeta", "Photoshop", "Comentário", "Coment", "Author"];

// JPEG segments: (marker, payload) up to SOS; and the bytes from SOS to the end (the image itself).
(List<(byte Marker, byte[] Payload)> Segments, byte[] Image) Jpeg(byte[] b)
{
    var list = new List<(byte, byte[])>(); var i = 2;
    while (b[i + 1] != 0xDA) { var len = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(i + 2)); list.Add((b[i + 1], b.AsSpan(i + 4, len - 2).ToArray())); i += 2 + len; }
    return (list, b[i..]);
}
List<(string Type, byte[] Data)> Chunks(byte[] b, bool png)
{
    var list = new List<(string, byte[])>(); var i = png ? 8 : 12;
    while (i + 8 <= b.Length)
    {
        if (png) { var len = (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(i)); list.Add((Encoding.ASCII.GetString(b, i + 4, 4), b.AsSpan(i + 8, len).ToArray())); i += 12 + len; }
        else { var len = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i + 4)); list.Add((Encoding.ASCII.GetString(b, i, 4), b.AsSpan(i + 8, len).ToArray())); i += 8 + len + (len & 1); }
    }
    return list;
}
bool OnlyOrientation(byte[] tiff, int expected) =>
    BinaryPrimitives.ReadUInt16LittleEndian(tiff.AsSpan(8)) == 1 && BinaryPrimitives.ReadUInt16LittleEndian(tiff.AsSpan(10)) == 0x0112 && BinaryPrimitives.ReadUInt16LittleEndian(tiff.AsSpan(18)) == expected && tiff.Length == 26;

Check("JPEG: EXIF (GPS, camera, serial, dates, author), XMP, IPTC and comments go; orientation 6 stays alone; the image bytes are untouched", () => {
    var original = Read("photo-metadata.jpg");
    var r = PhotoMetadata.Strip(original);
    Assert(r.Format == "jpeg" && r.Orientation == 6, "format and orientation");
    var (segments, image) = Jpeg(r.Content);
    Assert(segments.All(s => s.Marker is not (0xED or 0xFE)), "no IPTC/Photoshop nor comment");
    var app1 = segments.Where(s => s.Marker == 0xE1).ToList();
    Assert(app1.Count == 1 && Encoding.ASCII.GetString(app1[0].Payload, 0, 6) == "Exif\0\0" && OnlyOrientation(app1[0].Payload[6..], 6), "one minimal EXIF with Orientation only");
    Assert(image.SequenceEqual(Jpeg(original).Image), "scan data copied as it was (no recompression)");
    foreach (var leak in Leaks) Assert(!Text(r.Content).Contains(leak), "leak: " + leak);
});
Check("PNG: eXIf, tEXt, iTXt and tIME go; a minimal eXIf keeps orientation 6 before the image data; CRCs and pixels intact", () => {
    var original = Read("photo-metadata.png");
    var r = PhotoMetadata.Strip(original);
    var chunks = Chunks(r.Content, png: true);
    Assert(chunks.Select(c => c.Type).SequenceEqual(new[] { "IHDR", "eXIf", "IDAT", "IEND" }), string.Join(",", chunks.Select(c => c.Type)));
    Assert(OnlyOrientation(chunks[1].Data, 6), "orientation only");
    Assert(chunks[2].Data.SequenceEqual(Chunks(original, png: true).Single(c => c.Type == "IDAT").Data), "pixels untouched");
    foreach (var leak in Leaks) Assert(!Text(r.Content).Contains(leak), "leak: " + leak);
});
Check("WebP: EXIF and XMP chunks go; a minimal EXIF keeps orientation 6; VP8X flags and RIFF size match", () => {
    var r = PhotoMetadata.Strip(Read("photo-metadata.webp"));
    var chunks = Chunks(r.Content, png: false);
    Assert(chunks.Select(c => c.Type).SequenceEqual(new[] { "VP8X", "VP8L", "EXIF" }), string.Join(",", chunks.Select(c => c.Type)));
    Assert((chunks[0].Data[0] & 0x0C) == 0x08, "EXIF flag on, XMP flag off");
    Assert(OnlyOrientation(chunks[2].Data, 6), "orientation only");
    Assert(BinaryPrimitives.ReadUInt32LittleEndian(r.Content.AsSpan(4)) == r.Content.Length - 8, "RIFF size");
    foreach (var leak in Leaks) Assert(!Text(r.Content).Contains(leak), "leak: " + leak);
});
Check("a photo already upright keeps no EXIF at all; removing twice gives the same file", () => {
    var r = PhotoMetadata.Strip(Read("photo-with-gps.jpg"));
    Assert(r.Orientation == 1 && Jpeg(r.Content).Segments.All(s => s.Marker != 0xE1), "no EXIF block");
    foreach (var name in new[] { "photo-metadata.jpg", "photo-metadata.png", "photo-metadata.webp" })
    { var once = PhotoMetadata.Strip(Read(name)).Content; Assert(PhotoMetadata.Strip(once).Content.SequenceEqual(once), "idempotent: " + name); }
});
Check("only JPEG, PNG and WebP; truncated or damaged files are refused with a clear message", () => {
    void Refused(byte[] bytes, string message) { try { PhotoMetadata.Strip(bytes); } catch (ArgumentException e) { Assert(e.Message == message, e.Message); return; } throw new Exception("accepted: " + message); }
    Refused(Encoding.ASCII.GetBytes("GIF89a....................."), PhotoMetadata.UnsupportedFormat);
    Refused([0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c', 0, 0, 0, 0], PhotoMetadata.UnsupportedFormat); // HEIC
    Refused(Encoding.ASCII.GetBytes("not an image at all"), PhotoMetadata.UnsupportedFormat);
    var jpeg = Read("photo-metadata.jpg"); Refused(jpeg[..200], PhotoMetadata.Unreadable);
    var png = Read("photo-metadata.png"); Refused(png[..60], PhotoMetadata.Unreadable);
});
Console.WriteLine($"{passed} isolated photo metadata tests passed.");
