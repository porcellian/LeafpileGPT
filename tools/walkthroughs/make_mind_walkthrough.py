#!/usr/bin/env python3
"""Build the A Mind Forever Voyaging field guide from route and trace data."""

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
DATA_PATH = ROOT / "walkthroughs" / "source-data" / "mind.json"
OUTPUT_PATH = ROOT / "walkthroughs" / "mind-maximum-score.pdf"

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
    pdf.drawString(36, 15, "A MIND FOREVER VOYAGING  |  Z4 BEST ENDING")
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
    width, height = chrome(pdf, "A Mind Forever Voyaging field guide", 1)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 27)
    pdf.drawString(52, height - 82, "A MIND FOREVER VOYAGING")
    pdf.setFillColor(CORAL)
    pdf.setFont("Helvetica-Bold", 12)
    pdf.drawString(54, height - 105, "Best-ending walkthrough  |  local Z4 release")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 8)
    pdf.drawString(54, height - 121, "A command-by-command route from PRISM's awakening to the Silver Dove")

    pdf.setFillColor(CARD)
    pdf.roundRect(48, height - 207, width - 96, 70, 12, fill=1, stroke=0)
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 8)
    pdf.drawString(64, height - 155, "VERIFIED FINISH")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 16)
    pdf.drawString(64, height - 179, "The New Plan replaces Ryder's plan")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica-Bold", 7.2)
    pdf.drawRightString(width - 64, height - 155, f"{data['commandsCount']:,} commands  |  no numeric score")
    pdf.setFont("Helvetica", 7.1)
    pdf.drawRightString(width - 64, height - 173, f"{data['totalInstructions']:,} executed instructions  |  seed {data['randomSeed']}")
    pdf.drawRightString(width - 64, height - 190, f"{len(data['opcodeInventory'])} opcode rows  |  Release {data['release']}  |  Serial {data['serial']}")

    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 10.5)
    pdf.drawString(54, height - 230, "Before you start")
    y = height - 247
    for text in data["intro"]:
        y = paragraph(pdf, text, 56, y, width - 112, size=7.4, leading=9)
        y -= 3

    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 9.5)
    pdf.drawString(54, y - 1, "Outcome checkpoints")
    card_y = y - 14
    gap = 8
    card_width = (width - 108 - gap * 2) / 3
    for i, item in enumerate(data["checkpoints"]):
        col, row = i % 3, i // 3
        x = 54 + col * (card_width + gap)
        top = card_y - row * 48
        pdf.setFillColor(CARD)
        pdf.roundRect(x, top - 40, card_width, 37, 7, fill=1, stroke=0)
        pdf.setFillColor(CORAL if i == len(data["checkpoints"]) - 1 else TEAL)
        pdf.setFont("Helvetica-Bold", 7.1)
        pdf.drawString(x + 8, top - 14, item["heading"])
        pdf.setFillColor(NAVY)
        pdf.setFont("Helvetica", 6.1)
        for j, line in enumerate(wrap(item["detail"], "Helvetica", 6.1, card_width - 16)[:2]):
            pdf.drawString(x + 8, top - 25 - j * 7, line)

    pdf.setFillColor(CARD)
    pdf.roundRect(54, 114, width - 108, 45, 7, fill=1, stroke=0)
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 7)
    pdf.drawString(64, 143, "ENDING VERIFIED")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica", 6.4)
    for i, line in enumerate(wrap(data["endingVerification"], "Helvetica", 6.4, width - 190)[:3]):
        pdf.drawString(159, 143 - i * 8, line)
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 6.7)
    pdf.drawString(54, 69, "The requested Z3 file was not present locally; this route uses the available Z4 release.")
    pdf.drawString(54, 56, "Fatal events occur inside future simulations; PRISM survives the mission and completes the voyage.")
    pdf.showPage()


