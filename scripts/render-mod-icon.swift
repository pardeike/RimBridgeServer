#!/usr/bin/env swift

import AppKit
import Foundation

struct RenderError: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

let arguments = CommandLine.arguments
guard arguments.count == 3 else {
    throw RenderError(message: "Usage: render-mod-icon.swift <preview.png> <modicon.png>")
}

let previewPath = arguments[1]
let outputPath = arguments[2]

guard let preview = NSImage(contentsOfFile: previewPath) else {
    throw RenderError(message: "Could not read preview image at \(previewPath)")
}

let previewSize = preview.size
guard previewSize.width > 0, previewSize.height > 0 else {
    throw RenderError(message: "Preview image has invalid dimensions.")
}

let orbCropRect = NSRect(x: 240, y: 190, width: 140, height: 140)

let iconSize = NSSize(width: 64, height: 64)
guard let bitmap = NSBitmapImageRep(
    bitmapDataPlanes: nil,
    pixelsWide: Int(iconSize.width),
    pixelsHigh: Int(iconSize.height),
    bitsPerSample: 8,
    samplesPerPixel: 4,
    hasAlpha: true,
    isPlanar: false,
    colorSpaceName: .deviceRGB,
    bitmapFormat: [],
    bytesPerRow: 0,
    bitsPerPixel: 0) else {
    throw RenderError(message: "Could not allocate output bitmap.")
}

NSGraphicsContext.saveGraphicsState()
guard let context = NSGraphicsContext(bitmapImageRep: bitmap) else {
    throw RenderError(message: "Could not create graphics context.")
}

NSGraphicsContext.current = context

let canvas = NSRect(origin: .zero, size: iconSize)
let background = NSGradient(colors: [
    NSColor(calibratedRed: 0.05, green: 0.10, blue: 0.16, alpha: 1.0),
    NSColor(calibratedRed: 0.02, green: 0.05, blue: 0.10, alpha: 1.0)
])!
background.draw(in: canvas, angle: -90)

let vignette = NSGradient(colors: [
    NSColor(calibratedRed: 0.24, green: 0.36, blue: 0.56, alpha: 0.16),
    NSColor(calibratedRed: 0.01, green: 0.03, blue: 0.08, alpha: 0.0)
])!
vignette.draw(in: canvas, relativeCenterPosition: NSPoint(x: -0.45, y: 0.55))

let inset = canvas.insetBy(dx: 3.5, dy: 3.5)
let frame = NSBezierPath(roundedRect: inset, xRadius: 12, yRadius: 12)
NSColor(calibratedRed: 1.0, green: 0.91, blue: 0.22, alpha: 0.28).setStroke()
frame.lineWidth = 2
frame.stroke()

let orbRect = NSRect(x: 5, y: 29, width: 28, height: 28)
let orbPath = NSBezierPath(ovalIn: orbRect)
NSGraphicsContext.current?.saveGraphicsState()
orbPath.addClip()
preview.draw(in: orbRect, from: orbCropRect, operation: .copy, fraction: 1.0)
NSGraphicsContext.current?.restoreGraphicsState()
NSColor(calibratedRed: 1.0, green: 0.91, blue: 0.22, alpha: 0.48).setStroke()
orbPath.lineWidth = 1.5
orbPath.stroke()

let shadow = NSShadow()
shadow.shadowBlurRadius = 6
shadow.shadowOffset = NSSize(width: 0, height: -1)
shadow.shadowColor = NSColor(calibratedWhite: 0.0, alpha: 0.9)

let paragraph = NSMutableParagraphStyle()
paragraph.alignment = .center

let text = NSAttributedString(
    string: "RB",
    attributes: [
        .font: NSFont.systemFont(ofSize: 31, weight: .black),
        .foregroundColor: NSColor(calibratedRed: 1.0, green: 0.93, blue: 0.20, alpha: 1.0),
        .strokeColor: NSColor(calibratedWhite: 0.02, alpha: 1.0),
        .strokeWidth: -5.0,
        .paragraphStyle: paragraph,
        .shadow: shadow
    ])

let textRect = NSRect(x: 7, y: 7, width: 52, height: 30)
text.draw(in: textRect)

NSGraphicsContext.restoreGraphicsState()

guard let pngData = bitmap.representation(using: .png, properties: [:]) else {
    throw RenderError(message: "Could not encode PNG output.")
}

try pngData.write(to: URL(fileURLWithPath: outputPath), options: .atomic)
