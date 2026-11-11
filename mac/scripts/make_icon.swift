// Renders the GitVelto app icon: a git branch that glides up-right on a blue gradient.
// Usage: swiftc scripts/make_icon.swift -o build/make_icon && build/make_icon Resources/AppIcon.icns
// Writes a PNG-based .icns directly (no sips/iconutil needed).
import AppKit

let outPath = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "AppIcon.icns"
/// Optional source artwork (PNG). When given, it is scaled into every icon size instead of drawing.
let sourceImage: NSImage? = CommandLine.arguments.count > 2 ? NSImage(contentsOfFile: CommandLine.arguments[2]) : nil
if CommandLine.arguments.count > 2 && sourceImage == nil { print("cannot read \(CommandLine.arguments[2])"); exit(1) }

func render(pixels: Int) throws -> Data {
guard let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels,
                                 bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                                 colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) else { exit(1) }
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
NSGraphicsContext.current?.imageInterpolation = .high
if let src = sourceImage {
    src.draw(in: NSRect(x: 0, y: 0, width: pixels, height: pixels), from: .zero, operation: .copy, fraction: 1)
    NSGraphicsContext.restoreGraphicsState()
    guard let png = rep.representation(using: .png, properties: [:]) else { exit(1) }
    return png
}
let scale = NSAffineTransform()
scale.scale(by: CGFloat(pixels) / 1024)
scale.concat()

func rgb(_ r: Int, _ g: Int, _ b: Int, _ a: CGFloat = 1) -> NSColor {
    NSColor(srgbRed: CGFloat(r) / 255, green: CGFloat(g) / 255, blue: CGFloat(b) / 255, alpha: a)
}

// Background squircle (macOS icon grid: 824pt body centered)
let body = NSRect(x: 100, y: 100, width: 824, height: 824)
let bg = NSBezierPath(roundedRect: body, xRadius: 185, yRadius: 185)
NSGraphicsContext.saveGraphicsState()
let shadow = NSShadow()
shadow.shadowColor = NSColor.black.withAlphaComponent(0.3)
shadow.shadowBlurRadius = 24
shadow.shadowOffset = NSSize(width: 0, height: -10)
shadow.set()
rgb(40, 70, 200).setFill()
bg.fill()
NSGraphicsContext.restoreGraphicsState()
NSGradient(colors: [rgb(56, 189, 248), rgb(59, 130, 246), rgb(79, 70, 229)])!.draw(in: bg, angle: -60)

// Soft highlight
NSGraphicsContext.saveGraphicsState()
bg.addClip()
let glow = NSBezierPath(ovalIn: NSRect(x: 40, y: 560, width: 700, height: 500))
NSColor.white.withAlphaComponent(0.10).setFill()
glow.fill()

// Speed lines ("glide")
for (i, y) in [CGFloat(330), 400, 470].enumerated() {
    let l = NSBezierPath()
    l.move(to: CGPoint(x: 150 + CGFloat(i) * 30, y: y))
    l.line(to: CGPoint(x: 300 + CGFloat(i) * 10, y: y))
    l.lineWidth = 22
    l.lineCapStyle = .round
    NSColor.white.withAlphaComponent(0.28 - CGFloat(i) * 0.06).setStroke()
    l.stroke()
}
NSGraphicsContext.restoreGraphicsState()

// Git graph: main line + a branch that glides up and to the right
let white = NSColor.white
let main = NSBezierPath()
main.move(to: CGPoint(x: 400, y: 250))
main.line(to: CGPoint(x: 400, y: 560))
main.lineWidth = 46
main.lineCapStyle = .round
white.setStroke()
main.stroke()

let glide = NSBezierPath()
glide.move(to: CGPoint(x: 400, y: 330))
glide.curve(to: CGPoint(x: 700, y: 700), controlPoint1: CGPoint(x: 400, y: 560), controlPoint2: CGPoint(x: 560, y: 690))
glide.lineWidth = 46
glide.lineCapStyle = .round
white.setStroke()
glide.stroke()

let nodeColor = rgb(59, 110, 230)
for (pt, r) in [(CGPoint(x: 400, y: 250), CGFloat(62)), (CGPoint(x: 400, y: 600), 62), (CGPoint(x: 712, y: 712), 72)] {
    let dot = NSBezierPath(ovalIn: NSRect(x: pt.x - r, y: pt.y - r, width: r * 2, height: r * 2))
    white.setFill()
    dot.fill()
    let inner = NSBezierPath(ovalIn: NSRect(x: pt.x - r * 0.48, y: pt.y - r * 0.48, width: r * 0.96, height: r * 0.96))
    nodeColor.setFill()
    inner.fill()
}

NSGraphicsContext.restoreGraphicsState()
guard let png = rep.representation(using: .png, properties: [:]) else { exit(1) }
return png
}

// ICNS container: 'icns' + length, then (type, length, png) entries.
let entries: [(String, Int)] = [
    ("icp4", 16), ("icp5", 32), ("ic07", 128), ("ic08", 256), ("ic09", 512),
    ("ic11", 32), ("ic12", 64), ("ic13", 256), ("ic14", 512), ("ic10", 1024),
]
func be32(_ v: Int) -> Data { withUnsafeBytes(of: UInt32(v).bigEndian) { Data($0) } }
var body = Data()
for (type, px) in entries {
    let png = try render(pixels: px)
    body.append(Data(type.utf8)); body.append(be32(png.count + 8)); body.append(png)
}
var icns = Data("icns".utf8)
icns.append(be32(body.count + 8))
icns.append(body)
try icns.write(to: URL(fileURLWithPath: outPath))
print("wrote \(outPath)")