def route_pages(pdf: canvas.Canvas, data: dict, page: int) -> int:
    commands = data["commands"]
    per_page = 42
    starts = {int(k): v for k, v in data["stepStarts"].items()}
    notes = {int(k): v for k, v in data["routeNotes"].items()}
    for start in range(0, len(commands), per_page):
        end = min(start + per_page, len(commands))
        width, height = chrome(pdf, "Numbered route", page)
        pdf.setFillColor(NAVY)
        pdf.setFont("Helvetica-Bold", 15)
        pdf.drawString(52, height - 68, f"Commands {start + 1:04d}-{end:04d}")
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 7.2)
        pdf.drawString(54, height - 83, "Enter each numbered line at the current prompt. Single-letter entries dismiss story pauses.")
        items = list(enumerate(commands[start:end], start=start + 1))
        split = (len(items) + 1) // 2
        columns = [items[:split], items[split:]]
        x_positions = [52, 315]
        for column, column_items in enumerate(columns):
            x, y = x_positions[column], height - 108
            for number, command in column_items:
                if number in starts:
                    pdf.setFillColor(CORAL)
                    pdf.setFont("Helvetica-Bold", 5.8)
                    pdf.drawString(x, y + 3, starts[number])
                    y -= 8
                pdf.setFillColor(CORAL if number in notes else TEAL)
                pdf.setFont("Courier-Bold", 6.8)
                pdf.drawRightString(x + 27, y, f"{number:04d}")
                pdf.setFillColor(NAVY)
                pdf.setFont("Courier", 6.8)
                lines = wrap(command, "Courier", 6.8, 218)
                for i, line in enumerate(lines):
                    pdf.drawString(x + 34, y - i * 7.7, line)
                y -= max(13, 7.7 * len(lines) + 2)
                for note in notes.get(number, []):
                    pdf.setFillColor(CORAL)
                    pdf.setFont("Helvetica-Bold", 5.6)
                    note_lines = wrap(note, "Helvetica-Bold", 5.6, 218)[:2]
                    for i, line in enumerate(note_lines):
                        pdf.drawString(x + 34, y + 1 - i * 6.7, line)
                    y -= 6.7 * len(note_lines) + 2
            if column == 0:
                pdf.setStrokeColor(RULE)
                pdf.line(304, 54, 304, height - 100)
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
    pdf.setFont("Helvetica-Bold", 6.4)
    lines = wrap(label, "Helvetica-Bold", 6.4, w - 20)[:2]
    label_y = y + h / 2 + (3 if len(lines) == 2 else -2)
    for line in lines:
        pdf.drawString(x + 16, label_y, line)
        label_y -= 7.5
    pdf.restoreState()


def doodle(pdf: canvas.Canvas, kind: str, x: float, y: float) -> None:
    pdf.saveState()
    pdf.setStrokeColor(CORAL)
    pdf.setLineWidth(1.15)
    if kind == "town":
        for dx, dy, w, h in ((-24, -10, 16, 23), (0, -10, 18, 33), (27, -10, 20, 18)):
            pdf.rect(x + dx, y + dy, w, h, fill=0, stroke=1)
            pdf.line(x + dx + 4, y + dy + h - 6, x + dx + w - 4, y + dy + h - 6)
        pdf.line(x - 34, y - 12, x + 49, y - 12)
    elif kind == "church":
        pdf.rect(x - 26, y - 13, 52, 27, fill=0, stroke=1)
        pdf.line(x - 12, y + 14, x - 12, y + 28)
        pdf.line(x - 19, y + 21, x - 5, y + 21)
        pdf.line(x - 26, y + 14, x, y + 28)
        pdf.line(x, y + 28, x + 26, y + 14)
        pdf.rect(x - 4, y - 13, 8, 14, fill=0, stroke=1)
    elif kind == "core":
        pdf.rect(x - 29, y - 16, 58, 30, fill=0, stroke=1)
        pdf.circle(x - 12, y - 1, 8, fill=0, stroke=1)
        pdf.line(x - 12, y - 10, x - 12, y + 8)
        pdf.line(x - 21, y - 1, x - 3, y - 1)
        pdf.rect(x + 4, y - 10, 18, 19, fill=0, stroke=1)
        pdf.line(x + 8, y - 4, x + 18, y - 4)
        pdf.line(x + 8, y + 1, x + 18, y + 1)
    elif kind == "skyvan":
        pdf.ellipse(x - 31, y - 8, x + 23, y + 9, fill=0, stroke=1)
        pdf.line(x - 5, y - 7, x - 11, y - 17)
        pdf.line(x + 9, y - 6, x + 4, y - 17)
        pdf.circle(x + 29, y + 1, 7, fill=0, stroke=1)
        pdf.line(x + 27, y - 10, x + 33, y - 10)
    elif kind == "newspaper":
        pdf.rect(x - 24, y - 16, 48, 32, fill=0, stroke=1)
        pdf.line(x - 18, y + 9, x + 18, y + 9)
        pdf.line(x - 18, y + 3, x + 18, y + 3)
        pdf.line(x - 18, y - 4, x + 14, y - 4)
        pdf.line(x - 18, y - 10, x + 10, y - 10)
    else:
        pdf.ellipse(x - 28, y - 10, x + 20, y + 10, fill=0, stroke=1)
        pdf.line(x + 18, y, x + 32, y + 12)
        pdf.line(x + 18, y, x + 32, y - 12)
        pdf.circle(x - 13, y + 2, 1.8, fill=1, stroke=0)
    pdf.restoreState()


