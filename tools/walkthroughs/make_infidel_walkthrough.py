#!/usr/bin/env python3
"""Build Infidel's verified no-death 400-point route guide."""

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
DATA_PATH = ROOT / "walkthroughs" / "source-data" / "infidel.json"
OUTPUT_PATH = ROOT / "walkthroughs" / "infidel-maximum-score.pdf"

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
    with trace_path.open("rb") as trace:
        trace.seek(0, 2)
        trace.seek(max(0, trace.tell() - 64_000))
        for line in trace.read().decode().splitlines():
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


def chrome(pdf: canvas.Canvas, label: str, page: int, size=letter,
           footer="INFIDEL  |  400 / 400 NO-DEATH ROUTE") -> tuple[float, float]:
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
              font="Helvetica", size=8, leading=10, color=NAVY) -> float:
    pdf.setFillColor(color)
    pdf.setFont(font, size)
    for line in wrap(text, font, size, width):
        pdf.drawString(x, y, line)
        y -= leading
    return y


def cover(pdf: canvas.Canvas, data: dict) -> None:
    width, height = chrome(pdf, "An Infidel field guide", 1)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 30)
    pdf.drawString(52, height - 91, "INFIDEL")
    pdf.setFillColor(CORAL)
    pdf.setFont("Helvetica-Bold", 14)
    pdf.drawString(54, height - 115, "Maximum-score no-death walkthrough")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 8.5)
    pdf.drawString(54, height - 133,
                   "A fresh-start route from the expedition camp to the buried pyramid's 400-point finish")

    pdf.setFillColor(PAPER)
    pdf.roundRect(48, height - 226, width - 96, 75, 12, fill=1, stroke=0)
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 8)
    pdf.drawString(64, height - 169, "VERIFIED SAFE FINISH")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 23)
    pdf.drawString(64, height - 201, "400 / 400")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica-Bold", 8)
    pdf.drawRightString(width - 64, height - 169,
                        f"{data['commandsCount']} commands  |  {data['moves']} moves  |  zero player deaths")
    pdf.setFont("Helvetica", 7.5)
    pdf.drawRightString(width - 64, height - 189,
                        f"{data['totalInstructions']:,} executed instructions  |  seed {data['randomSeed']}")
    pdf.drawRightString(width - 64, height - 207,
                        f"{len(data['opcodeInventory'])} opcode rows  |  Release {data['release']}  |  Serial {data['serial']}")

    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 11)
    pdf.drawString(54, height - 252, "Before you start")
    y = height - 270
    for text in data["intro"]:
        y = paragraph(pdf, text, 56, y, width - 112, size=7.5, leading=9)
        y -= 5

    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 10.5)
    pdf.drawString(54, y - 1, "Score checkpoints")
    card_y = y - 14
    gap = 8
    card_width = (width - 108 - gap * 2) / 3
    for i, item in enumerate(data["checkpoints"]):
        col, row = i % 3, i // 3
        x = 54 + col * (card_width + gap)
        top = card_y - row * 55
        pdf.setFillColor(PAPER)
        pdf.roundRect(x, top - 47, card_width, 44, 7, fill=1, stroke=0)
        pdf.setFillColor(CORAL if i == len(data["checkpoints"]) - 1 else TEAL)
        pdf.setFont("Helvetica-Bold", 7.7)
        pdf.drawString(x + 9, top - 16, item["heading"])
        pdf.setFillColor(NAVY)
        pdf.setFont("Helvetica", 6.4)
        for j, line in enumerate(wrap(item["detail"], "Helvetica", 6.4, card_width - 18)[:2]):
            pdf.drawString(x + 9, top - 28 - j * 8, line)

    pdf.setFillColor(PAPER)
    pdf.roundRect(54, 282, width - 108, 35, 7, fill=1, stroke=0)
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 7)
    pdf.drawString(64, 303, "ENDING VERIFIED")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica", 6.3)
    for i, line in enumerate(wrap(data["endingVerification"], "Helvetica", 6.3, width - 180)[:2]):
        pdf.drawString(159, 303 - i * 8, line)

    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 7)
    pdf.drawString(54, 48,
                   f"The route visits {len(data['visitedLocations'])} locations across four schematic field maps.")
    pdf.drawString(54, 37,
                   "The fatal sarcophagus opening is intentionally omitted; quit after confirming 400 / 400.")
    pdf.showPage()


