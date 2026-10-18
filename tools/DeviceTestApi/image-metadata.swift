// DeviceTests only: what Apple's ImageIO reads from image files — size, orientation and identifying metadata.
// Used by verify-photo-metadata.cjs (compiled on demand into .device-tests/bin). Prints one JSON object per file.
import Foundation
import ImageIO
import CryptoKit
for path in CommandLine.arguments.dropFirst() {
  var out: [String: Any] = ["file": URL(fileURLWithPath: path).lastPathComponent]
  if let src = CGImageSourceCreateWithURL(URL(fileURLWithPath: path) as CFURL, nil),
     let props = CGImageSourceCopyPropertiesAtIndex(src, 0, nil) as? [String: Any] {
    let image = CGImageSourceCreateImageAtIndex(src, 0, nil)
    out["decodes"] = image != nil
    // A fingerprint of the decoded pixels: equal fingerprints mean the picture itself did not change.
    if let data = image?.dataProvider?.data as Data? { out["pixels"] = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined().prefix(16).description }
    out["width"] = props[kCGImagePropertyPixelWidth as String] ?? 0
    out["height"] = props[kCGImagePropertyPixelHeight as String] ?? 0
    out["orientation"] = props[kCGImagePropertyOrientation as String] ?? 1
    out["gps"] = props[kCGImagePropertyGPSDictionary as String] != nil
    let tiff = props[kCGImagePropertyTIFFDictionary as String] as? [String: Any] ?? [:]
    out["camera"] = tiff[kCGImagePropertyTIFFMake as String] != nil || tiff[kCGImagePropertyTIFFModel as String] != nil
    out["artist"] = tiff[kCGImagePropertyTIFFArtist as String] != nil
    out["dates"] = tiff[kCGImagePropertyTIFFDateTime as String] != nil || (props[kCGImagePropertyExifDictionary as String] as? [String: Any])?[kCGImagePropertyExifDateTimeOriginal as String] != nil
    out["iptc"] = props[kCGImagePropertyIPTCDictionary as String] != nil
    let png = props[kCGImagePropertyPNGDictionary as String] as? [String: Any] ?? [:]
    out["pngText"] = png[kCGImagePropertyPNGAuthor as String] != nil || png[kCGImagePropertyPNGComment as String] != nil
    // Every metadata tag ImageIO exposes, as «prefix:name» (EXIF/TIFF/GPS/XMP/IPTC alike).
    var tags: [String] = []
    if let meta = CGImageSourceCopyMetadataAtIndex(src, 0, nil) {
      CGImageMetadataEnumerateTagsUsingBlock(meta, nil, [kCGImageMetadataEnumerateRecursively: true] as CFDictionary) { _, tag in
        let prefix = CGImageMetadataTagCopyPrefix(tag) as String? ?? "?", name = CGImageMetadataTagCopyName(tag) as String? ?? "?"
        tags.append("\(prefix):\(name)"); return true
      }
    }
    out["tags"] = tags.sorted()
  } else { out["decodes"] = false }
  let data = try! JSONSerialization.data(withJSONObject: out, options: [.sortedKeys])
  print(String(data: data, encoding: .utf8)!)
}
