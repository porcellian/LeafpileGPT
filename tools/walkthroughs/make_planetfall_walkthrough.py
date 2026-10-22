#!/usr/bin/env python3
"""Build the Planetfall route guide and trace-derived opcode inventory."""

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
DATA_PATH = ROOT / "walkthroughs" / "source-data" / "planetfall.json"
OUTPUT_PATH = ROOT / "walkthroughs" / "planetfall-maximum-score.pdf"

CREAM = colors.HexColor("#F6F2E8")
TEAL = colors.HexColor("#247F7C")
NAVY = colors.HexColor("#153F4B")
MUTED = colors.HexColor("#637B80")
PALE = colors.HexColor("#E6ECE4")
PAPER = colors.HexColor("#FFFCF5")
CORAL = colors.HexColor("#D96C55")
PENCIL = colors.HexColor("#39464A")
LIGHT_LINE = colors.HexColor("#D9D4C8")


def refresh_trace(trace_path: Path) -> None:
    data = json.loads(DATA_PATH.read_text())
    summary = None
    for line in trace_path.read_text().splitlines():
        record = json.loads(line)
        if "opcodeInventory" in record:
            summary = record
    if summary is None:
        raise ValueError(f"Trace has no opcode inventory summary: {trace_path}")
    if sum(row["Count"] for row in summary["opcodeInventory"]) != summary["instructionsExecuted"]:
        raise ValueError("Opcode counts do not sum to total instructions executed")
    data["totalInstructions"] = summary["instructionsExecuted"]
    data["opcodeInventory"] = summary["opcodeInventory"]
    DATA_PATH.write_text(json.dumps(data, indent=2) + "\n")


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


def background(pdf: canvas.Canvas, size) -> tuple[float, float]:
    width, height = size
    pdf.setPageSize(size)
    pdf.setFillColor(CREAM)
    pdf.rect(0, 0, width, height, fill=1, stroke=0)
    pdf.setStrokeColor(LIGHT_LINE)
    pdf.setLineWidth(0.35)
    for y in range(42, int(height), 28):
        pdf.line(0, y, width, y)
    return width, height


def chrome(pdf: canvas.Canvas, label: str, page: int, size=letter,
           footer="PLANETFALL  |  NO-DEATH 80/80 ROUTE") -> tuple[float, float]:
    width, height = background(pdf, size)
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
              font="Helvetica", size=8.5, leading=11, color=NAVY) -> float:
    pdf.setFillColor(color)
    pdf.setFont(font, size)
    for line in wrap(text, font, size, width):
        pdf.drawString(x, y, line)
        y -= leading
    return y


def pill(pdf: canvas.Canvas, x: float, y: float, width: float,
         heading: str, detail: str, accent=TEAL) -> None:
    pdf.setFillColor(PAPER)
    pdf.roundRect(x, y - 54, width, 51, 8, fill=1, stroke=0)
    pdf.setFillColor(accent)
    pdf.setFont("Helvetica-Bold", 7.8)
    pdf.drawString(x + 9, y - 17, heading)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica", 6.3)
    for index, line in enumerate(wrap(detail, "Helvetica", 6.3, width - 18)[:3]):
        pdf.drawString(x + 9, y - 30 - index * 8, line)