def map_page(pdf: canvas.Canvas, map_data: dict, page: int) -> None:
    width, height = chrome(pdf, f"Field sketch  |  {map_data['title']}", page, landscape(letter))
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 15)
    pdf.drawString(45, height - 65, map_data["title"])
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 6.8)
    pdf.drawString(47, height - 79, map_data["caption"])
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
        pdf.roundRect(mx - 18, my - 4, 36, 8, 2, fill=1, stroke=0)
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 5)
        pdf.drawCentredString(mx, my - 1.5, label)
    labels = {room["id"]: room["label"] for room in map_data["rooms"]}
    for idx, room in enumerate(map_data["rooms"]):
        x, y = positions[room["id"]]
        hand_box(pdf, x, y, box_w, box_h, room["label"], idx + 1)
    doodle(pdf, map_data["icon"], 87, 75)
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 6.4)
    pdf.drawString(140, 77, "FIELD NOTES")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica-Oblique", 6.1)
    pdf.drawString(199, 77, "Arrows mark route-verified links; the layout is a hand-sketched guide, not to scale.")
    pdf.drawString(140, 65, map_data["note"])
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
        pdf.drawString(54, 43, "Count totals reconcile to all instructions; First Seq is one-based and First PC is hexadecimal.")
        pdf.showPage()
        page += 1
    return page


def build(data: dict, output: Path) -> int:
    if len(data["commands"]) != data["commandsCount"]:
        raise ValueError("Command count does not match route data")
    if sum(row["Count"] for row in data["opcodeInventory"]) != data["totalInstructions"]:
        raise ValueError("Opcode counts do not sum to total instructions executed")
    labels = {room["label"] for map_data in data["maps"] for room in map_data["rooms"]}
    if not set(data["allMapLabels"]).issubset(labels):
        raise ValueError(f"Maps omit route-discovered locations: {sorted(set(data['allMapLabels']) - labels)}")
    for map_data in data["maps"]:
        grid_ids = [room_id for row in map_data["grid"] for room_id in row if room_id is not None]
        if len(grid_ids) != len(set(grid_ids)) or set(grid_ids) != {room["id"] for room in map_data["rooms"]}:
            raise ValueError(f"Map grid does not position each room exactly once: {map_data['title']}")
    output.parent.mkdir(parents=True, exist_ok=True)
    pdf = canvas.Canvas(str(output), pagesize=letter, pageCompression=1)
    pdf.setTitle("A Mind Forever Voyaging - Z4 best-ending walkthrough")
    pdf.setAuthor("LeafpileGPT project")
    pdf.setSubject("Fresh Mind Z4 route, hand-sketched maps, and executed opcode inventory")
    cover(pdf, data)
    page = route_pages(pdf, data, 2)
    for map_data in data["maps"]:
        map_page(pdf, map_data, page)
        page += 1
    page = inventory_pages(pdf, data, page)
    pdf.save()
    return page - 1


def refresh_inventory(data: dict, trace_path: Path) -> None:
    summary = None
    with trace_path.open(encoding="utf-8") as stream:
        for line in stream:
            if '"opcodeInventory"' in line:
                summary = json.loads(line)
    if summary is None:
        raise ValueError(f"No opcode summary found in trace: {trace_path}")
    rows = summary["opcodeInventory"]
    total = summary["instructionsExecuted"]
    if sum(row["Count"] for row in rows) != total:
        raise ValueError("Trace opcode counts do not sum to its instruction total")
    data["totalInstructions"] = total
    data["opcodeInventory"] = rows


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--refresh-trace", type=Path, help="refresh opcode data from a local replay trace")
    parser.add_argument("--output", type=Path, default=OUTPUT_PATH)
    args = parser.parse_args()
    data = json.loads(DATA_PATH.read_text(encoding="utf-8"))
    if args.refresh_trace:
        refresh_inventory(data, args.refresh_trace)
        DATA_PATH.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    pages = build(data, args.output)
    print(f"Wrote {args.output}: {pages} pages, {len(data['opcodeInventory'])} opcode rows, "
          f"{data['totalInstructions']:,} instructions")


if __name__ == "__main__":
    main()
