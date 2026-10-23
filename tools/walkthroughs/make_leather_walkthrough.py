#!/usr/bin/env python3
"""Build the Leather Goddesses of Phobos route guide and trace inventory."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.pagesizes import landscape, letter
from reportlab.pdfbase.pdfmetrics import stringWidth
from reportlab.pdfgen import canvas


ROOT = Path(__file__).resolve().parents[2]
DATA_PATH = ROOT / "walkthroughs" / "source-data" / "leather.json"
OUTPUT_PATH = ROOT / "walkthroughs" / "leather-maximum-score.pdf"

CREAM = colors.HexColor("#F6F2E8")
TEAL = colors.HexColor("#247F7C")
NAVY = colors.HexColor("#153F4B")
MUTED = colors.HexColor("#637B80")
PALE = colors.HexColor("#E6ECE4")
PAPER = colors.HexColor("#FFFCF5")
CORAL = colors.HexColor("#D96C55")
PENCIL = colors.HexColor("#39464A")
RULE = colors.HexColor("#D9D4C8")


def refresh_trace(trace_path: Path) -> None:
    data = json.loads(DATA_PATH.read_text())
    summary = None
    with trace_path.open() as stream:
        for line in stream:
            record = json.loads(line)
            if "opcodeInventory" in record:
                summary = record
    if summary is None:
        raise ValueError(f"Trace has no opcode inventory summary: {trace_path}")
    if sum(row["Count"] for row in summary["opcodeInventory"]) != summary["instructionsExecuted"]:
        raise ValueError("Opcode counts do not sum to total instructions executed")
    data["totalInstructions"] = summary["instructionsExecuted"]
    data["opcodeInventory"] = summary["opcodeInventory"]
    DATA_PATH.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n")


def wrap(text: str, font: str, size: float, width: float) -> list[str]:
    lines: list[str] = []
    current = ""
    for word in text.split():
        candidate = word if not current else f"{current} {word}"
        if stringWidth(candidate, font, size) <= width:
            current = candidate
        else:
            if current:
                lines.append(current)
            current = word
    if current:
        lines.append(current)
    return lines


def chrome(pdf: canvas.Canvas, label: str, page: int, size=letter,
           footer="LEATHER GODDESSES  |  248 / 248 ROUTE") -> tuple[float, float]:
    width, height = size
    pdf.setPageSize(size)
    pdf.setFillColor(CREAM)
    pdf.rect(0, 0, width, height, fill=1, stroke=0)
    pdf.setStrokeColor(RULE)
    pdf.setLineWidth(0.35)
    for y in range(42, int(height), 28):
        pdf.line(0, y, width, y)
    pdf.setFillColor(TEAL)
    pdf.roundRect(34, height - 39, width - 68, 30, 10, fill=1, stroke=0)
    pdf.setFillColor(colors.white)
    pdf.setFont("Helvetica-Bold", 8)
    pdf.drawString(48, height - 27, label.upper())
    pdf.setStrokeColor(TEAL)
    pdf.setLineWidth(0.8)
    pdf.line(34, 27, width - 34, 27)
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica-Bold", 6.5)
    pdf.drawString(36, 15, footer)
    pdf.drawRightString(width - 36, 15, f"FIELD GUIDE  |  {page:02d}")
    return width, height


def paragraph(pdf: canvas.Canvas, text: str, x: float, y: float, width: float,
              font="Helvetica", size=7.5, leading=9, color=NAVY) -> float:
    pdf.setFillColor(color)
    pdf.setFont(font, size)
    for line in wrap(text, font, size, width):
        pdf.drawString(x, y, line)
        y -= leading
    return y


def cover(pdf: canvas.Canvas, data: dict) -> None:
    width, height = chrome(pdf, "A Leather Goddesses field guide", 1)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 25)
    pdf.drawString(52, height - 83, "LEATHER GODDESSES")
    pdf.drawString(52, height - 108, "OF PHOBOS")
    pdf.setFillColor(CORAL)
    pdf.setFont("Helvetica-Bold", 12)
    pdf.drawString(54, height - 130, data["subtitle"])

    pdf.setFillColor(PAPER)
    pdf.roundRect(48, height - 218, width - 96, 72, 12, fill=1, stroke=0)
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 8)
    pdf.drawString(64, height - 165, "VERIFIED FINISH")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 23)
    pdf.drawString(64, height - 194, f"{data['score']} / {data['maximumScore']}")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica-Bold", 7.2)
    pdf.drawRightString(width - 64, height - 165,
                        f"{data['commandsCount']} commands  |  479 story turns  |  no player death")
    pdf.setFont("Helvetica", 7)
    pdf.drawRightString(width - 64, height - 184,
                        f"{data['totalInstructions']:,} executed instructions")
    pdf.drawRightString(width - 64, height - 201,
                        f"{len(data['opcodeInventory'])} opcode rows  |  V3 release {data['release']}  |  Serial {data['serial']}")

    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 10)
    pdf.drawString(54, height - 242, "Before you start")
    y = height - 258
    for text in data["intro"]:
        y = paragraph(pdf, text, 56, y, width - 112, size=7, leading=8.3)
        y -= 4

    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 9.5)
    pdf.drawString(54, y - 2, "Score and item checkpoints")
    top = y - 16
    gap_x = 7
    card_width = (width - 108 - gap_x * 2) / 3
    for index, item in enumerate(data["checkpoints"]):
        column, row = index % 3, index // 3
        x = 54 + column * (card_width + gap_x)
        card_top = top - row * 42
        pdf.setFillColor(PAPER)
        pdf.roundRect(x, card_top - 37, card_width, 36, 6, fill=1, stroke=0)
        pdf.setFillColor(CORAL if index == len(data["checkpoints"]) - 1 else TEAL)
        pdf.setFont("Helvetica-Bold", 6.5)
        pdf.drawString(x + 7, card_top - 13, f"{item['heading']}  ·  #{item['command']:03d}")
        pdf.setFillColor(NAVY)
        pdf.setFont("Helvetica", 5.8)
        for row_index, line in enumerate(wrap(item["detail"], "Helvetica", 5.8, card_width - 14)[:2]):
            pdf.drawString(x + 7, card_top - 23 - row_index * 7, line)

    pdf.setFillColor(PAPER)
    pdf.roundRect(54, 49, width - 108, 31, 7, fill=1, stroke=0)
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 6.8)
    pdf.drawString(64, 67, "ENDING VERIFIED")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica", 6.2)
    pdf.drawString(149, 67, data["endingVerification"])
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica-Oblique", 6)
    pdf.drawString(54, 37,
                   f"Maps cover all {len(data['visitedLocations'])} rooms visited on this route; directions are schematic.")
    pdf.showPage()


def command_page(pdf: canvas.Canvas, data: dict, start: int, end: int, page: int) -> None:
    width, height = chrome(pdf, "Numbered route", page)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 15)
    pdf.drawString(52, height - 68, "The route to save Earth")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 7.1)
    pdf.drawString(54, height - 83,
                   f"Commands {start:03d}–{end:03d}; read down the left column, then down the right.")
    items = list(enumerate(data["commands"][start - 1:end], start=start))
    split = (len(items) + 1) // 2
    columns = [items[:split], items[split:]]
    x_positions = [52, 315]
    labels = data["stepStarts"]
    critical = set(data["criticalCommands"])
    for column_items, x in zip(columns, x_positions):
        y = height - 108
        for number, command in column_items:
            if str(number) in labels:
                pdf.setFillColor(CORAL)
                pdf.setFont("Helvetica-Bold", 6)
                pdf.drawString(x, y + 4, labels[str(number)])
                y -= 7
            pdf.setFillColor(CORAL if number in critical else TEAL)
            pdf.setFont("Courier-Bold", 7)
            pdf.drawRightString(x + 27, y, f"{number:03d}")
            pdf.setFillColor(NAVY)
            pdf.setFont("Courier", 7)
            lines = wrap(command, "Courier", 7, 219)
            for line_index, line in enumerate(lines):
                pdf.drawString(x + 34, y - line_index * 8, line)
            y -= max(14, 8 * len(lines) + 2)
    pdf.showPage()


def hand_line(pdf: canvas.Canvas, x1: float, y1: float, x2: float, y2: float,
              seed: int, color=CORAL, width=0.85) -> None:
    dx, dy = x2 - x1, y2 - y1
    length = max(1, math.hypot(dx, dy))
    px, py = -dy / length, dx / length
    wobble = ((seed * 37) % 7 - 3) * 0.42
    pdf.setStrokeColor(color)
    pdf.setLineWidth(width)
    mx, my = (x1 + x2) / 2, (y1 + y2) / 2
    pdf.line(x1, y1, mx + px * wobble, my + py * wobble)
    pdf.line(mx - px * wobble * 0.3, my - py * wobble * 0.3, x2, y2)
    angle = math.atan2(dy, dx)
    for delta in (-0.48, 0.48):
        pdf.line(x2, y2, x2 - 4 * math.cos(angle + delta), y2 - 4 * math.sin(angle + delta))


def hand_box(pdf: canvas.Canvas, x: float, y: float, width: float, height: float,
             label: str, number: int) -> None:
    pdf.saveState()
    pdf.setFillColor(PALE)
    pdf.setStrokeColor(PENCIL)
    pdf.setLineWidth(0.85)
    path = pdf.beginPath()
    path.moveTo(x + 1, y + 1)
    path.lineTo(x + width - 2, y + 2 + number % 2)
    path.lineTo(x + width - 1, y + height - 2)
    path.lineTo(x + 2 + number % 3, y + height - 1)
    path.close()
    pdf.drawPath(path, fill=1, stroke=1)
    pdf.setFillColor(TEAL)
    pdf.circle(x + 10, y + height / 2, 6.1, fill=1, stroke=0)
    pdf.setFillColor(colors.white)
    pdf.setFont("Helvetica-Bold", 4.8)
    pdf.drawCentredString(x + 10, y + height / 2 - 1.7, f"{number:02d}")
    lines = wrap(label, "Helvetica-Bold", 6.2, width - 27)[:2]
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 6.2)
    label_y = y + height / 2 + (2.5 if len(lines) == 2 else -1.8)
    for line in lines:
        pdf.drawString(x + 20, label_y, line)
        label_y -= 7
    pdf.restoreState()


def doodle(pdf: canvas.Canvas, kind: str, x: float, y: float) -> None:
    pdf.saveState()
    pdf.setStrokeColor(CORAL)
    pdf.setFillColor(PAPER)
    pdf.setLineWidth(1.05)
    if kind == "glass":
        pdf.line(x - 14, y + 12, x + 14, y + 12)
        pdf.line(x - 10, y + 12, x - 7, y - 5)
        pdf.line(x + 10, y + 12, x + 7, y - 5)
        pdf.arc(x - 7, y - 10, x + 7, y - 1, 180, 180)
        pdf.line(x, y - 5, x, y - 15)
        pdf.line(x - 8, y - 15, x + 8, y - 15)
        pdf.circle(x + 4, y + 4, 1.5, fill=0, stroke=1)
    elif kind == "planet":
        pdf.ellipse(x - 21, y - 11, x + 21, y + 11, fill=0, stroke=1)
        pdf.circle(x, y, 9, fill=0, stroke=1)
        pdf.circle(x - 3, y + 2, 1, fill=1, stroke=1)
        pdf.circle(x + 4, y - 3, 1, fill=1, stroke=1)
    else:
        pdf.line(x, y + 16, x - 10, y - 2)
        pdf.line(x - 10, y - 2, x, y - 8)
        pdf.line(x, y - 8, x + 10, y - 2)
        pdf.line(x + 10, y - 2, x, y + 16)
        pdf.line(x - 6, y + 5, x - 14, y + 9)
        pdf.line(x + 6, y + 5, x + 14, y + 9)
        pdf.line(x - 3, y - 5, x - 7, y - 13)
        pdf.line(x + 3, y - 5, x + 7, y - 13)
        pdf.circle(x, y + 18, 2.3, fill=0, stroke=1)
    pdf.restoreState()


def map_page(pdf: canvas.Canvas, data: dict, map_data: dict, page: int, map_index: int) -> None:
    width, height = chrome(pdf, f"Hand-drawn map | {map_data['title']}", page,
                           landscape(letter), "LEATHER GODDESSES  |  ROUTE MAP")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 14)
    pdf.drawString(43, height - 65, map_data["title"])
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 6.8)
    pdf.drawString(45, height - 78,
                   f"{len(map_data['rooms'])} rooms; the four sketches cover all {len(data['visitedLocations'])} visited locations.")
    names = map_data["rooms"]
    box_w, box_h = 190, 26
    col_x = [38, 301, 564]
    row_gap = 37
    top_y = height - 111
    positions = {}
    layout = map_data["layout"]
    if set(layout) != set(names):
        raise ValueError(f"Map layout does not cover every room in {map_data['title']}")
    for name in names:
        col, row = layout[name]
        positions[name] = (col_x[col], top_y - row * row_gap)

    cross_sheet = []
    for edge_index, edge in enumerate(map_data["edges"]):
        source, target = edge["source"], edge["target"]
        if source not in positions or target not in positions:
            if source in positions or target in positions:
                cross_sheet.append(f"{source} → {target}")
            continue
        sx, sy = positions[source]
        tx, ty = positions[target]
        sx += box_w / 2
        sy += box_h / 2
        tx += box_w / 2
        ty += box_h / 2
        dx, dy = tx - sx, ty - sy
        length = max(1, math.hypot(dx, dy))
        ux, uy = dx / length, dy / length
        trim = min((box_w / 2 - 4) / abs(ux) if ux else math.inf,
                   (box_h / 2 - 4) / abs(uy) if uy else math.inf)
        sx += ux * trim
        sy += uy * trim
        tx -= ux * trim
        ty -= uy * trim
        hand_line(pdf, sx, sy, tx, ty, edge_index + map_index)
        if edge.get("label"):
            mx, my = (sx + tx) / 2, (sy + ty) / 2
            pdf.setFillColor(CREAM)
            pdf.roundRect(mx - 12, my - 3, 24, 7, 2, fill=1, stroke=0)
            pdf.setFillColor(MUTED)
            pdf.setFont("Helvetica", 4.8)
            pdf.drawCentredString(mx, my - 1.5, edge["label"][:9])

    for index, name in enumerate(names):
        x, y = positions[name]
        hand_box(pdf, x, y, box_w, box_h, name, index + 1)
    doodle(pdf, map_data["icon"], 72, 83)
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica-Oblique", 6)
    pdf.drawString(102, 81, map_data["caption"])
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 6.2)
    pdf.drawString(45, 52, "MAP KEY")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 5.9)
    pdf.drawString(93, 52, "Coral arrows show first-arrival travel in the successful route; room spacing is schematic.")
    if cross_sheet:
        pdf.setFillColor(CORAL)
        pdf.setFont("Helvetica-Oblique", 5.6)
        text = "Sheet continuation: " + "; ".join(cross_sheet)
        for index, line in enumerate(wrap(text, "Helvetica-Oblique", 5.6, width - 95)[:2]):
            pdf.drawString(45, 67 - index * 7, line)
    pdf.showPage()


def inventory_pages(pdf: canvas.Canvas, data: dict, start_page: int) -> int:
    rows = data["opcodeInventory"]
    if sum(row["Count"] for row in rows) != data["totalInstructions"]:
        raise ValueError("Opcode counts do not sum to total instructions executed")
    per_page = 20
    total_pages = math.ceil(len(rows) / per_page)
    for index in range(total_pages):
        page = start_page + index
        width, height = chrome(pdf, "Executed opcode inventory", page)
        chunk = rows[index * per_page:(index + 1) * per_page]
        pdf.setFillColor(NAVY)
        pdf.setFont("Helvetica-Bold", 16)
        pdf.drawString(52, height - 69, f"Opcode table {index + 1} of {total_pages}")
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 7.4)
        pdf.drawString(54, height - 85, "Rows are grouped by actual form, opcode number, and mnemonic; order is first occurrence.")
        pdf.setFillColor(PAPER)
        pdf.roundRect(50, height - 132, width - 100, 33, 8, fill=1, stroke=0)
        pdf.setFillColor(TEAL)
        pdf.setFont("Helvetica-Bold", 7.5)
        pdf.drawString(62, height - 111, f"{len(rows)} unique form/opcode/mnemonic rows")
        pdf.drawRightString(width - 62, height - 111,
                            f"{data['totalInstructions']:,} total instructions executed")
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 6.8)
        pdf.drawString(54, height - 147, "First Seq is one-based execution order; First PC is the first address in hexadecimal.")
        x0, y0 = 50, height - 169
        widths = [72, 48, 105, 79, 94, 114]
        headers = ["FORM", "OPCODE", "MNEMONIC", "COUNT", "FIRST SEQ", "FIRST PC"]
        table_width = sum(widths)
        pdf.setFillColor(TEAL)
        pdf.roundRect(x0, y0 - 24, table_width, 24, 5, fill=1, stroke=0)
        xpos = x0
        for col, label in enumerate(headers):
            pdf.setFillColor(colors.white)
            pdf.setFont("Helvetica-Bold", 6.8)
            if col in (1, 3, 4):
                pdf.drawRightString(xpos + widths[col] - 7, y0 - 15, label)
            else:
                pdf.drawString(xpos + 7, y0 - 15, label)
            xpos += widths[col]
        row_height = 22
        for row_index, row in enumerate(chunk):
            y = y0 - 24 - (row_index + 1) * row_height
            pdf.setFillColor(PALE if row_index % 2 == 0 else PAPER)
            pdf.rect(x0, y, table_width, row_height, fill=1, stroke=0)
            values = [row["Form"], str(row["Opcode"]), row["Mnemonic"],
                      f"{row['Count']:,}", f"{row['FirstSequence']:,}",
                      f"0x{row['FirstAddress']:04X}"]
            xpos = x0
            for col, value in enumerate(values):
                pdf.setFillColor(NAVY)
                pdf.setFont("Courier", 7)
                if col in (1, 3, 4):
                    pdf.drawRightString(xpos + widths[col] - 7, y + 7, value)
                else:
                    pdf.drawString(xpos + 7, y + 7, value)
                xpos += widths[col]
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica-Oblique", 6.4)
        pdf.drawString(54, 46, "The inventory and total are derived from the same successful fresh replay as this guide.")
        pdf.showPage()
    return total_pages


def build(data: dict, output: Path) -> int:
    if len(data["commands"]) != data["commandsCount"]:
        raise ValueError("Command count does not match route data")
    if len(set(data["visitedLocations"])) != len(data["visitedLocations"]):
        raise ValueError("Route-discovered location list contains duplicates")
    if set(data["visitedLocations"]) != {room for map_data in data["maps"] for room in map_data["rooms"]}:
        raise ValueError("Maps do not cover every route-discovered location")
    if sum(row["Count"] for row in data["opcodeInventory"]) != data["totalInstructions"]:
        raise ValueError("Opcode counts do not sum to total instructions executed")
    output.parent.mkdir(parents=True, exist_ok=True)
    pdf = canvas.Canvas(str(output), pagesize=letter, pageCompression=1)
    pdf.setTitle("Leather Goddesses of Phobos - Maximum-Score Walkthrough")
    pdf.setAuthor("LeafpileGPT project")
    pdf.setSubject("Verified Leather Goddesses route, hand-drawn maps, and executed opcode inventory")
    cover(pdf, data)
    page = 2
    per_page = 56
    for start in range(1, len(data["commands"]) + 1, per_page):
        command_page(pdf, data, start, min(start + per_page - 1, len(data["commands"])), page)
        page += 1
    for map_index, map_data in enumerate(data["maps"], start=1):
        map_page(pdf, data, map_data, page, map_index)
        page += 1
    table_pages = inventory_pages(pdf, data, page)
    pdf.save()
    return page + table_pages - 1


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--refresh-trace", type=Path,
                        help="refresh the inventory from a local RouteReplay JSONL trace")
    parser.add_argument("--output", type=Path, default=OUTPUT_PATH)
    args = parser.parse_args()
    if args.refresh_trace:
        refresh_trace(args.refresh_trace)
    data = json.loads(DATA_PATH.read_text())
    pages = build(data, args.output)
    print(f"Created {args.output}: {data['score']}/{data['maximumScore']}, "
          f"{data['totalInstructions']:,} instructions, {len(data['opcodeInventory'])} opcode rows, "
          f"{len(data['visitedLocations'])} locations, {pages} pages")


if __name__ == "__main__":
    main()