def cover(pdf: canvas.Canvas, data: dict, page: int) -> None:
    width, height = chrome(pdf, "A Planetfall field guide", page)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 30)
    pdf.drawString(52, height - 100, "PLANETFALL")
    pdf.setFillColor(CORAL)
    pdf.setFont("Helvetica-Bold", 15)
    pdf.drawString(54, height - 126, "No-death maximum-score walkthrough")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 9)
    pdf.drawString(54, height - 147,
                   "A fresh-start command route to the cure, repaired systems, and Cryo-Anteroom ending")

    pdf.setFillColor(PAPER)
    pdf.roundRect(48, height - 250, width - 96, 79, 12, fill=1, stroke=0)
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 8)
    pdf.drawString(64, height - 190, "VERIFIED FINISH")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 23)
    pdf.drawString(64, height - 223, "80 / 80")
    pdf.setFont("Helvetica-Bold", 8.5)
    pdf.drawRightString(width - 64, height - 190,
                        f"{data['commandsCount']} commands  |  {data['moves']:,} moves  |  zero player deaths")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 7.5)
    pdf.drawRightString(width - 64, height - 211,
                        f"{data['totalInstructions']:,} executed instructions  |  {len(data['opcodeInventory'])} opcode rows")
    pdf.drawRightString(width - 64, height - 229,
                        f"Rank: {data['rank']}  |  Release {data['release']}  |  Serial {data['serial']}")

    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 11.5)
    pdf.drawString(54, height - 281, "How to use this route")
    y = height - 301
    for text in data["intro"]:
        y = paragraph(pdf, text, 56, y, width - 112, size=7.8, leading=9.5)
        y -= 5

    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 11.5)
    pdf.drawString(54, y - 1, "Score checkpoints")
    card_y = y - 18
    gap = 8
    card_width = (width - 108 - gap * 3) / 4
    for i, item in enumerate(data["checkpoints"]):
        pill(pdf, 54 + i * (card_width + gap), card_y, card_width,
             item["heading"], item["detail"], CORAL if i == 3 else TEAL)

    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 7)
    pdf.drawString(54, 65,
                   "Floyd's scripted sacrifice is part of the story; the player survives this route.")
    pdf.drawString(54, 52,
                   f"Replay seed {data['randomSeed']}  |  {len(data['visitedLocations'])} route-discovered locations mapped  |  route and trace remain local")
    pdf.showPage()


def command_page(pdf: canvas.Canvas, data: dict, start: int, end: int, page: int) -> None:
    width, height = chrome(pdf, "Numbered route", page)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 17)
    pdf.drawString(52, height - 72, "The escape and cure")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 8)
    pdf.drawString(54, height - 89,
                   f"Commands {start:03d}-{end:03d}; read down the left column, then down the right.")

    numbered = list(enumerate(data["commands"][start - 1:end], start=start))
    split = (len(numbered) + 1) // 2
    columns = [numbered[:split], numbered[split:]]
    x_positions = [54, 317]
    step_starts = data["stepStarts"]
    critical = set(data["criticalCommands"])
    for col_index, items in enumerate(columns):
        y = height - 116
        x = x_positions[col_index]
        for number, command in items:
            if str(number) in step_starts:
                pdf.setFillColor(CORAL)
                pdf.setFont("Helvetica-Bold", 6.2)
                pdf.drawString(x, y + 4, step_starts[str(number)])
                y -= 8
            pdf.setFillColor(CORAL if number in critical else TEAL)
            pdf.setFont("Courier-Bold", 7.8)
            pdf.drawRightString(x + 27, y, f"{number:03d}")
            pdf.setFillColor(NAVY)
            pdf.setFont("Courier", 7.8)
            lines = wrap(command, "Courier", 7.8, 218)
            for line_index, line in enumerate(lines):
                pdf.drawString(x + 34, y - line_index * 9.4, line)
            y -= max(19, 9.4 * len(lines) + 2)
    pdf.showPage()


def hand_line(pdf: canvas.Canvas, x1: float, y1: float, x2: float, y2: float,
              seed: int, arrow=True, color=PENCIL, width=1.0) -> None:
    dx, dy = x2 - x1, y2 - y1
    length = max(1, math.hypot(dx, dy))
    px, py = -dy / length, dx / length
    wobble = ((seed * 37) % 7 - 3) * 0.5
    pdf.setStrokeColor(color)
    pdf.setLineWidth(width)
    pdf.line(x1, y1, (x1 + x2) / 2 + px * wobble, (y1 + y2) / 2 + py * wobble)
    pdf.line((x1 + x2) / 2 - px * wobble * 0.3,
             (y1 + y2) / 2 - py * wobble * 0.3, x2, y2)
    if arrow:
        angle = math.atan2(dy, dx)
        for delta in (-0.5, 0.5):
            pdf.line(x2, y2,
                     x2 - 5 * math.cos(angle + delta),
                     y2 - 5 * math.sin(angle + delta))