def command_page(pdf: canvas.Canvas, data: dict, start: int, end: int, page: int) -> None:
    width, height = chrome(pdf, "Numbered route", page)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 16)
    pdf.drawString(52, height - 69, "The route through the tomb")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 7.5)
    pdf.drawString(54, height - 85,
                   f"Commands {start:03d}-{end:03d}; read down the left column, then down the right.")
    items = list(enumerate(data["commands"][start - 1:end], start=start))
    split = (len(items) + 1) // 2
    columns = [items[:split], items[split:]]
    x_positions = [52, 315]
    labels = data["stepStarts"]
    critical = set(data["criticalCommands"])
    for column, column_items in enumerate(columns):
        x, y = x_positions[column], height - 111
        for number, command in column_items:
            if str(number) in labels:
                pdf.setFillColor(CORAL)
                pdf.setFont("Helvetica-Bold", 6.2)
                pdf.drawString(x, y + 4, labels[str(number)])
                y -= 8
            pdf.setFillColor(CORAL if number in critical else TEAL)
            pdf.setFont("Courier-Bold", 7.3)
            pdf.drawRightString(x + 27, y, f"{number:03d}")
            pdf.setFillColor(NAVY)
            pdf.setFont("Courier", 7.3)
            lines = wrap(command, "Courier", 7.3, 219)
            for line_index, line in enumerate(lines):
                pdf.drawString(x + 34, y - line_index * 8.7, line)
            y -= max(17, 8.7 * len(lines) + 2)
    pdf.showPage()


def hand_line(pdf: canvas.Canvas, x1: float, y1: float, x2: float, y2: float,
              seed: int, color=CORAL, width=0.9) -> None:
    dx, dy = x2 - x1, y2 - y1
    length = max(1, math.hypot(dx, dy))
    px, py = -dy / length, dx / length
    wobble = ((seed * 37) % 7 - 3) * 0.45
    pdf.setStrokeColor(color)
    pdf.setLineWidth(width)
    mx, my = (x1 + x2) / 2, (y1 + y2) / 2
    pdf.line(x1, y1, mx + px * wobble, my + py * wobble)
    pdf.line(mx - px * wobble * 0.3, my - py * wobble * 0.3, x2, y2)
    angle = math.atan2(dy, dx)
    for delta in (-0.48, 0.48):
        pdf.line(x2, y2, x2 - 4.5 * math.cos(angle + delta), y2 - 4.5 * math.sin(angle + delta))


def hand_box(pdf: canvas.Canvas, x: float, y: float, width: float, height: float,
             label: str, number: int) -> None:
    pdf.saveState()
    pdf.setFillColor(PALE)
    pdf.setStrokeColor(PENCIL)
    pdf.setLineWidth(0.9)
    path = pdf.beginPath()
    path.moveTo(x + 1, y + 1)
    path.lineTo(x + width - 2, y + 2 + number % 2)
    path.lineTo(x + width - 1, y + height - 2)
    path.lineTo(x + 2 + number % 3, y + height - 1)
    path.close()
    pdf.drawPath(path, fill=1, stroke=1)
    pdf.setFillColor(TEAL)
    pdf.circle(x + 11, y + height / 2, 6.7, fill=1, stroke=0)
    pdf.setFillColor(colors.white)
    pdf.setFont("Helvetica-Bold", 5.2)
    pdf.drawCentredString(x + 11, y + height / 2 - 1.8, f"{number:02d}")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 6.7)
    lines = wrap(label, "Helvetica-Bold", 6.7, width - 30)[:2]
    label_y = y + height / 2 + (3 if len(lines) == 2 else -2)
    for line in lines:
        pdf.drawString(x + 22, label_y, line)
        label_y -= 8
    pdf.restoreState()


def doodle(pdf: canvas.Canvas, kind: str, x: float, y: float) -> None:
    pdf.saveState()
    pdf.setStrokeColor(CORAL)
    pdf.setFillColor(PAPER)
    pdf.setLineWidth(1.15)
    if kind == "tent":
        pdf.line(x - 20, y - 10, x, y + 12)
        pdf.line(x, y + 12, x + 20, y - 10)
        pdf.line(x - 20, y - 10, x + 20, y - 10)
        pdf.line(x, y + 12, x, y - 10)
        pdf.line(x + 10, y + 13, x + 10, y + 21)
    elif kind == "pyramid":
        pdf.line(x - 22, y - 10, x, y + 15)
        pdf.line(x, y + 15, x + 22, y - 10)
        pdf.line(x - 22, y - 10, x + 22, y - 10)
        pdf.line(x - 11, y - 10, x - 4, y - 1)
        pdf.line(x - 4, y - 1, x + 4, y - 1)
        pdf.line(x + 4, y - 1, x + 11, y - 10)
    elif kind == "barge":
        path = pdf.beginPath()
        path.moveTo(x - 24, y - 5)
        path.lineTo(x + 23, y - 5)
        path.lineTo(x + 15, y - 14)
        path.lineTo(x - 15, y - 14)
        path.close()
        pdf.drawPath(path, fill=0, stroke=1)
        pdf.line(x, y - 5, x, y + 17)
        pdf.line(x, y + 17, x + 15, y + 3)
        pdf.line(x, y + 3, x - 9, y + 9)
    else:
        pdf.rect(x - 19, y - 12, 38, 24, fill=0, stroke=1)
        pdf.line(x - 15, y - 8, x + 15, y + 8)
        pdf.line(x - 15, y + 8, x + 15, y - 8)
        pdf.circle(x, y, 3, fill=0, stroke=1)
    pdf.restoreState()


