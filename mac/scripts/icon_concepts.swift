// Renders GitVelto app-icon concepts.
//   swiftc scripts/icon_concepts.swift -o build/icon_concepts && build/icon_concepts build/icon-concepts
// Writes concept-<A..E>.png (1024px) and sheet.png (comparison on light and dark backgrounds).
import AppKit

let outDir = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "icon-concepts"
try? FileManager.default.createDirectory(atPath: outDir, withIntermediateDirectories: true)

func rgb(_ hex: UInt32, _ a: CGFloat = 1) -> NSColor {
    NSColor(srgbRed: CGFloat((hex >> 16) & 0xff) / 255, green: CGFloat((hex >> 8) & 0xff) / 255,
            blue: CGFloat(hex & 0xff) / 255, alpha: a)
}

let body = NSRect(x: 100, y: 100, width: 824, height: 824)
func squircle() -> NSBezierPath { NSBezierPath(roundedRect: body, xRadius: 186, yRadius: 186) }

func background(_ colors: [NSColor], angle: CGFloat) {
    NSGraphicsContext.saveGraphicsState()
    let sh = NSShadow()
    sh.shadowColor = NSColor.black.withAlphaComponent(0.28)
    sh.shadowBlurRadius = 22
    sh.shadowOffset = NSSize(width: 0, height: -8)
    sh.set()
    colors[0].setFill()
    squircle().fill()
    NSGraphicsContext.restoreGraphicsState()
    if colors.count > 1 { NSGradient(colors: colors)!.draw(in: squircle(), angle: angle) }
}

func stroke(_ p: NSBezierPath, _ c: NSColor, _ w: CGFloat) {
    p.lineWidth = w; p.lineCapStyle = .round; p.lineJoinStyle = .round
    c.setStroke(); p.stroke()
}

func dot(_ c: CGPoint, _ r: CGFloat, fill: NSColor, ring: NSColor? = nil, ringWidth: CGFloat = 0) {
    let p = NSBezierPath(ovalIn: NSRect(x: c.x - r, y: c.y - r, width: r * 2, height: r * 2))
    fill.setFill(); p.fill()
    if let ring { p.lineWidth = ringWidth; ring.setStroke(); p.stroke() }
}

// A: "V-Merge" — a V drawn by two branches merging into one commit.
func conceptA() {
    background([rgb(0x5B5BF7), rgb(0x3A2FC9)], angle: -90)
    let w = NSColor.white
    let left = CGPoint(x: 330, y: 700), right = CGPoint(x: 694, y: 700), bottom = CGPoint(x: 512, y: 318)
    let p = NSBezierPath()
    p.move(to: left)
    p.curve(to: bottom, controlPoint1: CGPoint(x: 330, y: 520), controlPoint2: CGPoint(x: 512, y: 470))
    p.move(to: right)
    p.curve(to: bottom, controlPoint1: CGPoint(x: 694, y: 520), controlPoint2: CGPoint(x: 512, y: 470))
    stroke(p, w, 58)
    dot(left, 58, fill: w); dot(right, 58, fill: w)
    dot(bottom, 74, fill: rgb(0x3A2FC9), ring: w, ringWidth: 40)
}

// B: "Fast-forward" — a commit line that turns into a speed chevron.
func conceptB() {
    background([rgb(0x1C1F26), rgb(0x0E1014)], angle: -90)
    let mint = rgb(0x37E2B4)
    let line = NSBezierPath()
    line.move(to: CGPoint(x: 250, y: 512)); line.line(to: CGPoint(x: 520, y: 512))
    stroke(line, rgb(0xFFFFFF, 0.9), 44)
    dot(CGPoint(x: 260, y: 512), 52, fill: .white)
    dot(CGPoint(x: 430, y: 512), 52, fill: .white)
    for (i, x) in [CGFloat(560), 680].enumerated() {
        let c = NSBezierPath()
        c.move(to: CGPoint(x: x, y: 650)); c.line(to: CGPoint(x: x + 120, y: 512)); c.line(to: CGPoint(x: x, y: 374))
        stroke(c, i == 0 ? mint.withAlphaComponent(0.55) : mint, 58)
    }
}