def hand_box(pdf: canvas.Canvas, x: float, y: float, width: float,
             height: float, label: str, number: int, visited=True) -> None:
    pdf.saveState()
    pdf.setFillColor(PALE if visited else PAPER)
    pdf.setStrokeColor(PENCIL)
    pdf.setLineWidth(0.9)
    p = pdf.beginPath()
    p.moveTo(x + 1, y + 1)
    p.lineTo(x + width - 2, y + 2 + (number % 2))
    p.lineTo(x + width - 1, y + height - 2)
    p.lineTo(x + 3, y + height - 1 - (number % 3))
    p.close()
    pdf.drawPath(p, fill=1, stroke=1)
    pdf.setFillColor(TEAL)
    pdf.circle(x + 11, y + height / 2, 7, fill=1, stroke=0)
    pdf.setFillColor(colors.white)
    pdf.setFont("Helvetica-Bold", 5.4)
    pdf.drawCentredString(x + 11, y + height / 2 - 1.8, f"{number:02d}")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 6.8)
    lines = wrap(label, "Helvetica-Bold", 6.8, width - 32)[:2]
    label_y = y + height / 2 + (3 if len(lines) == 2 else -2)
    for line in lines:
        pdf.drawString(x + 23, label_y, line)
        label_y -= 8
    pdf.restoreState()


def doodle(pdf: canvas.Canvas, kind: str, x: float, y: float, scale=1.0) -> None:
    pdf.saveState()
    pdf.setStrokeColor(CORAL)
    pdf.setFillColor(PAPER)
    pdf.setLineWidth(1.1)
    if kind == "pod":
        pdf.ellipse(x - 18 * scale, y - 8 * scale, x + 18 * scale, y + 8 * scale, fill=0)
        pdf.line(x - 9 * scale, y, x + 8 * scale, y)
        pdf.line(x + 8 * scale, y, x + 3 * scale, y + 4 * scale)
        pdf.line(x + 8 * scale, y, x + 3 * scale, y - 4 * scale)
    elif kind == "shuttle":
        pdf.line(x - 20 * scale, y, x + 12 * scale, y)
        pdf.line(x + 12 * scale, y, x + 4 * scale, y + 7 * scale)
        pdf.line(x + 12 * scale, y, x + 4 * scale, y - 7 * scale)
        pdf.line(x - 7 * scale, y, x - 13 * scale, y + 8 * scale)
        pdf.line(x - 7 * scale, y, x - 13 * scale, y - 8 * scale)
    elif kind == "robot":
        pdf.roundRect(x - 11 * scale, y - 11 * scale, 22 * scale, 22 * scale, 3, fill=0)
        pdf.circle(x - 4 * scale, y + 3 * scale, 1.7 * scale, fill=1, stroke=0)
        pdf.circle(x + 4 * scale, y + 3 * scale, 1.7 * scale, fill=1, stroke=0)
        pdf.line(x - 4 * scale, y - 4 * scale, x + 4 * scale, y - 4 * scale)
        pdf.line(x, y + 11 * scale, x, y + 16 * scale)
    elif kind == "laser":
        pdf.roundRect(x - 17 * scale, y - 5 * scale, 25 * scale, 10 * scale, 2, fill=0)
        pdf.line(x + 8 * scale, y, x + 23 * scale, y)
        pdf.line(x - 8 * scale, y - 5 * scale, x - 8 * scale, y - 13 * scale)
    elif kind == "vial":
        pdf.line(x - 5 * scale, y + 12 * scale, x + 5 * scale, y + 12 * scale)
        pdf.line(x - 4 * scale, y + 12 * scale, x - 4 * scale, y + 5 * scale)
        pdf.line(x + 4 * scale, y + 12 * scale, x + 4 * scale, y + 5 * scale)
        pdf.line(x - 4 * scale, y + 5 * scale, x - 10 * scale, y - 10 * scale)
        pdf.line(x + 4 * scale, y + 5 * scale, x + 10 * scale, y - 10 * scale)
        pdf.line(x - 10 * scale, y - 10 * scale, x + 10 * scale, y - 10 * scale)
        pdf.line(x - 7 * scale, y - 3 * scale, x + 7 * scale, y - 3 * scale)
    pdf.restoreState()


