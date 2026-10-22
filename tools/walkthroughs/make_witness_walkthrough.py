#!/usr/bin/env python3
"""Build the Witness case guide and trace-derived opcode inventory."""

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
DATA_PATH = ROOT / "walkthroughs" / "source-data" / "witness.json"
OUTPUT_PATH = ROOT / "walkthroughs" / "witness-maximum-score.pdf"

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
        entry = json.loads(line)
        if "opcodeInventory" in entry:
            summary = entry
    if summary is None:
        raise ValueError(f"Trace has no opcode inventory summary: {trace_path}")
    rows = summary["opcodeInventory"]
    total = summary["instructionsExecuted"]
    if sum(row["Count"] for row in rows) != total:
        raise ValueError("Opcode counts do not sum to total instructions executed")
    data["totalInstructions"] = total
    data["opcodeInventory"] = rows
    DATA_PATH.write_text(json.dumps(data, indent=2) + "\n")


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
           footer="THE WITNESS  |  CONVICTION ROUTE") -> tuple[float, float]:
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
    pdf.roundRect(x, y - 49, width, 46, 8, fill=1, stroke=0)
    pdf.setFillColor(accent)
    pdf.setFont("Helvetica-Bold", 8.4)
    pdf.drawString(x + 9, y - 17, heading)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica", 6.2)
    for index, line in enumerate(wrap(detail, "Helvetica", 6.2, width - 18)[:2]):
        pdf.drawString(x + 9, y - 31 - index * 8, line)


def cover(pdf: canvas.Canvas, data: dict) -> None:
    width, height = chrome(pdf, "A field guide to a California murder", 1)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 29)
    pdf.drawString(52, height - 100, "THE WITNESS")
    pdf.setFillColor(CORAL)
    pdf.setFont("Helvetica-Bold", 16)
    pdf.drawString(54, height - 127, "Best-ending case walkthrough")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 9.5)
    pdf.drawString(54, height - 149,
                   "A deterministic, command-by-command route to Monica Linder's conviction")

    pdf.setFillColor(PAPER)
    pdf.roundRect(48, height - 255, width - 96, 84, 12, fill=1, stroke=0)
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 8)
    pdf.drawString(64, height - 190, "VERIFIED FINISH")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 20)
    pdf.drawString(64, height - 222, "GUILTY VERDICT")
    pdf.setFont("Helvetica-Bold", 8.5)
    pdf.drawRightString(width - 64, height - 193,
                        f"{data['commandsCount']} commands  |  {data['moves']} moves  |  zero deaths")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 7.5)
    pdf.drawRightString(width - 64, height - 215,
                        f"{data['totalInstructions']:,} executed instructions  |  {len(data['opcodeInventory'])} unique opcode rows")
    pdf.drawRightString(width - 64, height - 234,
                        "Fresh Release 22 story  |  conviction confirmed by the ending letter")

    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 12)
    pdf.drawString(54, height - 287, "How to play this route")
    y = height - 308
    for text in data["intro"]:
        y = paragraph(pdf, text, 56, y, width - 112, size=8.1, leading=10)
        y -= 5

    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 12)
    pdf.drawString(54, y - 2, "Evidence checkpoints")
    card_y = y - 19
    gap = 8
    card_width = (width - 108 - gap * 3) / 4
    for i, item in enumerate(data["checkpoints"]):
        pill(pdf, 54 + i * (card_width + gap), card_y, card_width,
             item["heading"], item["detail"], CORAL if i == 3 else TEAL)

    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 7.1)
    pdf.drawString(54, 72,
                   "Witness has no numeric score global in this replay; completion is measured by the conviction ending.")
    pdf.drawString(54, 59,
                   "Replay settings: deterministic seed 1035  |  route and trace remain local  |  58-row opcode inventory")
    pdf.showPage()


