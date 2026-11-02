#!/usr/bin/env swift
// Draws the app icon: a gold bell with a lightning bolt on deep navy.
// Usage: swift scripts/generate-app-icon.swift App/Resources/Assets.xcassets/AppIcon.appiconset/AppIcon.png
import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers

let size = 1024
let space = CGColorSpace(name: CGColorSpace.sRGB)!

func color(_ hex: UInt32) -> CGColor {
    CGColor(
        colorSpace: space,
        components: [
            CGFloat((hex >> 16) & 0xFF) / 255,
            CGFloat((hex >> 8) & 0xFF) / 255,
            CGFloat(hex & 0xFF) / 255,
            1,
        ]
    )!
}

func gradient(_ top: UInt32, _ bottom: UInt32) -> CGGradient {
    CGGradient(colorsSpace: space, colors: [color(top), color(bottom)] as CFArray, locations: [0, 1])!
}

// App Store icons must be opaque, so the context has no alpha channel.
let context = CGContext(
    data: nil, width: size, height: size, bitsPerComponent: 8, bytesPerRow: 0, space: space,
    bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue
)!

// Work in y-down coordinates so the shapes read like a drawing.
context.translateBy(x: 0, y: CGFloat(size))
context.scaleBy(x: 1, y: -1)

let top = CGPoint(x: 512, y: 0)
let bottom = CGPoint(x: 512, y: 1024)

context.drawLinearGradient(gradient(0x17306B, 0x081229), start: top, end: bottom, options: [])

// A soft glow behind the bell.
let glow = CGGradient(
    colorsSpace: space,
    colors: [
        CGColor(colorSpace: space, components: [0.27, 0.46, 0.92, 0.45])!,
        CGColor(colorSpace: space, components: [0.27, 0.46, 0.92, 0.0])!,
    ] as CFArray,
    locations: [0, 1]
)!
context.drawRadialGradient(
    glow,
    startCenter: CGPoint(x: 512, y: 500), startRadius: 0,
    endCenter: CGPoint(x: 512, y: 500), endRadius: 520,
    options: []
)

let bell = CGMutablePath()
bell.move(to: CGPoint(x: 512, y: 190))
bell.addCurve(to: CGPoint(x: 302, y: 430), control1: CGPoint(x: 385, y: 190), control2: CGPoint(x: 302, y: 290))
bell.addLine(to: CGPoint(x: 302, y: 590))
bell.addCurve(to: CGPoint(x: 212, y: 715), control1: CGPoint(x: 302, y: 645), control2: CGPoint(x: 252, y: 675))
bell.addCurve(to: CGPoint(x: 245, y: 775), control1: CGPoint(x: 190, y: 740), control2: CGPoint(x: 205, y: 775))
bell.addLine(to: CGPoint(x: 779, y: 775))
bell.addCurve(to: CGPoint(x: 812, y: 715), control1: CGPoint(x: 819, y: 775), control2: CGPoint(x: 834, y: 740))
bell.addCurve(to: CGPoint(x: 722, y: 590), control1: CGPoint(x: 772, y: 675), control2: CGPoint(x: 722, y: 645))
bell.addLine(to: CGPoint(x: 722, y: 430))
bell.addCurve(to: CGPoint(x: 512, y: 190), control1: CGPoint(x: 722, y: 290), control2: CGPoint(x: 639, y: 190))
bell.closeSubpath()

// A separate path: overlapping it with the bell's outline would cut a hole where they cross.
let knob = CGPath(ellipseIn: CGRect(x: 462, y: 138, width: 100, height: 100), transform: nil)

let clapper = CGMutablePath()
clapper.move(to: CGPoint(x: 422, y: 808))
clapper.addLine(to: CGPoint(x: 602, y: 808))
clapper.addCurve(to: CGPoint(x: 422, y: 808), control1: CGPoint(x: 592, y: 912), control2: CGPoint(x: 432, y: 912))
clapper.closeSubpath()

func fill(_ path: CGPath, _ colors: CGGradient) {
    context.saveGState()
    context.addPath(path)
    context.clip()
    context.drawLinearGradient(colors, start: CGPoint(x: 512, y: 130), end: CGPoint(x: 512, y: 900), options: [])
    context.restoreGState()
}

let gold = gradient(0xFFE27A, 0xF5A623)
fill(knob, gold)
fill(bell, gold)
fill(clapper, gold)

let bolt = CGMutablePath()
bolt.addLines(between: [
    CGPoint(x: 566, y: 262),
    CGPoint(x: 398, y: 520),
    CGPoint(x: 498, y: 520),
    CGPoint(x: 452, y: 712),
    CGPoint(x: 628, y: 442),
    CGPoint(x: 526, y: 442),
])
bolt.closeSubpath()

context.addPath(bolt)
context.setFillColor(color(0x0E1C42))
context.fillPath()

guard CommandLine.arguments.count > 1, let image = context.makeImage() else {
    fatalError("Usage: generate-app-icon.swift <output.png>")
}
let url = URL(fileURLWithPath: CommandLine.arguments[1]) as CFURL
let destination = CGImageDestinationCreateWithURL(url, UTType.png.identifier as CFString, 1, nil)!
CGImageDestinationAddImage(destination, image, nil)
guard CGImageDestinationFinalize(destination) else { fatalError("Could not write the icon") }