def map_page(pdf: canvas.Canvas, data: dict, map_data: dict, page: int, map_index: int) -> None:
    width, height = chrome(pdf, f"Hand-sketched map  |  {map_data['title']}", page,
                           landscape(letter), "PLANETFALL  |  ROUTE MAP")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 16)
    pdf.drawString(48, height - 66, map_data["title"])
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 7.2)
    pdf.drawString(50, height - 81,
                   f"{len(map_data['rooms'])} rooms on this sheet; all {len(data['visitedLocations'])} route-discovered locations appear across the maps.")

    names = map_data["rooms"]
    box_w, box_h = 211, 32
    col_x = [43, 290, 537]
    top_y, gap_y = height - 128, 49
    positions: dict[str, tuple[float, float]] = {}
    for index, name in enumerate(names):
        col = min(2, index // 6)
        row_in_col = index % 6
        row = row_in_col if col % 2 == 0 else 5 - row_in_col
        x = col_x[col]
        y = top_y - row * gap_y
        positions[name] = (x, y)

    local_rooms = set(names)
    for edge_index, edge in enumerate(map_data["edges"]):
        target = edge["target"]
        if target not in positions:
            continue
        tx, ty = positions[target]
        if edge["source"] not in positions:
            pdf.setFillColor(CORAL)
            pdf.setFont("Helvetica-Oblique", 5.5)
            pdf.drawString(tx + box_w / 2 - 54, ty + box_h + 5,
                           f"from {edge['source'][:20]}")
            hand_line(pdf, tx + box_w / 2, ty + box_h + 3,
                      tx + box_w / 2, ty + box_h - 2, edge_index + map_index,
                      color=CORAL, width=0.8)
            continue
        sx, sy = positions[edge["source"]]
        sx += box_w / 2
        sy += box_h / 2
        tx += box_w / 2
        ty += box_h / 2
        dx, dy = tx - sx, ty - sy
        length = max(1, math.hypot(dx, dy))
        ux, uy = dx / length, dy / length
        source_edge = min((box_w / 2 - 4) / abs(ux) if ux else math.inf,
                          (box_h / 2 - 4) / abs(uy) if uy else math.inf)
        target_edge = min((box_w / 2 - 4) / abs(ux) if ux else math.inf,
                          (box_h / 2 - 4) / abs(uy) if uy else math.inf)
        sx += ux * source_edge
        sy += uy * source_edge
        tx -= ux * target_edge
        ty -= uy * target_edge
        hand_line(pdf, sx, sy, tx, ty, edge_index + map_index,
                  color=CORAL, width=0.85)
        label = edge["label"][:16]
        mx, my = (sx + tx) / 2, (sy + ty) / 2
        pdf.setFillColor(CREAM)
        pdf.roundRect(mx - 24, my - 4, 48, 9, 2, fill=1, stroke=0)
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 5.2)
        pdf.drawCentredString(mx, my - 1.7, label)

    for index, name in enumerate(names):
        x, y = positions[name]
        hand_box(pdf, x, y, box_w, box_h, name, index + 1)

    doodles = ["pod", "robot", "shuttle", "laser"]
    doodle(pdf, doodles[map_index - 1], 85, 112, 0.9)
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica-Oblique", 6.2)
    pdf.drawString(109, 109, ["pressure pod / surface", "Floyd and the access lifts",
                               "Alfie to the systems labs", "the laser and cure"][map_index - 1])
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 6.7)
    pdf.drawString(50, 59, "MAP KEY")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 6.5)
    pdf.drawString(103, 59, "Coral arrows show first-arrival travel from the successful route; return legs are omitted.")
    pdf.drawString(50, 47, "Room boxes follow the player's rough field notes. Connections and distances are schematic, not to scale.")
    pdf.showPage()