def command_page(pdf: canvas.Canvas, data: dict, start: int, end: int, page: int) -> None:
    width, height = chrome(pdf, "Numbered route", page)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 17)
    pdf.drawString(52, height - 73, "The investigation")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 8)
    pdf.drawString(54, height - 89,
                   f"Commands {start:03d}-{end:03d}; read down the left column, then down the right.")

    commands = data["commands"][start - 1:end]
    numbered = list(enumerate(commands, start=start))
    split = (len(numbered) + 1) // 2
    columns = [numbered[:split], numbered[split:]]
    x_positions = [54, 316]
    for column_index, items in enumerate(columns):
        y = height - 119
        x = x_positions[column_index]
        for number, command in items:
            if str(number) in data["stepStarts"]:
                pdf.setFillColor(TEAL)
                pdf.setFont("Helvetica-Bold", 6.5)
                pdf.drawString(x, y + 5, data["stepStarts"][str(number)].upper())
                y -= 6
            pdf.setFillColor(CORAL if number in data["criticalCommands"] else TEAL)
            pdf.setFont("Courier-Bold", 7)
            pdf.drawRightString(x + 23, y, f"{number:03d}")
            pdf.setFillColor(NAVY)
            pdf.setFont("Courier", 7)
            lines = wrap(command, "Courier", 7, 220)
            for line_index, line in enumerate(lines):
                pdf.drawString(x + 30, y - line_index * 8.4, line)
            y -= max(17, 8.4 * len(lines) + 2)
    pdf.showPage()


def hand_line(pdf: canvas.Canvas, x1: float, y1: float, x2: float, y2: float,
              seed: int, arrow=True, color=PENCIL, width=1.15) -> None:
    dx, dy = x2 - x1, y2 - y1
    length = max(1, math.hypot(dx, dy))
    px, py = -dy / length, dx / length
    wobble = ((seed * 37) % 7 - 3) * 0.55
    pdf.setStrokeColor(color)
    pdf.setLineWidth(width)
    pdf.line(x1, y1, (x1 + x2) / 2 + px * wobble, (y1 + y2) / 2 + py * wobble)
    pdf.line((x1 + x2) / 2 - px * wobble * 0.3,
             (y1 + y2) / 2 - py * wobble * 0.3, x2, y2)
    if arrow:
        angle = math.atan2(dy, dx)
        for delta in (-0.48, 0.48):
            ax = x2 - 6 * math.cos(angle + delta)
            ay = y2 - 6 * math.sin(angle + delta)
            pdf.line(x2, y2, ax, ay)


def hand_box(pdf: canvas.Canvas, x: float, y: float, w: float, h: float,
             label: str, object_ids: str = "", seed: int = 1,
             accent=TEAL) -> None:
    pdf.setFillColor(PALE if seed % 3 else PAPER)
    pdf.setStrokeColor(PENCIL)
    pdf.setLineWidth(1.0)
    p = pdf.beginPath()
    p.moveTo(x + 2, y + (seed % 3))
    p.lineTo(x + w - 1, y + 2 + ((seed + 1) % 3))
    p.lineTo(x + w - 2 - ((seed + 2) % 3), y + h - 1)
    p.lineTo(x + 1 + ((seed + 3) % 3), y + h - 2)
    p.close()
    pdf.drawPath(p, fill=1, stroke=1)
    pdf.setFillColor(accent)
    pdf.circle(x + 9, y + h - 9, 2, fill=1, stroke=0)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-BoldOblique", 7.3)
    for index, line in enumerate(wrap(label, "Helvetica-BoldOblique", 7.3, w - 14)[:2]):
        pdf.drawCentredString(x + w / 2, y + h / 2 + 3 - index * 8, line)
    if object_ids:
        pdf.setFillColor(MUTED)
        pdf.setFont("Courier", 5.2)
        pdf.drawString(x + 6, y + 6, f"ID {object_ids}")