// C: "Swoosh" — a single fast arc through two commits, on white.
func conceptC() {
    background([rgb(0xFFFFFF), rgb(0xEEF1F6)], angle: -90)
    let p = NSBezierPath()
    p.move(to: CGPoint(x: 270, y: 330))
    p.curve(to: CGPoint(x: 740, y: 690), controlPoint1: CGPoint(x: 560, y: 330), controlPoint2: CGPoint(x: 600, y: 650))
    NSGraphicsContext.saveGraphicsState()
    let path = p.copy() as! NSBezierPath
    path.lineWidth = 76; path.lineCapStyle = .round
    let outline = NSBezierPath()
    outline.append(path)
    // gradient stroke: clip to a stroked path approximation by drawing several passes
    let steps = 28
    for i in 0..<steps {
        let t = CGFloat(i) / CGFloat(steps - 1)
        let col = NSColor(srgbRed: 0.15 + 0.0 * t, green: 0.55 - 0.25 * t, blue: 0.95 - 0.05 * t, alpha: 1)
        let seg = NSBezierPath()
        seg.move(to: point(on: p, t: max(0, t - 0.045)))
        seg.line(to: point(on: p, t: min(1, t + 0.045)))
        stroke(seg, col, 76)
    }
    NSGraphicsContext.restoreGraphicsState()
    dot(CGPoint(x: 270, y: 330), 64, fill: .white, ring: rgb(0x2690F0), ringWidth: 34)
    dot(CGPoint(x: 740, y: 690), 78, fill: rgb(0x2A4FE0))
}

func point(on p: NSBezierPath, t: CGFloat) -> CGPoint {
    // cubic from element 0 (move) and element 1 (curve)
    var pts = [NSPoint](repeating: .zero, count: 3)
    p.element(at: 0, associatedPoints: &pts)
    let p0 = pts[0]
    p.element(at: 1, associatedPoints: &pts)
    let (c1, c2, p3) = (pts[0], pts[1], pts[2])
    let u = 1 - t
    let x = u*u*u*p0.x + 3*u*u*t*c1.x + 3*u*t*t*c2.x + t*t*t*p3.x
    let y = u*u*u*p0.y + 3*u*u*t*c1.y + 3*u*t*t*c2.y + t*t*t*p3.y
    return CGPoint(x: x, y: y)
}

// D: "Folder branch" — a flat Finder-like folder with a branch glyph (shows the Finder integration).
func conceptD() {
    background([rgb(0xF5F7FA), rgb(0xE3E8EF)], angle: -90)
    let tab = NSBezierPath(roundedRect: NSRect(x: 220, y: 600, width: 250, height: 120), xRadius: 40, yRadius: 40)
    rgb(0x2F8BEA).setFill(); tab.fill()
    let folder = NSBezierPath(roundedRect: NSRect(x: 220, y: 270, width: 584, height: 400), xRadius: 56, yRadius: 56)
    NSGradient(colors: [rgb(0x5AB0FF), rgb(0x2F8BEA)])!.draw(in: folder, angle: -90)
    let w = NSColor.white
    let main = NSBezierPath(); main.move(to: CGPoint(x: 440, y: 340)); main.line(to: CGPoint(x: 440, y: 590))
    stroke(main, w, 36)
    let br = NSBezierPath(); br.move(to: CGPoint(x: 440, y: 400))
    br.curve(to: CGPoint(x: 600, y: 560), controlPoint1: CGPoint(x: 440, y: 500), controlPoint2: CGPoint(x: 600, y: 470))
    stroke(br, w, 36)
    dot(CGPoint(x: 440, y: 340), 40, fill: w); dot(CGPoint(x: 440, y: 590), 40, fill: w); dot(CGPoint(x: 600, y: 570), 40, fill: w)
}