def inventory_pages(pdf: canvas.Canvas, data: dict, start_page: int) -> None:
    rows = data["opcodeInventory"]
    per_page = 20
    if sum(row["Count"] for row in rows) != data["totalInstructions"]:
        raise ValueError("Opcode counts do not sum to total instructions executed")
    total_pages = math.ceil(len(rows) / per_page)
    for index in range(total_pages):
        page = start_page + index
        width, height = chrome(pdf, "Executed opcode inventory", page)
        chunk = rows[index * per_page:(index + 1) * per_page]
        pdf.setFillColor(NAVY)
        pdf.setFont("Helvetica-Bold", 17)
        pdf.drawString(52, height - 73, f"Opcode table {index + 1} of {total_pages}")
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 8)
        pdf.drawString(54, height - 90,
                       "Rows follow first appearance in the successful fresh replay.")

        pdf.setFillColor(PAPER)
        pdf.roundRect(50, height - 143, width - 100, 37, 8, fill=1, stroke=0)
        pdf.setFillColor(TEAL)
        pdf.setFont("Helvetica-Bold", 8)
        pdf.drawString(62, height - 121, f"{len(rows)} unique form/opcode/mnemonic rows")
        pdf.drawRightString(width - 62, height - 121,
                            f"{data['totalInstructions']:,} total instructions executed")
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 7)
        pdf.drawString(54, height - 157,
                       "First Seq is one-based execution order; First PC is the first address in hexadecimal.")

        x0, y0 = 50, height - 183
        widths = [72, 48, 105, 79, 94, 114]
        headers = ["FORM", "OPCODE", "MNEMONIC", "COUNT", "FIRST SEQ", "FIRST PC"]
        total_width = sum(widths)
        pdf.setFillColor(TEAL)
        pdf.roundRect(x0, y0 - 25, total_width, 25, 5, fill=1, stroke=0)
        xpos = x0
        for col, label in enumerate(headers):
            pdf.setFillColor(colors.white)
            pdf.setFont("Helvetica-Bold", 7)
            if col in (1, 3, 4):
                pdf.drawRightString(xpos + widths[col] - 7, y0 - 16, label)
            else:
                pdf.drawString(xpos + 7, y0 - 16, label)
            xpos += widths[col]

        row_height = 24
        for row_index, row in enumerate(chunk):
            y = y0 - 25 - (row_index + 1) * row_height
            pdf.setFillColor(PALE if row_index % 2 == 0 else PAPER)
            pdf.rect(x0, y, total_width, row_height, fill=1, stroke=0)
            values = [row["Form"], str(row["Opcode"]), row["Mnemonic"],
                      f"{row['Count']:,}", f"{row['FirstSequence']:,}",
                      f"0x{row['FirstAddress']:04X}"]
            xpos = x0
            for col, value in enumerate(values):
                pdf.setFillColor(NAVY)
                pdf.setFont("Courier", 7.2)
                if col in (1, 3, 4):
                    pdf.drawRightString(xpos + widths[col] - 7, y + 8, value)
                else:
                    pdf.drawString(xpos + 7, y + 8, value)
                xpos += widths[col]
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica-Oblique", 6.8)
        pdf.drawString(54, 49, "Trace summary is from the same fresh replay used to verify the 80/80 route.")
        pdf.showPage()


def build(data: dict, output: Path) -> int:
    if len(data["commands"]) != data["commandsCount"]:
        raise ValueError("Command count does not match route data")
    if sum(row["Count"] for row in data["opcodeInventory"]) != data["totalInstructions"]:
        raise ValueError("Opcode counts do not sum to total instructions executed")
    pdf = canvas.Canvas(str(output), pagesize=letter, pageCompression=1)
    pdf.setTitle("Planetfall - Maximum-Score Walkthrough")
    pdf.setAuthor("LeafpileGPT project")
    pdf.setSubject("Verified 80-point route, hand-sketched maps, and executed opcode inventory")
    page = 1
    cover(pdf, data, page)
    page += 1
    per_page = 46
    for start in range(1, len(data["commands"]) + 1, per_page):
        end = min(start + per_page - 1, len(data["commands"]))
        command_page(pdf, data, start, end, page)
        page += 1
    for map_index, map_data in enumerate(data["maps"], start=1):
        map_page(pdf, data, map_data, page, map_index)
        page += 1
    inventory_pages(pdf, data, page)
    pdf.save()
    return page + math.ceil(len(data["opcodeInventory"]) / 20) - 1


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--refresh-trace", type=Path,
                        help="refresh the committed opcode inventory from a local RouteReplay trace")
    parser.add_argument("--output", type=Path, default=OUTPUT_PATH)
    args = parser.parse_args()
    if args.refresh_trace:
        refresh_trace(args.refresh_trace)
    data = json.loads(DATA_PATH.read_text())
    data["commandsCount"] = len(data["commands"])
    pages = build(data, args.output)
    print(f"Created {args.output}: {data['score']}/{data['maximumScore']}, "
          f"{data['totalInstructions']:,} instructions, {len(data['opcodeInventory'])} opcode rows, "
          f"{len(data['visitedLocations'])} locations, {pages} pages")


if __name__ == "__main__":
    main()
