import CoreGraphics
import CoreText
import Foundation

guard CommandLine.arguments.count == 6 else {
    fputs("usage: apply_starcross_frame.swift input.pdf opcode-tables.pdf output.pdf replace-pages title\n", stderr)
    exit(2)
}

let inputURL = URL(fileURLWithPath: CommandLine.arguments[1])
let tablesURL = URL(fileURLWithPath: CommandLine.arguments[2])
let outputURL = URL(fileURLWithPath: CommandLine.arguments[3])
let replacedPages = Set(CommandLine.arguments[4].split(separator: ",").compactMap { Int($0) })
let title = CommandLine.arguments[5]
let pageRect = CGRect(x: 0, y: 0, width: 612, height: 792)

guard let input = CGPDFDocument(inputURL as CFURL),
      let tables = CGPDFDocument(tablesURL as CFURL) else {
    fputs("Could not read source guide or generated opcode appendix.\n", stderr)
    exit(1)
}

var mediaBox = pageRect
guard let consumer = CGDataConsumer(url: outputURL as CFURL),
      let context = CGContext(consumer: consumer, mediaBox: &mediaBox, nil) else {
    fputs("Could not create refreshed PDF.\n", stderr)
    exit(1)
}

let cream = CGColor(red: 0.965, green: 0.949, blue: 0.910, alpha: 1)
let rule = CGColor(red: 0.851, green: 0.831, blue: 0.784, alpha: 1)
let teal = CGColor(red: 0.141, green: 0.498, blue: 0.486, alpha: 1)
let muted = CGColor(red: 0.388, green: 0.482, blue: 0.502, alpha: 1)
let white = CGColor(gray: 1, alpha: 1)

func drawLabel(_ text: String, x: CGFloat, y: CGFloat, size: CGFloat,
               color: CGColor, bold: Bool = false) {
    let fontName = bold ? "Helvetica-Bold" : "Helvetica"
    let font = CTFontCreateWithName(fontName as CFString, size, nil)
    let attributed = NSAttributedString(string: text, attributes: [
        NSAttributedString.Key(kCTFontAttributeName as String): font,
        NSAttributedString.Key(kCTForegroundColorAttributeName as String): color,
    ])
    let line = CTLineCreateWithAttributedString(attributed)
    context.textPosition = CGPoint(x: x, y: y)
    CTLineDraw(line, context)
}

func beginPage() {
    context.beginPDFPage(nil)
    context.setFillColor(cream)
    context.fill(pageRect)
    context.setStrokeColor(rule)
    context.setLineWidth(0.35)
    for y in stride(from: 42.0, through: 770.0, by: 28.0) {
        context.move(to: CGPoint(x: 0, y: y))
        context.addLine(to: CGPoint(x: 612, y: y))
    }
    context.strokePath()
}

func drawFrame(pageNumber: Int) {
    context.setFillColor(teal)
    context.addPath(CGPath(roundedRect: CGRect(x: 34, y: 752, width: 544, height: 32),
                           cornerWidth: 12, cornerHeight: 12, transform: nil))
    context.fillPath()
    drawLabel(title, x: 48, y: 764, size: 8, color: white, bold: true)
    context.setStrokeColor(teal)
    context.setLineWidth(0.8)
    context.move(to: CGPoint(x: 34, y: 27))
    context.addLine(to: CGPoint(x: 578, y: 27))
    context.strokePath()
    let pageText = String(format: "%02d", pageNumber)
    let font = CTFontCreateWithName("Helvetica-Bold" as CFString, 6.5, nil)
    let attr = NSAttributedString(string: "FIELD GUIDE  |  \(pageText)", attributes: [
        NSAttributedString.Key(kCTFontAttributeName as String): font,
        NSAttributedString.Key(kCTForegroundColorAttributeName as String): muted,
    ])
    let line = CTLineCreateWithAttributedString(attr)
    let width = CTLineGetTypographicBounds(line, nil, nil, nil)
    context.textPosition = CGPoint(x: 576 - width, y: 15)
    CTLineDraw(line, context)
}

func drawGuidePage(_ page: CGPDFPage, number: Int) {
    beginPage()
    let sourceRect = page.getBoxRect(.mediaBox)
    let bodyRect = CGRect(x: 31, y: 35, width: 550, height: 712)
    let scale = min(bodyRect.width / sourceRect.width, bodyRect.height / sourceRect.height)
    let width = sourceRect.width * scale
    let height = sourceRect.height * scale
    context.saveGState()
    context.translateBy(x: bodyRect.midX - width / 2, y: bodyRect.midY - height / 2)
    context.scaleBy(x: scale, y: scale)
    context.translateBy(x: -sourceRect.minX, y: -sourceRect.minY)
    context.drawPDFPage(page)
    context.restoreGState()
    context.setFillColor(cream)
    context.fill(CGRect(x: 29, y: 31, width: 554, height: 38))
    drawFrame(pageNumber: number)
    context.endPDFPage()
}

func drawAppendixPage(_ page: CGPDFPage) {
    beginPage()
    context.drawPDFPage(page)
    context.endPDFPage()
}

let insertionPage = replacedPages.min() ?? (input.numberOfPages + 1)
var outputPageNumber = 0
var insertedTables = false
for pageNumber in 1...input.numberOfPages {
    if pageNumber == insertionPage {
        for tableNumber in 1...tables.numberOfPages {
            guard let page = tables.page(at: tableNumber) else { continue }
            outputPageNumber += 1
            drawAppendixPage(page)
        }
        insertedTables = true
    }
    guard !replacedPages.contains(pageNumber), let page = input.page(at: pageNumber) else { continue }
    outputPageNumber += 1
    drawGuidePage(page, number: outputPageNumber)
}
if !insertedTables {
    for tableNumber in 1...tables.numberOfPages {
        if let page = tables.page(at: tableNumber) {
            outputPageNumber += 1
            drawAppendixPage(page)
        }
    }
}
context.closePDF()
