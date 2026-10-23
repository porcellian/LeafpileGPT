#!/usr/bin/env python3
"""Build Wishbringer's field guide from committed route and trace data."""

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
DATA_PATH = ROOT / "walkthroughs" / "source-data" / "wishbringer.json"
OUTPUT_PATH = ROOT / "walkthroughs" / "wishbringer-maximum-score.pdf"

PAPER = colors.HexColor("#F6F2E8")
CARD = colors.HexColor("#FFFCF5")
TEAL = colors.HexColor("#247F7C")
NAVY = colors.HexColor("#153F4B")
MUTED = colors.HexColor("#637B80")
PALE = colors.HexColor("#E6ECE4")
CORAL = colors.HexColor("#D96C55")
PENCIL = colors.HexColor("#38484A")
RULE = colors.HexColor("#D9D4C8")


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


def chrome(pdf: canvas.Canvas, section: str, page: int, size=letter) -> tuple[float, float]:
    width, height = size
    pdf.setPageSize(size)
    pdf.setFillColor(PAPER)
    pdf.rect(0, 0, width, height, fill=1, stroke=0)
    pdf.setStrokeColor(RULE)
    pdf.setLineWidth(0.35)
    for y in range(42, int(height), 28):
        pdf.line(0, y, width, y)
    pdf.setFillColor(TEAL)
    pdf.roundRect(34, height - 39, width - 68, 30, 10, fill=1, stroke=0)
    pdf.setFillColor(colors.white)
    pdf.setFont("Helvetica-Bold", 8)
    pdf.drawString(48, height - 27, section.upper())
    pdf.setStrokeColor(TEAL)
    pdf.setLineWidth(0.8)
    pdf.line(34, 27, width - 34, 27)
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica-Bold", 6.5)
    pdf.drawString(36, 15, "WISHBRINGER  |  100 / 100  |  NO WISHES")
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
    width, height = chrome(pdf, "A Wishbringer field guide", 1)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 30)
    pdf.drawString(52, height - 91, "WISHBRINGER")
    pdf.setFillColor(CORAL)
    pdf.setFont("Helvetica-Bold", 14)
    pdf.drawString(54, height - 115, "100-point, no-wish walkthrough")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 8.5)
    pdf.drawString(54, height - 133, "A fresh-start route through Festeron, Witchville, and the tower")

    pdf.setFillColor(CARD)
    pdf.roundRect(48, height - 226, width - 96, 75, 12, fill=1, stroke=0)
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 8)
    pdf.drawString(64, height - 169, "VERIFIED FINISH")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 23)
    pdf.drawString(64, height - 201, "100 / 100")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica-Bold", 8)
    pdf.drawRightString(width - 64, height - 169, f"{data['commandsCount']} commands  |  {data['moves']} moves  |  no deaths")
    pdf.setFont("Helvetica", 7.5)
    pdf.drawRightString(width - 64, height - 189, f"{data['totalInstructions']:,} executed instructions  |  seed {data['randomSeed']}")
    pdf.drawRightString(width - 64, height - 207, f"{len(data['opcodeInventory'])} opcode rows  |  Release {data['release']}  |  Serial {data['serial']}")

    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 11)
    pdf.drawString(54, height - 252, "Before you start")
    y = height - 270
    for text in data["intro"]:
        y = paragraph(pdf, text, 56, y, width - 112, size=7.5, leading=9)
        y -= 4

    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 10.5)
    pdf.drawString(54, y - 1, "Score checkpoints")
    checkpoints = data["checkpoints"]
    card_y = y - 14
    gap = 8
    card_width = (width - 108 - gap * 2) / 3
    for i, item in enumerate(checkpoints):
        col, row = i % 3, i // 3
        x = 54 + col * (card_width + gap)
        top = card_y - row * 49
        pdf.setFillColor(CARD)
        pdf.roundRect(x, top - 41, card_width, 38, 7, fill=1, stroke=0)
        pdf.setFillColor(CORAL if i == len(checkpoints) - 1 else TEAL)
        pdf.setFont("Helvetica-Bold", 7.4)
        pdf.drawString(x + 8, top - 15, item["heading"])
        pdf.setFillColor(NAVY)
        pdf.setFont("Helvetica", 6.2)
        for j, line in enumerate(wrap(item["detail"], "Helvetica", 6.2, card_width - 16)[:2]):
            pdf.drawString(x + 8, top - 27 - j * 7, line)

    pdf.setFillColor(CARD)
    pdf.roundRect(54, 282, width - 108, 38, 7, fill=1, stroke=0)
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 7)
    pdf.drawString(64, 303, "ENDING VERIFIED")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica", 6.4)
    for i, line in enumerate(wrap(data["endingVerification"], "Helvetica", 6.4, width - 185)[:2]):
        pdf.drawString(159, 303 - i * 8, line)
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 7)
    pdf.drawString(54, 55, "The Wishbringer's seven wishes remain unused; the no-wish route earns the full 100 points.")
    pdf.drawString(54, 43, "The route avoids the Boot Patrol and uses the branch to rescue Tasmania without a death.")
    pdf.showPage()