// E: "V monogram" — two rounded bars forming a V; one bar ends in a commit.
func conceptE() {
    background([rgb(0xFF8A3D), rgb(0xF5367A)], angle: -60)
    let w = NSColor.white
    let l = NSBezierPath(); l.move(to: CGPoint(x: 320, y: 700)); l.line(to: CGPoint(x: 512, y: 320))
    stroke(l, w.withAlphaComponent(0.75), 92)
    let r = NSBezierPath(); r.move(to: CGPoint(x: 512, y: 320)); r.line(to: CGPoint(x: 676, y: 650))
    stroke(r, w, 92)
    dot(CGPoint(x: 704, y: 708), 78, fill: rgb(0xF5367A), ring: w, ringWidth: 40)
}

let concepts: [(String, String, () -> Void)] = [
    ("A", "V-Merge: 2本のブランチが合流して V", conceptA),
    ("B", "Fast-forward: コミット列 ≫ 加速", conceptB),
    ("C", "Swoosh: 一筆の弧で軽快さ", conceptC),
    ("D", "Folder: Finder 統合を表現", conceptD),
    ("E", "V モノグラム: V + コミット", conceptE),
]

func render(_ draw: () -> Void, pixels: Int) -> NSBitmapImageRep {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels, bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                               bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    NSGraphicsContext.current?.imageInterpolation = .high
    let t = NSAffineTransform(); t.scale(by: CGFloat(pixels) / 1024); t.concat()
    draw()
    NSGraphicsContext.restoreGraphicsState()
    return rep
}

var images: [NSImage] = []
for (id, _, draw) in concepts {
    let rep = render(draw, pixels: 1024)
    try rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "\(outDir)/concept-\(id).png"))
    let img = NSImage(size: NSSize(width: 1024, height: 1024)); img.addRepresentation(rep); images.append(img)
}

// Comparison sheet: light row + dark row, with small (Dock/Finder) sizes too.
let cell: CGFloat = 330, rows: CGFloat = 2, header: CGFloat = 70
let W = cell * CGFloat(concepts.count), H = header + rows * 400
let sheet = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(W), pixelsHigh: Int(H), bitsPerSample: 8, samplesPerPixel: 4,
                             hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: sheet)
NSGraphicsContext.current?.imageInterpolation = .high
rgb(0xF2F2F5).setFill(); NSRect(x: 0, y: 400, width: W, height: H - 400).fill()
rgb(0x1E1E22).setFill(); NSRect(x: 0, y: 0, width: W, height: 400).fill()
let titleAttrs: [NSAttributedString.Key: Any] = [.font: NSFont.boldSystemFont(ofSize: 26), .foregroundColor: rgb(0x222222)]
("GitVelto アイコン案（上: ライト背景 / 下: ダーク背景・小サイズ）" as NSString).draw(at: NSPoint(x: 24, y: H - 48), withAttributes: titleAttrs)
for (i, img) in images.enumerated() {
    let x = CGFloat(i) * cell
    img.draw(in: NSRect(x: x + 45, y: 400 + 80, width: 240, height: 240))
    let label = "\(concepts[i].0)  \(concepts[i].1)" as NSString
    label.draw(in: NSRect(x: x + 16, y: 400 + 20, width: cell - 24, height: 56),
               withAttributes: [.font: NSFont.systemFont(ofSize: 17), .foregroundColor: rgb(0x333333)])
    img.draw(in: NSRect(x: x + 45, y: 150, width: 160, height: 160))
    img.draw(in: NSRect(x: x + 215, y: 230, width: 64, height: 64))
    img.draw(in: NSRect(x: x + 215, y: 180, width: 32, height: 32))
    img.draw(in: NSRect(x: x + 255, y: 184, width: 16, height: 16))
    ("\(concepts[i].0)" as NSString).draw(at: NSPoint(x: x + 120, y: 60),
        withAttributes: [.font: NSFont.boldSystemFont(ofSize: 34), .foregroundColor: NSColor.white])
}
NSGraphicsContext.restoreGraphicsState()
try sheet.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "\(outDir)/sheet.png"))
print("wrote \(outDir)")