def doodle(pdf: canvas.Canvas, kind: str, x: float, y: float, scale=1.0) -> None:
    pdf.saveState()
    pdf.setStrokeColor(CORAL)
    pdf.setFillColor(CORAL)
    pdf.setLineWidth(1.2)
    if kind == "house":
        pdf.line(x - 12 * scale, y, x, y + 11 * scale)
        pdf.line(x, y + 11 * scale, x + 12 * scale, y)
        pdf.rect(x - 9 * scale, y - 11 * scale, 18 * scale, 11 * scale, fill=0)
        pdf.rect(x - 2 * scale, y - 11 * scale, 5 * scale, 7 * scale, fill=0)
    elif kind == "clock":
        pdf.circle(x, y, 9 * scale, fill=0)
        pdf.line(x, y, x, y + 5 * scale)
        pdf.line(x, y, x + 5 * scale, y - 2 * scale)
    elif kind == "magnifier":
        pdf.circle(x - 2 * scale, y + 2 * scale, 7 * scale, fill=0)
        pdf.line(x + 3 * scale, y - 3 * scale, x + 10 * scale, y - 10 * scale)
    elif kind == "footprints":
        for i in range(3):
            pdf.ellipse(x + (i % 2) * 5 * scale, y + i * 6 * scale,
                        x + (i % 2) * 5 * scale + 3 * scale,
                        y + i * 6 * scale + 7 * scale, fill=0)
    elif kind == "wire":
        p = pdf.beginPath()
        p.moveTo(x - 13 * scale, y)
        p.curveTo(x - 6 * scale, y + 12 * scale, x + 6 * scale, y - 12 * scale, x + 13 * scale, y)
        pdf.drawPath(p, stroke=1, fill=0)
        pdf.circle(x - 13 * scale, y, 1.5 * scale, fill=1, stroke=0)
        pdf.circle(x + 13 * scale, y, 1.5 * scale, fill=1, stroke=0)
    elif kind == "book":
        pdf.rect(x - 9 * scale, y - 8 * scale, 18 * scale, 16 * scale, fill=0)
        pdf.line(x, y - 8 * scale, x, y + 8 * scale)
    elif kind == "car":
        pdf.roundRect(x - 14 * scale, y - 5 * scale, 28 * scale, 10 * scale, 3, fill=0)
        pdf.circle(x - 8 * scale, y - 7 * scale, 3 * scale, fill=0)
        pdf.circle(x + 8 * scale, y - 7 * scale, 3 * scale, fill=0)
    elif kind == "tree":
        pdf.circle(x, y + 5 * scale, 8 * scale, fill=0)
        pdf.line(x, y - 4 * scale, x, y - 13 * scale)
        pdf.line(x - 5 * scale, y - 13 * scale, x + 5 * scale, y - 13 * scale)
    elif kind == "handcuffs":
        pdf.circle(x - 5 * scale, y, 5 * scale, fill=0)
        pdf.circle(x + 5 * scale, y, 5 * scale, fill=0)
        pdf.line(x, y, x + 1 * scale, y)
    pdf.restoreState()