def route_pages(pdf: canvas.Canvas, data: dict, page: int) -> int:
    commands = data["commands"]
    per_page = 42
    for start in range(0, len(commands), per_page):
        end = min(start + per_page, len(commands))
        width, height = chrome(pdf, "Numbered route", page)
        pdf.setFillColor(NAVY)
        pdf.setFont("Helvetica-Bold", 16)
        pdf.drawString(52, height - 69, f"Commands {start + 1:03d}-{end:03d}")
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 7.5)
        pdf.drawString(54, height - 85, "Read down the left column, then down the right. The coral entries mark score checkpoints.")
        items = list(enumerate(commands[start:end], start=start + 1))
        split = (len(items) + 1) // 2
        columns = [items[:split], items[split:]]
        x_positions = [52, 315]
        checkpoint_map = {int(k): v for k, v in data["routeNotes"].items()}
        for column, column_items in enumerate(columns):
            x, y = x_positions[column], height - 111
            for number, command in column_items:
                if str(number) in data["stepStarts"]:
                    pdf.setFillColor(CORAL)
                    pdf.setFont("Helvetica-Bold", 6.1)
                    pdf.drawString(x, y + 3, data["stepStarts"][str(number)])
                    y -= 8
                pdf.setFillColor(CORAL if number in checkpoint_map else TEAL)
                pdf.setFont("Courier-Bold", 7)
                pdf.drawRightString(x + 27, y, f"{number:03d}")
                pdf.setFillColor(NAVY)
                pdf.setFont("Courier", 7)
                lines = wrap(command, "Courier", 7, 220)
                for i, line in enumerate(lines):
                    pdf.drawString(x + 34, y - i * 8, line)
                y -= max(15, 8 * len(lines) + 2)
                for note in checkpoint_map.get(number, []):
                    pdf.setFillColor(CORAL)
                    pdf.setFont("Helvetica-Bold", 5.7)
                    for i, line in enumerate(wrap(note, "Helvetica-Bold", 5.7, 220)[:2]):
                        pdf.drawString(x + 34, y + 2 - i * 7, line)
                    y -= 7 * min(2, len(wrap(note, "Helvetica-Bold", 5.7, 220))) + 2
            if column == 0:
                pdf.setStrokeColor(RULE)
                pdf.line(304, 63, 304, height - 102)
        pdf.showPage()
        page += 1
    return page


def hand_line(pdf: canvas.Canvas, x1: float, y1: float, x2: float, y2: float,
              seed: int, color=CORAL, width=0.9) -> None:
    dx, dy = x2 - x1, y2 - y1
    length = max(1, math.hypot(dx, dy))
    px, py = -dy / length, dx / length
    wobble = ((seed * 37) % 7 - 3) * 0.4
    mx, my = (x1 + x2) / 2, (y1 + y2) / 2
    pdf.setStrokeColor(color)
    pdf.setLineWidth(width)
    pdf.line(x1, y1, mx + px * wobble, my + py * wobble)
    pdf.line(mx - px * wobble * 0.3, my - py * wobble * 0.3, x2, y2)
    angle = math.atan2(dy, dx)
    for delta in (-0.48, 0.48):
        pdf.line(x2, y2, x2 - 4.2 * math.cos(angle + delta), y2 - 4.2 * math.sin(angle + delta))


def hand_box(pdf: canvas.Canvas, x: float, y: float, w: float, h: float,
             label: str, number: int) -> None:
    pdf.saveState()
    pdf.setFillColor(PALE)
    pdf.setStrokeColor(PENCIL)
    pdf.setLineWidth(0.85)
    for dx, dy in ((0, 0), (0.8, -0.6)):
        path = pdf.beginPath()
        path.moveTo(x + dx + 1, y + dy + 1)
        path.lineTo(x + dx + w - 2, y + dy + 1 + number % 2)
        path.lineTo(x + dx + w - 1, y + dy + h - 2)
        path.lineTo(x + dx + 2 + number % 3, y + dy + h - 1)
        path.close()
        pdf.drawPath(path, fill=1 if dx == 0 else 0, stroke=1)
    pdf.setFillColor(CORAL)
    pdf.circle(x + 9, y + h - 10, 2, fill=1, stroke=0)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 6.7)
    lines = wrap(label, "Helvetica-Bold", 6.7, w - 20)[:2]
    label_y = y + h / 2 + (3 if len(lines) == 2 else -2)
    for line in lines:
        pdf.drawString(x + 16, label_y, line)
        label_y -= 8
    pdf.restoreState()