def map_page(pdf: canvas.Canvas, data: dict, map_data: dict, page: int, map_index: int) -> None:
    width, height = chrome(pdf, f"Hand-drawn map  |  {map_data['title']}", page,
                           landscape(letter), "INFIDEL  |  ROUTE MAP")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 15)
    pdf.drawString(45, height - 66, map_data["title"])
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 7)
    pdf.drawString(47, height - 80,
                   f"{len(map_data['rooms'])} locations on this sheet; all {len(data['visitedLocations'])} route-discovered rooms are mapped.")
    names = map_data["rooms"]
    box_w, box_h = 190, 29
    col_x = [41, 301, 561]
    rows_per_col = min(6, math.ceil(len(names) / 3))
    top_y, gap_y = height - 124, 50
    positions = {}
    for index, name in enumerate(names):
        col = min(2, index // rows_per_col)
        row = index % rows_per_col
        if col == 1:
            row = rows_per_col - 1 - row
        positions[name] = (col_x[col], top_y - row * gap_y)

    unique_edges = []
    for edge in map_data["edges"]:
        if edge.get("firstArrival", False):
            key = (edge["source"], edge["target"], edge["label"])
            if key not in {(item["source"], item["target"], item["label"]) for item in unique_edges}:
                unique_edges.append(edge)
    for edge_index, edge in enumerate(unique_edges):
        source, target = edge["source"], edge["target"]
        sx, sy = positions[source]
        tx, ty = positions[target]
        sx += box_w / 2
        sy += box_h / 2
        tx += box_w / 2
        ty += box_h / 2
        dx, dy = tx - sx, ty - sy
        length = max(1, math.hypot(dx, dy))
        ux, uy = dx / length, dy / length
        edge_trim = min((box_w / 2 - 4) / abs(ux) if ux else math.inf,
                        (box_h / 2 - 4) / abs(uy) if uy else math.inf)
        sx += ux * edge_trim
        sy += uy * edge_trim
        tx -= ux * edge_trim
        ty -= uy * edge_trim
        hand_line(pdf, sx, sy, tx, ty, edge_index + map_index)
        mx, my = (sx + tx) / 2, (sy + ty) / 2
        pdf.setFillColor(CREAM)
        pdf.roundRect(mx - 26, my - 4, 52, 8, 2, fill=1, stroke=0)
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 4.6)
        pdf.drawCentredString(mx, my - 1.6, edge["label"][:17])

    for index, name in enumerate(names):
        x, y = positions[name]
        hand_box(pdf, x, y, box_w, box_h, name, index + 1)
    doodle(pdf, map_data["icon"], 79, 101)
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica-Oblique", 6.2)
    pdf.drawString(112, 99, map_data["iconCaption"])
    if map_data.get("portal"):
        hand_line(pdf, 47, 75, 69, 75, map_index + 50, width=1.1)
        pdf.setFillColor(NAVY)
        pdf.setFont("Helvetica-Bold", 6.1)
        pdf.drawString(78, 73, map_data["portal"])
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 6.4)
    pdf.drawString(45, 56, "MAP KEY")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 6.1)
    pdf.drawString(94, 56, "Coral arrows show the first observed route between rooms; geometry is schematic field notes.")
    pdf.drawString(45, 44, "Unshown reverse trips and loops are omitted to keep the hand-drawn map legible.")
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
        pdf.drawString(54, height - 85, "Rows are grouped by actual instruction form and ordered by first appearance.")
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
        pdf.drawString(54, 46, "Inventory and total come from the same successful fresh replay as this guide.")
        pdf.showPage()
    return total_pages


def build(data: dict, output: Path) -> int:
    if len(data["commands"]) != data["commandsCount"]:
        raise ValueError("Command count does not match route data")
    if len(set(data["visitedLocations"])) != len(data["visitedLocations"]):
        raise ValueError("Route-discovered location list contains duplicates")
    if set(data["visitedLocations"]) != {room for sheet in data["maps"] for room in sheet["rooms"]}:
        raise ValueError("Maps do not cover every route-discovered location")
    if sum(row["Count"] for row in data["opcodeInventory"]) != data["totalInstructions"]:
        raise ValueError("Opcode counts do not sum to total instructions executed")
    output.parent.mkdir(parents=True, exist_ok=True)
    pdf = canvas.Canvas(str(output), pagesize=letter, pageCompression=1)
    pdf.setTitle("Infidel - Maximum-Score No-Death Walkthrough")
    pdf.setAuthor("LeafpileGPT project")
    pdf.setSubject("Verified Infidel 400-point no-death route, field maps, and executed opcode inventory")
    cover(pdf, data)
    page = 2
    per_page = 50
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
                        help="refresh the committed inventory from a local RouteReplay trace")
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