def map_page_one(pdf: canvas.Canvas, page: int) -> None:
    width, height = chrome(pdf, "Hand-sketched room map", page, landscape(letter), "THE WITNESS  |  CASE MAP")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 17)
    pdf.drawString(48, height - 72, "Approach, public rooms, and service wing")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 7.5)
    pdf.drawString(50, height - 88, "Room IDs are story object numbers. The repeated hall labels form one connected corridor.")

    boxes = {
        "Driveway Entrance": (48, 386, 100, 54, "224"),
        "Driveway": (190, 386, 92, 54, "225"),
        "Front Porch": (328, 386, 92, 54, "228"),
        "Front Yard + Kitchen Garden": (470, 386, 132, 54, "237 / 229"),
        "Dining Room": (654, 386, 96, 54, "158"),
        "Entry": (48, 270, 90, 54, "130"),
        "Hallway - South": (180, 270, 112, 54, "122 / 138"),
        "Hallway - Mid / North": (340, 270, 120, 54, "139 / 132"),
        "Living Room": (510, 270, 104, 54, "168"),
        "Kitchen": (654, 270, 96, 54, "153"),
        "Garage": (180, 153, 112, 54, "102"),
        "Workshop": (328, 153, 92, 54, "96"),
        "Storage Closet": (470, 153, 132, 54, "126"),
        "Butler's Room": (654, 153, 96, 54, "135"),
    }
    centers = {name: (x + w / 2, y + h / 2) for name, (x, y, w, h, _) in boxes.items()}
    edges = [
        ("Driveway Entrance", "Driveway"), ("Driveway", "Front Porch"),
        ("Front Porch", "Front Yard + Kitchen Garden"), ("Front Yard + Kitchen Garden", "Dining Room"),
        ("Entry", "Hallway - South"), ("Hallway - South", "Hallway - Mid / North"),
        ("Hallway - Mid / North", "Living Room"), ("Dining Room", "Living Room"),
        ("Dining Room", "Kitchen"), ("Kitchen", "Hallway - South"),
        ("Hallway - South", "Garage"), ("Garage", "Workshop"),
        ("Hallway - South", "Storage Closet"), ("Hallway - Mid / North", "Butler's Room"),
    ]
    for index, (a, b) in enumerate(edges, 1):
        ax, ay = centers[a]; bx, by = centers[b]
        if abs(ax - bx) < 15:
            hand_line(pdf, ax, ay + (27 if by > ay else -27), bx, by + (-27 if by > ay else 27), index)
        elif abs(ay - by) < 12:
            hand_line(pdf, ax + (50 if bx > ax else -50), ay, bx + (-50 if bx > ax else 50), by, index)
        else:
            # Route connectors around box edges so their labels stay readable.
            sx = ax + (50 if bx > ax else -50)
            ex = bx + (-50 if bx > ax else 50)
            hand_line(pdf, sx, ay, ex, by, index)
    # The driveway bends east to the garage; sketch this lane around the hall.
    driveway_x, driveway_y = centers["Driveway"]
    garage_x, garage_y = centers["Garage"]
    hand_line(pdf, driveway_x + 46, driveway_y, 310, driveway_y - 16, 18, arrow=False)
    hand_line(pdf, 310, driveway_y - 16, 310, garage_y, 19, arrow=False)
    hand_line(pdf, 310, garage_y, garage_x + 46, garage_y, 20)
    for index, (name, (x, y, w, h, ids)) in enumerate(boxes.items(), 1):
        hand_box(pdf, x, y, w, h, name, ids, index)
    doodle(pdf, "car", 169, 410, .75)
    doodle(pdf, "house", 424, 453, .8)
    doodle(pdf, "book", 754, 179, .7)
    doodle(pdf, "tree", 619, 412, .8)
    doodle(pdf, "clock", 492, 281, .8)
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 6.7)
    pdf.drawString(50, 88, "The kitchen garden is a planted section of the front yard. The hallway is split into south, middle, and north stretches.")
    pdf.drawString(50, 75, "The route enters the office from the south hallway and reaches the workshop through the garage.")
    pdf.showPage()