def doodle(pdf: canvas.Canvas, kind: str, x: float, y: float) -> None:
    pdf.saveState()
    pdf.setStrokeColor(CORAL)
    pdf.setLineWidth(1.2)
    if kind == "envelope":
        pdf.rect(x - 19, y - 12, 38, 24, fill=0, stroke=1)
        pdf.line(x - 19, y + 12, x, y - 1)
        pdf.line(x + 19, y + 12, x, y - 1)
        pdf.line(x - 19, y - 12, x, y + 2)
        pdf.line(x + 19, y - 12, x, y + 2)
    elif kind == "town":
        for dx, dy, w, h in ((-22, -12, 17, 25), (2, -12, 19, 33), (25, -12, 15, 20)):
            pdf.rect(x + dx, y + dy, w, h, fill=0, stroke=1)
            pdf.line(x + dx + 4, y + dy + h - 6, x + dx + w - 4, y + dy + h - 6)
        pdf.line(x - 30, y - 13, x + 45, y - 13)
    elif kind == "cliff":
        pdf.line(x - 39, y + 18, x - 15, y + 5)
        pdf.line(x - 15, y + 5, x - 3, y - 3)
        pdf.line(x - 3, y - 3, x + 8, y - 19)
        pdf.line(x + 8, y - 19, x + 40, y - 19)
        pdf.line(x - 27, y - 11, x + 24, y - 11)
    else:
        pdf.ellipse(x - 33, y - 13, x + 23, y + 13, fill=0, stroke=1)
        pdf.circle(x + 28, y + 1, 7, fill=0, stroke=1)
        pdf.line(x - 21, y - 9, x - 27, y - 18)
        pdf.line(x + 1, y - 9, x - 5, y - 18)
        pdf.arc(x - 14, y - 8, x + 12, y + 8, 0, 170)
    pdf.restoreState()


def map_page(pdf: canvas.Canvas, data: dict, map_data: dict, page: int) -> None:
    width, height = chrome(pdf, f"Field sketch  |  {map_data['title']}", page, landscape(letter))
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 15)
    pdf.drawString(45, height - 65, map_data["title"])
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 6.8)
    pdf.drawString(47, height - 79, map_data["caption"])
    rooms = map_data["rooms"]
    # The loose grid follows the observed route while leaving room for side locations.
    box_w, box_h = 125, 28
    xs = [42, 188, 334, 480, 626]
    grid = map_data["grid"]
    rows = len(grid)
    ytop = height - 125
    step_y = min(67, 318 / max(1, rows - 1)) if rows > 1 else 67
    positions = {}
    for row, row_ids in enumerate(grid):
        for col, room_id in enumerate(row_ids):
            if room_id is not None:
                positions[room_id] = (xs[col], ytop - row * step_y)

    for idx, edge in enumerate(map_data["edges"]):
        a, b, label = edge
        if a not in positions or b not in positions:
            continue
        ax, ay = positions[a]
        bx, by = positions[b]
        sx, sy = ax + box_w / 2, ay + box_h / 2
        tx, ty = bx + box_w / 2, by + box_h / 2
        dx, dy = tx - sx, ty - sy
        length = max(1, math.hypot(dx, dy))
        ux, uy = dx / length, dy / length
        trim = min((box_w / 2 - 4) / abs(ux) if ux else math.inf,
                   (box_h / 2 - 4) / abs(uy) if uy else math.inf)
        sx, sy = sx + ux * trim, sy + uy * trim
        tx, ty = tx - ux * trim, ty - uy * trim
        hand_line(pdf, sx, sy, tx, ty, idx + page)
        mx, my = (sx + tx) / 2, (sy + ty) / 2
        pdf.setFillColor(PAPER)
        pdf.roundRect(mx - 20, my - 4, 40, 8, 2, fill=1, stroke=0)
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 5)
        pdf.drawCentredString(mx, my - 1.5, label)

    for idx, room in enumerate(rooms):
        x, y = positions[room["id"]]
        hand_box(pdf, x, y, box_w, box_h, room["label"], idx + 1)
    doodle(pdf, map_data["icon"], 87, 75)
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 6.4)
    pdf.drawString(140, 77, "FIELD NOTES")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica-Oblique", 6.2)
    pdf.drawString(199, 77, "Arrows mark route-verified links; placement is schematic. Split Underground rooms have numeric tags.")
    pdf.drawString(140, 65, "A room without a connecting arrow is still a named room in the story world; this sketch keeps crowded areas legible.")
    pdf.showPage()


