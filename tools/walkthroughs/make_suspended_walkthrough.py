#!/usr/bin/env python3
"""Build the Suspended route guide from committed text and trace summary data."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.pagesizes import landscape, letter
from reportlab.pdfbase.pdfmetrics import stringWidth
from reportlab.pdfgen import canvas


ROOT = Path(__file__).resolve().parents[2]
DATA_PATH = ROOT / "walkthroughs" / "source-data" / "suspended.json"
OUTPUT_PATH = ROOT / "walkthroughs" / "suspended-maximum-score.pdf"

CREAM = colors.HexColor("#F6F2E8")
TEAL = colors.HexColor("#247F7C")
NAVY = colors.HexColor("#153F4B")
MUTED = colors.HexColor("#637B80")
PALE = colors.HexColor("#E6ECE4")
PAPER = colors.HexColor("#FFFCF5")
CORAL = colors.HexColor("#D96C55")
PENCIL = colors.HexColor("#39464A")
LIGHT_LINE = colors.HexColor("#D9D4C8")


def draw_background(pdf: canvas.Canvas, width: float, height: float) -> None:
    pdf.setFillColor(CREAM)
    pdf.rect(0, 0, width, height, stroke=0, fill=1)
    pdf.setStrokeColor(LIGHT_LINE)
    pdf.setLineWidth(0.35)
    for y in range(42, int(height), 28):
        pdf.line(0, y, width, y)


def draw_chrome(pdf: canvas.Canvas, page_label: str, page_number: int,
                size=letter) -> tuple[float, float]:
    width, height = size
    pdf.setPageSize(size)
    draw_background(pdf, width, height)
    pdf.setFillColor(TEAL)
    pdf.roundRect(34, height - 39, width - 68, 30, 10, fill=1, stroke=0)
    pdf.setFillColor(colors.white)
    pdf.setFont("Helvetica-Bold", 8)
    pdf.drawString(48, height - 27, page_label.upper())
    pdf.setStrokeColor(TEAL)
    pdf.setLineWidth(0.8)
    pdf.line(34, 27, width - 34, 27)
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica-Bold", 6.5)
    pdf.drawString(36, 15, "SUSPENDED  |  NO-DEATH ROUTE")
    pdf.drawRightString(width - 36, 15, f"FIELD GUIDE  |  {page_number:02d}")
    return width, height


def wrap_lines(text: str, font: str, size: float, max_width: float) -> list[str]:
    words = text.split()
    lines: list[str] = []
    current = ""
    for word in words:
        trial = word if not current else f"{current} {word}"
        if stringWidth(trial, font, size) <= max_width:
            current = trial
        else:
            if current:
                lines.append(current)
            current = word
    if current:
        lines.append(current)
    return lines


def paragraph(pdf: canvas.Canvas, text: str, x: float, y: float, width: float,
              font="Helvetica", size=9, leading=13, color=NAVY) -> float:
    pdf.setFillColor(color)
    pdf.setFont(font, size)
    for line in wrap_lines(text, font, size, width):
        pdf.drawString(x, y, line)
        y -= leading
    return y


def pill(pdf: canvas.Canvas, x: float, y: float, width: float,
         heading: str, detail: str, accent=TEAL) -> None:
    pdf.setFillColor(PAPER)
    pdf.roundRect(x, y - 44, width, 42, 8, fill=1, stroke=0)
    pdf.setFillColor(accent)
    pdf.setFont("Helvetica-Bold", 9)
    pdf.drawString(x + 10, y - 17, heading)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica", 7.4)
    pdf.drawString(x + 10, y - 32, detail)


def cover_page(pdf: canvas.Canvas, data: dict, page: int) -> None:
    width, height = draw_chrome(pdf, "A FIELD GUIDE TO THE UNDERGROUND COMPLEX", page)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 29)
    pdf.drawString(52, height - 100, "SUSPENDED")
    pdf.setFillColor(CORAL)
    pdf.setFont("Helvetica-Bold", 16)
    pdf.drawString(54, height - 127, "No-death winning walkthrough")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 9.5)
    pdf.drawString(54, height - 149,
                   "A deterministic route through the repairs and the final reset")

    pdf.setFillColor(PAPER)
    pdf.roundRect(48, height - 255, width - 96, 84, 12, fill=1, stroke=0)
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 8)
    pdf.drawString(64, height - 190, "VERIFIED FINISH")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 23)
    pdf.drawString(64, height - 222, "WIN")
    pdf.setFont("Helvetica-Bold", 9)
    pdf.drawRightString(width - 64, height - 193,
                        "114,000 current casualties  |  rank 3  |  194 cycles")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 8)
    pdf.drawRightString(width - 64, height - 215,
                        "127 commands  |  214,086 instructions  |  zero robot deaths")
    pdf.drawRightString(width - 64, height - 234,
                        "54 unique form/opcode/mnemonic rows")

    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 12)
    pdf.drawString(54, height - 287, "How to play this route")
    y = height - 308
    for text in data["intro"]:
        y = paragraph(pdf, text, 56, y, width - 112, size=8.7, leading=12)
        y -= 6

    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 12)
    pdf.drawString(54, y - 2, "Verified checkpoints")
    card_y = y - 19
    gap = 10
    card_width = (width - 108 - gap * 2) / 3
    pill(pdf, 54, card_y, card_width, "8,000 casualties", "25 cycles after surface controls")
    pill(pdf, 54 + card_width + gap, card_y, card_width, "114,000 casualties", "rank 3 at the winning ending", CORAL)
    pill(pdf, 54 + 2 * (card_width + gap), card_y, card_width, "29,888,000 survivors", "170,000 projected during recovery")

    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 7.3)
    pdf.drawString(54, 75,
                   "Replay settings: fresh Release 8 story  |  deterministic seed 1035  |  local route and trace")
    pdf.drawString(54, 62,
                   "The route keeps Poet out of the acid-filled Cavernous Room until Auda reports the hiss has stopped.")
    pdf.showPage()


def command_page(pdf: canvas.Canvas, commands: list[str], start: int,
                 end: int, data: dict, page: int, page_title: str) -> None:
    width, height = draw_chrome(pdf, page_title, page)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 17)
    pdf.drawString(52, height - 73, page_title)
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 8)
    pdf.drawString(54, height - 89,
                   f"Commands {start:03d}-{end:03d}; read down the left column, then down the right.")

    selected = list(enumerate(commands[start - 1:end], start=start))
    split = (len(selected) + 1) // 2
    columns = [selected[:split], selected[split:]]
    lefts = [54, 316]
    row_y = height - 119
    row_step = 20
    for col_index, items in enumerate(columns):
        x = lefts[col_index]
        for number, command in items:
            if number in data["stepStarts"]:
                label = data["stepStarts"][str(number)]
                pdf.setFillColor(TEAL)
                pdf.setFont("Helvetica-Bold", 7)
                pdf.drawString(x, row_y + 6, label.upper())
                row_y -= 5
            pdf.setFillColor(CORAL if number in data["criticalCommands"] else TEAL)
            pdf.setFont("Courier-Bold", 7.2)
            pdf.drawRightString(x + 24, row_y, f"{number:03d}")
            pdf.setFillColor(NAVY)
            pdf.setFont("Courier", 7.15)
            max_width = 220
            lines = wrap_lines(command, "Courier", 7.15, max_width)
            for line_index, line in enumerate(lines):
                pdf.drawString(x + 31, row_y - line_index * 8.7, line)
            row_y -= max(row_step, 8.7 * len(lines) + 2)
        row_y = height - 119
    pdf.showPage()


def hand_line(pdf: canvas.Canvas, x1: float, y1: float, x2: float, y2: float,
              seed: int, color=PENCIL, width=1.1, arrow=False) -> None:
    # Deterministic two-stroke line with a small hand-drawn wobble.
    dx, dy = x2 - x1, y2 - y1
    length = max(1.0, (dx * dx + dy * dy) ** 0.5)
    px, py = -dy / length, dx / length
    wobble = ((seed * 37) % 7 - 3) * 0.45
    pdf.setStrokeColor(color)
    pdf.setLineWidth(width)
    pdf.line(x1, y1, (x1 + x2) / 2 + px * wobble, (y1 + y2) / 2 + py * wobble)
    pdf.line((x1 + x2) / 2 - px * wobble * 0.35,
             (y1 + y2) / 2 - py * wobble * 0.35, x2, y2)
    if arrow:
        angle = __import__("math").atan2(dy, dx)
        size = 5
        left = (x2 - size * __import__("math").cos(angle - 0.45),
                y2 - size * __import__("math").sin(angle - 0.45))
        right = (x2 - size * __import__("math").cos(angle + 0.45),
                 y2 - size * __import__("math").sin(angle + 0.45))
        pdf.line(x2, y2, left[0], left[1])
        pdf.line(x2, y2, right[0], right[1])


def hand_box(pdf: canvas.Canvas, x: float, y: float, width: float,
             height: float, label: str, visited: bool, seed: int) -> None:
    fill = PALE if visited else PAPER
    pdf.setFillColor(fill)
    pdf.setStrokeColor(PENCIL)
    pdf.setLineWidth(1.0)
    path = pdf.beginPath()
    offsets = [((seed * 7) % 3), ((seed * 11) % 3), ((seed * 13) % 3), ((seed * 17) % 3)]
    path.moveTo(x + 3, y + 1 + offsets[0])
    path.lineTo(x + width - 1, y + 2 + offsets[1])
    path.lineTo(x + width - 2, y + height - 2 - offsets[2])
    path.lineTo(x + 1 + offsets[3], y + height - 1)
    path.close()
    pdf.drawPath(path, stroke=1, fill=1)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-BoldOblique", 7.2)
    lines = wrap_lines(label, "Helvetica-BoldOblique", 7.2, width - 12)
    top = y + height / 2 + (len(lines) - 1) * 4
    for index, line in enumerate(lines):
        pdf.drawCentredString(x + width / 2, top - index * 8, line)


def sketch_robot(pdf: canvas.Canvas, x: float, y: float) -> None:
    pdf.setStrokeColor(TEAL)
    pdf.setLineWidth(1.4)
    pdf.circle(x, y, 11, stroke=1, fill=0)
    pdf.line(x - 6, y + 2, x + 6, y + 2)
    pdf.circle(x - 4, y + 3, 1.2, stroke=1, fill=0)
    pdf.circle(x + 4, y + 3, 1.2, stroke=1, fill=0)
    pdf.line(x - 5, y - 6, x + 5, y - 6)
    pdf.line(x - 15, y - 17, x + 15, y - 17)
    pdf.line(x - 8, y - 11, x - 8, y - 17)
    pdf.line(x + 8, y - 11, x + 8, y - 17)


def sketch_cable(pdf: canvas.Canvas, x: float, y: float) -> None:
    pdf.setStrokeColor(CORAL)
    pdf.setLineWidth(1.2)
    p = pdf.beginPath()
    p.moveTo(x - 15, y)
    p.curveTo(x - 8, y + 16, x + 4, y - 16, x + 14, y)
    pdf.drawPath(p, stroke=1, fill=0)
    pdf.circle(x - 15, y, 2, stroke=1, fill=0)
    pdf.circle(x + 14, y, 2, stroke=1, fill=0)


def map_page(pdf: canvas.Canvas, data: dict, map_data: dict, page_number: int) -> None:
    size = landscape(letter)
    width, height = draw_chrome(pdf, f"FIELD MAP  |  {map_data['title']}", page_number, size)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 16)
    pdf.drawString(48, height - 69, map_data["title"])
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 7.8)
    pdf.drawString(50, height - 84,
                   "Hand-sketched schematic of all 63 named room objects, not to scale. Green rooms were reached on this route.")

    # Three loose corridors, arranged to echo a player's quick pencil map.
    box_w, box_h = 214, 28
    centers_x = [150, 396, 642]
    y_top, gap_y = height - 121, 37
    positions: dict[str, tuple[float, float]] = {}
    visited = set(data["visitedLocations"])
    names = map_data["rooms"]
    rows_per_column = (len(names) + len(centers_x) - 1) // len(centers_x)
    columns = [names[index * rows_per_column:(index + 1) * rows_per_column]
               for index in range(len(centers_x))]
    for col_index, column in enumerate(columns):
        x = centers_x[col_index] - box_w / 2
        for row_index, name in enumerate(column):
            y = y_top - row_index * gap_y
            positions[name] = (x, y)

    for edge_index, edge in enumerate(map_data["edges"]):
        source, target, route_edge = edge
        if source not in positions or target not in positions:
            continue
        sx, sy = positions[source]
        tx, ty = positions[target]
        color = CORAL if route_edge else MUTED
        line_width = 1.55 if route_edge else 0.85
        source_column = round((sx + box_w / 2 - centers_x[0]) / (centers_x[1] - centers_x[0]))
        target_column = round((tx + box_w / 2 - centers_x[0]) / (centers_x[1] - centers_x[0]))
        source_y, target_y = sy + box_h / 2, ty + box_h / 2
        if source_column == target_column:
            if abs(source_y - target_y) <= gap_y + 2:
                if target_y < source_y:
                    start, finish = (sx + box_w / 2, sy), (tx + box_w / 2, ty + box_h)
                else:
                    start, finish = (sx + box_w / 2, sy + box_h), (tx + box_w / 2, ty)
                hand_line(pdf, *start, *finish, edge_index + page_number,
                          color=color, width=line_width, arrow=True)
            else:
                side = sx + box_w + 6 if source_column < 2 else sx - 6
                source_edge = sx + box_w if source_column < 2 else sx
                target_edge = tx + box_w if source_column < 2 else tx
                hand_line(pdf, source_edge, source_y, side, source_y,
                          edge_index + page_number, color=color, width=line_width)
                hand_line(pdf, side, source_y, side, target_y,
                          edge_index + page_number + 1, color=color, width=line_width)
                hand_line(pdf, side, target_y, target_edge, target_y,
                          edge_index + page_number + 2, color=color, width=line_width, arrow=True)
        else:
            rightward = target_column > source_column
            source_edge = sx + box_w if rightward else sx
            target_edge = tx if rightward else tx + box_w
            gutter = (source_edge + target_edge) / 2
            hand_line(pdf, source_edge, source_y, gutter, source_y,
                      edge_index + page_number, color=color, width=line_width)
            hand_line(pdf, gutter, source_y, gutter, target_y,
                      edge_index + page_number + 1, color=color, width=line_width)
            hand_line(pdf, gutter, target_y, target_edge, target_y,
                      edge_index + page_number + 2, color=color, width=line_width, arrow=True)

    for index, name in enumerate(names):
        x, y = positions[name]
        hand_box(pdf, x, y, box_w, box_h, name, name in visited, index + page_number)

    if page_number == 5:
        sketch_robot(pdf, 54, 97)
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica-Oblique", 6.5)
        pdf.drawString(76, 78, "one crew, six very different sensors")
    else:
        sketch_cable(pdf, width - 24, 101)
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica-Oblique", 6.5)
        pdf.drawRightString(width - 36, 78, "the repaired FC cable run")

    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 6.8)
    pdf.drawString(50, 58, "ROUTE KEY")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 6.8)
    pdf.drawString(105, 58,
                   "Coral arrows trace verified travel on this run; pencil arrows show the connected corridor network.")
    pdf.showPage()


def inventory_pages(pdf: canvas.Canvas, data: dict, start_page: int) -> None:
    rows = data["opcodeInventory"]
    chunk_size = 20
    for page_index, start in enumerate(range(0, len(rows), chunk_size)):
        page_number = start_page + page_index
        width, height = draw_chrome(pdf, "EXECUTED OPCODE INVENTORY", page_number)
        pdf.setFillColor(NAVY)
        pdf.setFont("Helvetica-Bold", 17)
        pdf.drawString(52, height - 73, f"Opcode table {page_index + 1} of {(len(rows) + chunk_size - 1) // chunk_size}")
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 8)
        pdf.drawString(54, height - 90,
                       "Form, opcode, and mnemonic are grouped as executed; rows follow first appearance.")
        chunk = rows[start:start + chunk_size]
        pdf.setFillColor(PAPER)
        pdf.roundRect(50, height - 143, width - 100, 35, 7, fill=1, stroke=0)
        pdf.setFillColor(TEAL)
        pdf.setFont("Helvetica-Bold", 8)
        pdf.drawString(62, height - 123, f"{len(rows)} unique form/opcode/mnemonic rows")
        pdf.drawRightString(width - 62, height - 123,
                            f"{data['totalInstructions']:,} total instructions executed")
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 7)
        pdf.drawString(54, height - 157,
                       "First Seq is one-based execution order; First PC is the first instruction address in hexadecimal.")

        x0, y0 = 50, height - 187
        widths = [69, 54, 105, 90, 92, 102]
        headers = ["FORM", "OPCODE", "MNEMONIC", "COUNT", "FIRST SEQ", "FIRST PC"]
        xs = [x0]
        for col_width in widths:
            xs.append(xs[-1] + col_width)
        pdf.setFillColor(TEAL)
        pdf.roundRect(x0, y0 - 1, sum(widths), 24, 4, fill=1, stroke=0)
        for col, label in enumerate(headers):
            pdf.setFillColor(colors.white)
            pdf.setFont("Helvetica-Bold", 6.8)
            pdf.drawString(xs[col] + 6, y0 + 7, label)
        row_y = y0 - 21
        for row_index, row in enumerate(chunk):
            pdf.setFillColor(PALE if row_index % 2 == 0 else PAPER)
            pdf.rect(x0, row_y - 1, sum(widths), 21, fill=1, stroke=0)
            values = [row["Form"], str(row["Opcode"]), row["Mnemonic"],
                      f"{row['Count']:,}", f"{row['FirstSequence']:,}",
                      f"0x{row['FirstAddress']:04X}"]
            for col, value in enumerate(values):
                pdf.setFillColor(NAVY)
                pdf.setFont("Courier", 7)
                if col in (1, 3, 4):
                    pdf.drawRightString(xs[col + 1] - 6, row_y + 6, value)
                else:
                    pdf.drawString(xs[col] + 6, row_y + 6, value)
            row_y -= 21
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica-Oblique", 7)
        pdf.drawString(54, 52,
                       "Trace summary is from the same fresh replay used to verify the route.")
        pdf.showPage()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--refresh-trace", type=Path,
                        help="replace committed opcode summary from a local RouteReplay JSONL trace")
    args = parser.parse_args()
    data = json.loads(DATA_PATH.read_text())
    if args.refresh_trace:
        records = [json.loads(line) for line in args.refresh_trace.read_text().splitlines()]
        summary = next((record for record in reversed(records) if "opcodeInventory" in record), None)
        if summary is None:
            raise SystemExit("Trace has no final opcode inventory summary")
        rows = summary["opcodeInventory"]
        if sum(row["Count"] for row in rows) != summary["instructionsExecuted"]:
            raise SystemExit("Opcode row counts do not sum to the instruction total")
        data["opcodeInventory"] = rows
        data["totalInstructions"] = summary["instructionsExecuted"]
        DATA_PATH.write_text(json.dumps(data, indent=2) + "\n")
    if sum(row["Count"] for row in data["opcodeInventory"]) != data["totalInstructions"]:
        raise SystemExit("Opcode row counts do not sum to the instruction total")
    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    pdf = canvas.Canvas(str(OUTPUT_PATH), pagesize=letter,
                        title="Suspended - No-death Winning Walkthrough",
                        author="LeafpileGPT project",
                        subject="Verified safe route, hand-drawn map, and replay opcode inventory")
    page = 1
    cover_page(pdf, data, page)
    page += 1
    commands = data["commands"]
    ranges = [(1, 44, "SURFACE CONTROLS AND FIRST REPAIRS"),
              (45, 88, "ACCESS, CUTTER, AND ACID VALVE"),
              (89, len(commands), "CABLES, CAMERA, AND RESET CODE")]
    for start, end, title in ranges:
        command_page(pdf, commands, start, end, data, page, title)
        page += 1
    for map_data in data["maps"]:
        map_page(pdf, data, map_data, page)
        page += 1
    inventory_pages(pdf, data, page)
    pdf.save()
    print(f"Wrote {OUTPUT_PATH.relative_to(ROOT)} ({page + (len(data['opcodeInventory']) + 19) // 20 - 1} pages)")


if __name__ == "__main__":
    main()