def map_page_two(pdf: canvas.Canvas, page: int) -> None:
    width, height = chrome(pdf, "Hand-sketched room map", page, landscape(letter), "THE WITNESS  |  CASE MAP")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 17)
    pdf.drawString(48, height - 72, "Private rooms, crime scene, and back grounds")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 7.5)
    pdf.drawString(50, height - 88, "Boxes mark every physical room in the story; dotted branches mark garden edges and woods that cannot be entered safely.")

    boxes = {
        "Butler's Room": (48, 382, 104, 52, "135"),
        "Butler's Bathroom": (48, 274, 104, 52, "133"),
        "Hallway - Mid": (210, 382, 108, 52, "139"),
        "Monica's Bedroom": (374, 382, 112, 52, "197"),
        "Bathroom": (374, 274, 112, 52, "185"),
        "Toilet Room": (218, 274, 92, 52, "176"),
        "Linder's Bedroom": (538, 382, 120, 52, "174"),
        "Tub Room": (538, 274, 100, 52, "180"),
        "Rock Garden": (674, 382, 92, 52, "203"),
        "Back Yard": (674, 274, 92, 52, "205"),
        "Office": (374, 158, 112, 52, "207"),
        "Hallway - South": (210, 158, 108, 52, "122 / 138"),
        "Office Porch": (538, 158, 100, 52, "214"),
        "Office Path": (674, 158, 92, 52, "218"),
        "Side Yard": (538, 67, 100, 52, "219"),
    }
    centers = {name: (x + w / 2, y + h / 2) for name, (x, y, w, h, _) in boxes.items()}
    edges = [
        ("Hallway - Mid", "Butler's Room"), ("Butler's Room", "Butler's Bathroom"),
        ("Hallway - Mid", "Monica's Bedroom"), ("Monica's Bedroom", "Bathroom"),
        ("Bathroom", "Toilet Room"), ("Bathroom", "Linder's Bedroom"),
        ("Bathroom", "Tub Room"), ("Linder's Bedroom", "Rock Garden"),
        ("Rock Garden", "Back Yard"), ("Monica's Bedroom", "Back Yard"),
        ("Back Yard", "Office Porch"), ("Office Porch", "Office Path"),
        ("Office Path", "Side Yard"),
        ("Hallway - South", "Office"),
        ("Office", "Office Porch"),
    ]
    for index, (a, b) in enumerate(edges, 20):
        ax, ay = centers[a]; bx, by = centers[b]
        if abs(ax - bx) < 20:
            hand_line(pdf, ax, ay + (26 if by > ay else -26), bx, by + (-26 if by > ay else 26), index)
        elif abs(ay - by) < 12:
            hand_line(pdf, ax + (52 if bx > ax else -52), ay, bx + (-52 if bx > ax else 52), by, index)
        else:
            sx = ax + (52 if bx > ax else -52)
            ex = bx + (-52 if bx > ax else 52)
            hand_line(pdf, sx, ay, ex, by, index)
    mid_x, mid_y = centers["Hallway - Mid"]
    south_x, south_y = centers["Hallway - South"]
    # The south corridor jogs around the toilet-room branch on this sketch.
    hand_line(pdf, mid_x, mid_y - 26, 330, mid_y - 40, 54, arrow=False)
    hand_line(pdf, 330, mid_y - 40, 330, south_y + 32, 55, arrow=False)
    hand_line(pdf, 330, south_y + 32, south_x, south_y + 26, 56)
    for index, (name, (x, y, w, h, ids)) in enumerate(boxes.items(), 25):
        hand_box(pdf, x, y, w, h, name, ids, index)
    doodle(pdf, "book", 164, 300, .7)
    doodle(pdf, "clock", 518, 184, .75)
    doodle(pdf, "wire", 646, 286, .8)
    doodle(pdf, "footprints", 657, 113, .8)
    doodle(pdf, "magnifier", 510, 404, .8)
    doodle(pdf, "handcuffs", 492, 184, .8)
    doodle(pdf, "tree", 754, 320, .75)
    pdf.setStrokeColor(CORAL)
    pdf.setDash(3, 3)
    hand_line(pdf, 766, 300, 782, 300, 60, arrow=False, color=CORAL, width=.8)
    pdf.setDash()
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 6.5)
    pdf.drawString(671, 246, "Bamboo woods (dead end)")
    pdf.drawString(50, 53, "Clue marks: clock = hidden firing rig; wire = ordinary bell wire; footprints = escape trail; magnifier = evidence checkpoint.")
    pdf.showPage()