def inventory_pages(pdf: canvas.Canvas, data: dict, page: int) -> int:
    rows = data["opcodeInventory"]
    if sum(row["Count"] for row in rows) != data["totalInstructions"]:
        raise ValueError("Opcode counts do not sum to total instructions executed")
    per_page = 16
    total_pages = math.ceil(len(rows) / per_page)
    for index in range(total_pages):
        width, height = chrome(pdf, "Executed opcode inventory", page)
        chunk = rows[index * per_page:(index + 1) * per_page]
        pdf.setFillColor(NAVY)
        pdf.setFont("Helvetica-Bold", 16)
        pdf.drawString(52, height - 69, f"Opcode table {index + 1} of {total_pages}")
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 7.2)
        pdf.drawString(54, height - 85, "Rows use the executed instruction's form and are ordered by first appearance in the successful replay.")
        pdf.setFillColor(CARD)
        pdf.roundRect(50, height - 132, width - 100, 33, 8, fill=1, stroke=0)
        pdf.setFillColor(TEAL)
        pdf.setFont("Helvetica-Bold", 7.5)
        pdf.drawString(64, height - 114, f"TOTAL INSTRUCTIONS  {data['totalInstructions']:,}")
        pdf.drawRightString(width - 64, height - 114, f"{len(rows)} unique form/opcode/mnemonic combinations")
        x0, y0 = 52, height - 160
        widths = [66, 55, 116, 80, 95, 90]
        headers = ["Form", "Opcode", "Mnemonic", "Count", "First Seq", "First PC"]
        pdf.setFillColor(TEAL)
        pdf.roundRect(x0, y0 - 20, sum(widths), 21, 5, fill=1, stroke=0)
        xpos = x0
        for i, header in enumerate(headers):
            pdf.setFillColor(colors.white)
            pdf.setFont("Helvetica-Bold", 7)
            pdf.drawString(xpos + 6, y0 - 13, header)
            xpos += widths[i]
        for row_index, row in enumerate(chunk):
            y = y0 - 21 - row_index * 24
            pdf.setFillColor(CARD if row_index % 2 == 0 else PALE)
            pdf.roundRect(x0, y - 20, sum(widths), 22, 3, fill=1, stroke=0)
            vals = [row["Form"], str(row["Opcode"]), row["Mnemonic"], f"{row['Count']:,}",
                    f"{row['FirstSequence']:,}", f"0x{row['FirstAddress']:04X}"]
            xpos = x0
            for col, value in enumerate(vals):
                pdf.setFillColor(NAVY)
                pdf.setFont("Courier", 7)
                if col in (1, 3, 4, 5):
                    pdf.drawRightString(xpos + widths[col] - 7, y - 7, value)
                else:
                    pdf.drawString(xpos + 6, y - 7, value)
                xpos += widths[col]
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica-Oblique", 6.3)
        pdf.drawString(54, 43, "Count totals reconcile to the full instruction total; First Seq is one-based and First PC is hexadecimal.")
        pdf.showPage()
        page += 1
    return page


def build(data: dict, output: Path) -> int:
    if len(data["commands"]) != data["commandsCount"]:
        raise ValueError("Command count does not match route data")
    if sum(row["Count"] for row in data["opcodeInventory"]) != data["totalInstructions"]:
        raise ValueError("Opcode counts do not sum to total instructions executed")
    room_ids = [room["id"] for m in data["maps"] for room in m["rooms"]]
    if len(room_ids) != 52 or len(set(room_ids)) != 52:
        raise ValueError("The three field sketches must cover all 52 distinct room objects exactly once")
    for map_data in data["maps"]:
        grid_ids = [room_id for row in map_data["grid"] for room_id in row if room_id is not None]
        if len(grid_ids) != len(set(grid_ids)) or set(grid_ids) != {room["id"] for room in map_data["rooms"]}:
            raise ValueError(f"Map grid does not position each room exactly once: {map_data['title']}")
    output.parent.mkdir(parents=True, exist_ok=True)
    pdf = canvas.Canvas(str(output), pagesize=letter, pageCompression=1)
    pdf.setTitle("Wishbringer - 100-point, no-wish walkthrough")
    pdf.setAuthor("LeafpileGPT project")
    pdf.setSubject("Verified Wishbringer 100-point route, field maps, and executed opcode inventory")
    cover(pdf, data)
    page = route_pages(pdf, data, 2)
    for map_data in data["maps"]:
        map_page(pdf, data, map_data, page)
        page += 1
    page = inventory_pages(pdf, data, page)
    pdf.save()
    return page - 1


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=OUTPUT_PATH)
    args = parser.parse_args()
    data = json.loads(DATA_PATH.read_text())
    pages = build(data, args.output)
    print(f"Created {args.output}: {data['score']}/100, {data['commandsCount']} commands, "
          f"{data['totalInstructions']:,} instructions, {len(data['opcodeInventory'])} opcode rows, "
          f"52 locations, {pages} pages")


if __name__ == "__main__":
    main()