def inventory_pages(pdf: canvas.Canvas, data: dict, start_page: int) -> None:
    rows = data["opcodeInventory"]
    per_page = 20
    total_pages = math.ceil(len(rows) / per_page)
    if sum(row["Count"] for row in rows) != data["totalInstructions"]:
        raise ValueError("Opcode counts do not sum to total instructions executed")
    for page_index in range(total_pages):
        page = start_page + page_index
        width, height = chrome(pdf, "Executed opcode inventory", page)
        chunk = rows[page_index * per_page:(page_index + 1) * per_page]
        pdf.setFillColor(NAVY)
        pdf.setFont("Helvetica-Bold", 17)
        pdf.drawString(52, height - 73, "Executed opcodes")
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 8)
        pdf.drawString(54, height - 89,
                       f"Table {page_index + 1} of {total_pages}; rows are ordered by first appearance in the successful fresh replay.")

        pdf.setFillColor(PAPER)
        pdf.roundRect(50, height - 143, width - 100, 37, 8, fill=1, stroke=0)
        pdf.setFillColor(TEAL)
        pdf.setFont("Helvetica-Bold", 8)
        pdf.drawString(62, height - 121, f"{len(rows)} unique form/opcode/mnemonic rows")
        pdf.setFillColor(NAVY)
        pdf.drawRightString(width - 62, height - 121,
                            f"{data['totalInstructions']:,} total instructions executed")
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 7)
        pdf.drawString(54, height - 157,
                       "First Seq is one-based execution order; First PC is the instruction address in hexadecimal.")

        x0, y0 = 50, height - 182
        widths = [72, 48, 105, 79, 94, 114]
        headers = ["FORM", "OPCODE", "MNEMONIC", "COUNT", "FIRST SEQ", "FIRST PC"]
        row_height = 25
        pdf.setFillColor(TEAL)
        pdf.roundRect(x0, y0 - row_height, sum(widths), row_height, 5, fill=1, stroke=0)
        xpos = x0
        for index, label in enumerate(headers):
            pdf.setFillColor(colors.white)
            pdf.setFont("Helvetica-Bold", 7)
            if index in (1, 3, 4):
                pdf.drawRightString(xpos + widths[index] - 7, y0 - 16, label)
            else:
                pdf.drawString(xpos + 7, y0 - 16, label)
            xpos += widths[index]
        for row_index, row in enumerate(chunk):
            y = y0 - row_height * (row_index + 2)
            pdf.setFillColor(PALE if row_index % 2 == 0 else PAPER)
            pdf.rect(x0, y, sum(widths), row_height, fill=1, stroke=0)
            values = [row["Form"], str(row["Opcode"]), row["Mnemonic"],
                      f"{row['Count']:,}", f"{row['FirstSequence']:,}",
                      f"0x{row['FirstAddress']:04X}"]
            xpos = x0
            for index, value in enumerate(values):
                pdf.setFillColor(NAVY)
                pdf.setFont("Courier", 7.4)
                if index in (1, 3, 4):
                    pdf.drawRightString(xpos + widths[index] - 7, y + 9, value)
                else:
                    pdf.drawString(xpos + 7, y + 9, value)
                xpos += widths[index]
        pdf.showPage()


def build(data: dict, output: Path) -> None:
    if sum(row["Count"] for row in data["opcodeInventory"]) != data["totalInstructions"]:
        raise ValueError("Opcode counts do not sum to total instructions executed")
    pdf = canvas.Canvas(str(output), pagesize=letter, pageCompression=1)
    pdf.setTitle("The Witness - Maximum-Score Walkthrough")
    pdf.setAuthor("LeafpileGPT project")
    pdf.setSubject("Verified conviction route, room maps, and executed opcode inventory")
    cover(pdf, data)
    page = 2
    ranges = [(1, 28), (29, 56), (57, len(data["commands"]))]
    for start, end in ranges:
        command_page(pdf, data, start, end, page)
        page += 1
    map_page_one(pdf, page); page += 1
    map_page_two(pdf, page); page += 1
    inventory_pages(pdf, data, page)
    pdf.save()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--refresh-trace", type=Path,
                        help="refresh the checked-in opcode inventory from a local RouteReplay JSONL trace")
    parser.add_argument("--output", type=Path, default=OUTPUT_PATH)
    args = parser.parse_args()
    if args.refresh_trace:
        refresh_trace(args.refresh_trace)
    data = json.loads(DATA_PATH.read_text())
    data["commandsCount"] = len(data["commands"])
    build(data, args.output)
    print(f"Created {args.output}: {data['totalInstructions']:,} instructions, "
          f"{len(data['opcodeInventory'])} opcode rows, {len(data['commands'])} commands")


if __name__ == "__main__":
    main()
