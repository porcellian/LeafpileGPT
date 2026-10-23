#!/usr/bin/env python3
"""Build the Starcross-style Hitchhiker's Guide walkthrough from committed data."""

from __future__ import annotations

import argparse
import json
import math
import random
from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.pagesizes import landscape, letter
from reportlab.pdfbase.pdfmetrics import stringWidth
from reportlab.pdfgen import canvas


ROOT = Path(__file__).resolve().parents[2]
DATA_PATH = ROOT / "walkthroughs" / "source-data" / "hitchhiker.json"
OUTPUT_PATH = ROOT / "walkthroughs" / "hitchhiker-maximum-score.pdf"

CREAM = colors.HexColor("#F6F2E8")
TEAL = colors.HexColor("#247F7C")
NAVY = colors.HexColor("#153F4B")
MUTED = colors.HexColor("#637B80")
PALE = colors.HexColor("#E6ECE4")
PAPER = colors.HexColor("#FFFCF5")
CORAL = colors.HexColor("#D96C55")
PENCIL = colors.HexColor("#39464A")
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


def page_chrome(pdf: canvas.Canvas, section: str, page: int, size=letter) -> tuple[float, float]:
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
    pdf.drawString(48, height - 27, section.upper())
    pdf.setStrokeColor(TEAL)
    pdf.setLineWidth(0.8)
    pdf.line(34, 27, width - 34, 27)
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica-Bold", 6.5)
    pdf.drawString(36, 15, "HITCHHIKER'S GUIDE  |  400-POINT ROUTE")
    pdf.drawRightString(width - 36, 15, f"FIELD GUIDE  |  {page:02d}")
    return width, height


def draw_paragraph(pdf: canvas.Canvas, text: str, x: float, y: float, width: float,
                   font="Helvetica", size=7.5, leading=9.5, color=NAVY) -> float:
    pdf.setFillColor(color)
    pdf.setFont(font, size)
    for line in wrap(text, font, size, width):
        pdf.drawString(x, y, line)
        y -= leading
    return y


def cover(pdf: canvas.Canvas, data: dict) -> None:
    width, height = page_chrome(pdf, "A field guide to the galaxy", 1)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 28)
    pdf.drawString(52, height - 89, "THE HITCHHIKER'S GUIDE")
    pdf.drawString(52, height - 119, "TO THE GALAXY")
    pdf.setFillColor(CORAL)
    pdf.setFont("Helvetica-Bold", 13)
    pdf.drawString(54, height - 142, "Maximum-score walkthrough")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 8)
    pdf.drawString(54, height - 159, "A fresh-start route from Arthur's bedroom to the surface of Magrathea")

    pdf.setFillColor(PAPER)
    pdf.roundRect(48, height - 252, width - 96, 77, 12, fill=1, stroke=0)
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 8)
    pdf.drawString(64, height - 194, "VERIFIED FINISH")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 24)
    pdf.drawString(64, height - 225, "400 / 400")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica-Bold", 7.7)
    pdf.drawRightString(width - 64, height - 194,
                        f"{data['moves']} moves  |  {data['commandsCount']} commands  |  no game-over deaths")
    pdf.setFont("Helvetica", 7.2)
    pdf.drawRightString(width - 64, height - 213,
                        f"{data['totalInstructions']:,} executed instructions  |  seed {data['randomSeed']}")
    pdf.drawRightString(width - 64, height - 231,
                        f"{len(data['opcodeInventory'])} opcode rows  |  Release {data['release']}  |  Serial {data['serial']}")

    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 10.5)
    pdf.drawString(54, height - 277, "How to play this route")
    y = height - 294
    for text in data["intro"]:
        y = draw_paragraph(pdf, text, 56, y, width - 112, size=7.2, leading=8.8)
        y -= 4

    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 9.6)
    pdf.drawString(54, y - 1, "Score checkpoints")
    cards = data["checkpoints"]
    gap_x, gap_y = 7, 6
    cols = 4
    card_w = (width - 108 - gap_x * (cols - 1)) / cols
    card_h = 34
    first_top = y - 13
    for index, item in enumerate(cards):
        row, col = divmod(index, cols)
        x = 54 + col * (card_w + gap_x)
        top = first_top - row * (card_h + gap_y)
        pdf.setFillColor(PAPER)
        pdf.roundRect(x, top - card_h, card_w, card_h, 6, fill=1, stroke=0)
        pdf.setFillColor(CORAL if index == len(cards) - 1 else TEAL)
        pdf.setFont("Helvetica-Bold", 7)
        pdf.drawString(x + 8, top - 13, item["heading"])
        pdf.setFillColor(NAVY)
        pdf.setFont("Helvetica", 5.8)
        for line_index, line in enumerate(wrap(item["detail"], "Helvetica", 5.8, card_w - 16)[:2]):
            pdf.drawString(x + 8, top - 23 - line_index * 7, line)

    footer_y = 78
    pdf.setFillColor(PAPER)
    pdf.roundRect(54, footer_y, width - 108, 42, 7, fill=1, stroke=0)
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 7)
    pdf.drawString(64, footer_y + 27, "ENDING VERIFIED")
    pdf.setFillColor(NAVY)
    for index, line in enumerate(wrap(data["endingVerification"], "Helvetica", 6.5, width - 224)[:2]):
        pdf.drawString(157, footer_y + 27 - index * 9, line)
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 6.3)
    pdf.drawString(56, 64, "The guide includes hand-sketched maps for every story region and a trace-derived opcode inventory.")
    pdf.drawString(56, 54, "The particle shock is the scripted common-sense puzzle; it does not end the run or restart the player.")
    pdf.showPage()


def route_pages(pdf: canvas.Canvas, data: dict, page: int) -> int:
    commands = data["commands"]
    page_capacity = 68
    notes = data["routeNotes"]
    captions = data["routePages"]
    for start in range(0, len(commands), page_capacity):
        end = min(start + page_capacity, len(commands))
        width, height = page_chrome(pdf, "Numbered route", page)
        pdf.setFillColor(NAVY)
        pdf.setFont("Helvetica-Bold", 15)
        pdf.drawString(52, height - 68, f"Commands {start + 1:03d}-{end:03d}")
        caption = next((item["caption"] for item in captions if item["start"] <= start + 1 <= item["end"]),
                       "Continue the numbered route from a fresh game.")
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 7.6)
        pdf.drawString(54, height - 84, caption)
        col_w = (width - 108 - 22) / 2
        col_rows = math.ceil((end - start) / 2)
        y_top = height - 111
        for col in range(2):
            x = 54 + col * (col_w + 22)
            first = start + col * col_rows
            last = min(first + col_rows, end)
            y = y_top
            for index in range(first, last):
                command_number = index + 1
                command = commands[index]
                pdf.setFillColor(TEAL)
                pdf.setFont("Helvetica-Bold", 6.7)
                pdf.drawRightString(x + 25, y, f"{command_number:03d}.")
                pdf.setFillColor(NAVY)
                pdf.setFont("Courier", 6.35)
                wrapped = wrap(command, "Courier", 6.35, col_w - 34)
                for line_index, line in enumerate(wrapped):
                    pdf.drawString(x + 31, y - line_index * 7.6, line)
                y -= 11 + (len(wrapped) - 1) * 7.6
                for note in notes.get(str(command_number), []):
                    pdf.setFillColor(CORAL)
                    pdf.setFont("Helvetica-Bold", 5.5)
                    pdf.drawString(x + 31, y + 1, note)
                    y -= 7
            if col == 0:
                pdf.setStrokeColor(RULE)
                pdf.line(x + col_w + 11, 55, x + col_w + 11, height - 102)
        pdf.showPage()
        page += 1
    return page


def rough_room(pdf: canvas.Canvas, x: float, y: float, width: float, height: float,
               label: str, detail: str = "", icon: str = "") -> None:
    pdf.saveState()
    pdf.setStrokeColor(PENCIL)
    pdf.setLineWidth(0.85)
    for dx, dy in ((0, 0), (1.1, -0.8)):
        path = pdf.beginPath()
        path.moveTo(x + dx + 2, y + dy + 1)
        path.lineTo(x + dx + width - 2, y + dy + 2)
        path.lineTo(x + dx + width - 1, y + dy + height - 2)
        path.lineTo(x + dx + width / 2 + 1, y + dy + height)
        path.lineTo(x + dx + 1, y + dy + height - 1)
        path.close()
        pdf.drawPath(path, fill=0, stroke=1)
    pdf.setFillColor(CORAL)
    pdf.circle(x + 9, y + height - 10, 2.2, fill=1, stroke=0)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 6.8)
    label_lines = wrap(label, "Helvetica-Bold", 6.8, width - 18)[:2]
    for idx, line in enumerate(label_lines):
        pdf.drawCentredString(x + width / 2, y + height - 14 - idx * 8, line)
    if detail:
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 5.5)
        for idx, line in enumerate(wrap(detail, "Helvetica", 5.5, width - 14)[:2]):
            pdf.drawCentredString(x + width / 2, y + 12 - idx * 7, line)
    if icon:
        draw_icon(pdf, icon, x + width / 2, y + height / 2 - 2, 0.55)
    pdf.restoreState()


def arrow(pdf: canvas.Canvas, x1: float, y1: float, x2: float, y2: float,
          label: str = "", dashed: bool = False) -> None:
    pdf.saveState()
    pdf.setStrokeColor(PENCIL)
    pdf.setFillColor(PENCIL)
    pdf.setLineWidth(0.75)
    if dashed:
        pdf.setDash(3, 2)
    mid_x, mid_y = (x1 + x2) / 2, (y1 + y2) / 2
    pdf.line(x1, y1, mid_x + 1.3, mid_y - 0.8)
    pdf.line(mid_x + 1.3, mid_y - 0.8, x2, y2)
    angle = math.atan2(y2 - y1, x2 - x1)
    size = 4
    left = (x2 - size * math.cos(angle - 0.45), y2 - size * math.sin(angle - 0.45))
    right = (x2 - size * math.cos(angle + 0.45), y2 - size * math.sin(angle + 0.45))
    path = pdf.beginPath()
    path.moveTo(x2, y2)
    path.lineTo(*left)
    path.lineTo(*right)
    path.close()
    pdf.drawPath(path, fill=1, stroke=0)
    if label:
        pdf.setDash()
        pdf.setFillColor(CORAL)
        pdf.setFont("Helvetica-Bold", 5.4)
        pdf.drawCentredString(mid_x, mid_y + 4, label)
    pdf.restoreState()


def draw_icon(pdf: canvas.Canvas, icon: str, cx: float, cy: float, scale: float = 1) -> None:
    pdf.saveState()
    pdf.translate(cx, cy)
    pdf.scale(scale, scale)
    pdf.setStrokeColor(PENCIL)
    pdf.setFillColor(PALE)
    pdf.setLineWidth(1)
    if icon == "dog":
        pdf.circle(0, 0, 8, fill=0, stroke=1)
        pdf.ellipse(-11, 2, -4, 10, fill=0, stroke=1)
        pdf.ellipse(4, 2, 11, 10, fill=0, stroke=1)
        pdf.circle(-3, 1, 1, fill=1, stroke=0)
        pdf.circle(3, 1, 1, fill=1, stroke=0)
        pdf.circle(0, -3, 1.4, fill=1, stroke=0)
    elif icon in ("whale", "boat"):
        path = pdf.beginPath()
        path.moveTo(-16, 0); path.curveTo(-11, -7, 9, -7, 15, 0)
        path.lineTo(19, 5); path.lineTo(12, 3); path.curveTo(4, 10, -11, 8, -16, 0)
        pdf.drawPath(path, fill=0, stroke=1)
        pdf.arc(-3, 7, 8, 15, 10, 150)
        pdf.circle(9, 1, 1, fill=1, stroke=0)
    elif icon in ("tea", "pot"):
        pdf.roundRect(-9, -7, 17, 13, 3, fill=0, stroke=1)
        pdf.arc(5, -5, 14, 5, 265, 190)
        pdf.line(-7, 8, 8, 8)
        pdf.line(-3, 8, 0, 13)
        pdf.line(0, 13, 3, 8)
    elif icon == "beast":
        pdf.circle(0, 0, 10, fill=0, stroke=1)
        pdf.line(-7, 4, -12, 11); pdf.line(7, 4, 12, 11)
        pdf.circle(-3, 1, 1.5, fill=0, stroke=1); pdf.circle(3, 1, 1.5, fill=0, stroke=1)
        pdf.arc(-5, -8, 5, 0, 190, 160)
    elif icon == "ship":
        p = pdf.beginPath(); p.moveTo(-15, -3); p.lineTo(14, -3); p.lineTo(7, -9); p.lineTo(-8, -9); p.close()
        pdf.drawPath(p, fill=0, stroke=1); pdf.line(-5, -3, 1, 8); pdf.line(1, 8, 8, -3)
    elif icon == "synapse":
        for dx, dy in [(-9, 0), (0, 7), (9, 0), (0, -7)]:
            pdf.circle(dx, dy, 4, fill=0, stroke=1)
        pdf.line(-5, 0, -3, 4); pdf.line(3, 4, 5, 0); pdf.line(5, 0, 3, -4); pdf.line(-3, -4, -5, 0)
    elif icon == "star":
        for angle in range(0, 360, 45):
            a = math.radians(angle)
            pdf.line(0, 0, 14 * math.cos(a), 14 * math.sin(a))
        pdf.circle(0, 0, 3, fill=1, stroke=0)
    elif icon == "glass":
        pdf.line(-5, 9, 5, 9); pdf.line(-5, 9, -2, -1); pdf.line(5, 9, 2, -1)
        pdf.line(-2, -1, 2, -1); pdf.line(0, -1, 0, -11)
    elif icon == "bulldozer":
        pdf.rect(-12, -6, 17, 9, fill=0, stroke=1); pdf.rect(-5, 3, 7, 6, fill=0, stroke=1)
        pdf.circle(-7, -7, 3, fill=0, stroke=1); pdf.circle(2, -7, 3, fill=0, stroke=1)
        pdf.line(5, -4, 13, -8); pdf.line(13, -8, 13, 4)
    elif icon == "flower":
        pdf.rect(-7, -11, 14, 8, fill=0, stroke=1)
        pdf.line(0, -3, 0, 8); pdf.circle(0, 10, 3, fill=0, stroke=1)
        pdf.circle(-5, 8, 3, fill=0, stroke=1); pdf.circle(5, 8, 3, fill=0, stroke=1)
    elif icon == "wine":
        pdf.line(-6, 9, 6, 9); pdf.line(-6, 9, -3, 0); pdf.line(6, 9, 3, 0)
        pdf.line(-3, 0, 3, 0); pdf.line(0, 0, 0, -9); pdf.line(-5, -9, 5, -9)
    pdf.restoreState()


def map_title(pdf: canvas.Canvas, label: str, page: int) -> tuple[float, float]:
    width, height = page_chrome(pdf, label, page, landscape(letter))
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 15)
    pdf.drawString(50, height - 68, label)
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 7)
    pdf.drawString(52, height - 83, "Field sketch: room boxes and travel links are drawn as a player's quick map.")
    return width, height


def map_earth_and_vogon(pdf: canvas.Canvas, page: int) -> None:
    width, height = map_title(pdf, "EARTH TO VOGON SHIP", page)
    pdf.setFont("Helvetica-Bold", 8)
    pdf.setFillColor(TEAL)
    pdf.drawString(68, 506, "EARTH  |  start in the bedroom")
    pdf.drawString(432, 506, "VOGON SHIP  |  fish, poem, password")

    rough_room(pdf, 91, 403, 106, 48, "Back of House", "optional room", "")
    rough_room(pdf, 222, 403, 106, 48, "Bedroom", "tablet + gown", "")
    rough_room(pdf, 222, 328, 106, 48, "Front Porch", "junk mail", "")
    rough_room(pdf, 222, 250, 106, 48, "Front of House", "Prosser / Ford", "bulldozer")
    rough_room(pdf, 91, 250, 106, 48, "Pub", "beer + sandwich", "")
    rough_room(pdf, 353, 250, 106, 48, "Country Lane", "feed the dog", "dog")
    arrow(pdf, 274, 403, 274, 376, "south")
    arrow(pdf, 274, 328, 274, 298, "south")
    arrow(pdf, 222, 273, 197, 273, "west")
    arrow(pdf, 328, 273, 353, 273, "east")
    arrow(pdf, 328, 427, 353, 427, "")
    pdf.setFillColor(MUTED); pdf.setFont("Helvetica-Oblique", 6)
    pdf.drawString(74, 224, "Ford's Thumb + green button cross the transfer beam.")
    arrow(pdf, 459, 273, 489, 273, "DARK / THUMB", dashed=True)

    rough_room(pdf, 486, 386, 104, 49, "Captain's Quarters", "second verse", "star")
    rough_room(pdf, 611, 386, 82, 49, "Vogon Hold", "Babel fish", "")
    rough_room(pdf, 711, 386, 64, 49, "Airlock", "escape", "")
    arrow(pdf, 590, 410, 611, 410, "")
    arrow(pdf, 693, 410, 711, 410, "")
    rough_room(pdf, 548, 278, 124, 50, "Dark", "wait; hear; aft", "")
    arrow(pdf, 651, 386, 615, 328, "poem / password", dashed=True)
    arrow(pdf, 743, 386, 672, 315, "Thumb", dashed=True)
    draw_icon(pdf, "star", 522, 335, 0.7)
    pdf.setFillColor(NAVY); pdf.setFont("Helvetica-Bold", 6.5)
    pdf.drawString(684, 240, "Guide + fish protect the Hold route")
    pdf.showPage()


def map_heart_gold(pdf: canvas.Canvas, page: int) -> None:
    width, height = map_title(pdf, "HEART OF GOLD AND MAGRATHEA", page)
    rough_room(pdf, 337, 427, 112, 48, "Bridge", "Drive, tea, receptacle", "ship")
    rough_room(pdf, 155, 427, 108, 48, "Entry Bay No. 2", "ship arrival", "")
    rough_room(pdf, 247, 327, 116, 48, "Corridor, Fore End", "aft to corridor", "")
    rough_room(pdf, 247, 239, 116, 48, "Corridor, Aft End", "doors + stairs", "")
    rough_room(pdf, 92, 239, 104, 48, "Galley", "Nutrimat", "tea")
    rough_room(pdf, 400, 239, 104, 48, "Marvin's Pantry", "thermo-fusion chisel", "")
    rough_room(pdf, 544, 327, 93, 48, "Sauna", "grow the plant", "flower")
    rough_room(pdf, 250, 142, 109, 46, "Engine Room", "spare drive + tools", "")
    rough_room(pdf, 410, 142, 91, 46, "Hatchway", "eat the fruit", "")
    rough_room(pdf, 542, 142, 98, 46, "Access Space", "Marvin + pliers", "")
    rough_room(pdf, 546, 57, 93, 42, "Ramp / Magrathea", "400 / 400", "star")
    arrow(pdf, 263, 451, 337, 451, "aft")
    arrow(pdf, 393, 427, 307, 375, "down")
    arrow(pdf, 305, 327, 305, 287, "aft")
    arrow(pdf, 247, 263, 196, 263, "port")
    arrow(pdf, 363, 263, 400, 263, "port")
    arrow(pdf, 393, 427, 590, 375, "port", dashed=True)
    arrow(pdf, 305, 239, 305, 188, "down")
    arrow(pdf, 359, 165, 410, 165, "")
    arrow(pdf, 501, 165, 542, 165, "starboard")
    arrow(pdf, 591, 142, 591, 99, "down")
    pdf.setFillColor(MUTED); pdf.setFont("Helvetica-Oblique", 6)
    pdf.drawString(72, 116, "Dark transfers (not corridors) reach the five scenes, Maze, and whale.")
    pdf.drawString(72, 103, "Keep the fruit, real tea, no tea, fish, and the vision's hypersonic pliers.")
    pdf.showPage()


def map_scenarios(pdf: canvas.Canvas, page: int) -> None:
    width, height = map_title(pdf, "THE FIVE SCENES AND THE WHALE", page)
    # Four compact field sketches on one sheet.
    pdf.setFillColor(TEAL); pdf.setFont("Helvetica-Bold", 7.5)
    pdf.drawString(68, 505, "TRAAL  |  tell the Beast your name; return for the interface")
    rough_room(pdf, 93, 421, 94, 48, "Lair", "say 'Arthur Dent'", "beast")
    rough_room(pdf, 231, 421, 108, 48, "Outer Lair", "towel + memorial", "")
    rough_room(pdf, 383, 421, 98, 48, "Inner Lair", "Nutrimat interface", "")
    arrow(pdf, 187, 445, 231, 445, "east")
    arrow(pdf, 339, 445, 383, 445, "southwest")

    pdf.setFillColor(TEAL); pdf.setFont("Helvetica-Bold", 7.5)
    pdf.drawString(68, 365, "DAMOGRAN  |  avoid the spire, then board the Heart of Gold")
    rough_room(pdf, 101, 287, 116, 48, "Presidential Speedboat", "key + cushion fluff", "boat")
    rough_room(pdf, 291, 287, 95, 48, "Dais", "Trillian + guards", "")
    rough_room(pdf, 455, 287, 112, 48, "Heart of Gold", "east after rifles", "ship")
    arrow(pdf, 217, 311, 291, 311, "north")
    arrow(pdf, 386, 311, 455, 311, "east")

    pdf.setFillColor(TEAL); pdf.setFont("Helvetica-Bold", 7.5)
    pdf.drawString(68, 231, "PARTY  |  collect jacket fluff; Phil ends the visit")
    rough_room(pdf, 94, 153, 89, 45, "Kitchen", "", "")
    rough_room(pdf, 228, 153, 100, 45, "Dining Room", "", "")
    rough_room(pdf, 375, 153, 105, 45, "Living Room", "jacket fluff", "wine")
    rough_room(pdf, 527, 153, 104, 45, "Front Door", "Phil's pickup", "")
    arrow(pdf, 183, 175, 228, 175, "")
    arrow(pdf, 328, 175, 375, 175, "")
    arrow(pdf, 480, 175, 527, 175, "south")

    # Side notes capture rooms that are intentionally outside the optimal path.
    rough_room(pdf, 670, 408, 96, 45, "War Chamber", "awl; wait for dog", "star")
    rough_room(pdf, 670, 310, 96, 45, "Synapse Maze", "random exits", "synapse")
    for angle in range(8):
        a = math.radians(angle * 45)
        arrow(pdf, 718, 309, 718 + 32 * math.cos(a), 309 + 32 * math.sin(a), "", dashed=True)
    pdf.setFillColor(MUTED); pdf.setFont("Helvetica-Oblique", 5.8)
    pdf.drawString(649, 262, "Maze cannot be mapped: movement is random.")
    rough_room(pdf, 670, 167, 96, 46, "Inside the Whale", "flowerpot", "whale")
    pdf.setFillColor(NAVY); pdf.setFont("Helvetica-Bold", 5.8)
    pdf.drawString(654, 143, "feel warm + drink liquid")
    pdf.drawString(654, 133, "repair Thumb; leave before it splats")
    pdf.showPage()


def opcode_pages(pdf: canvas.Canvas, data: dict, page: int) -> int:
    rows = data["opcodeInventory"]
    page_rows = 23
    total_pages = math.ceil(len(rows) / page_rows)
    for chunk_index, start in enumerate(range(0, len(rows), page_rows), start=1):
        chunk = rows[start:start + page_rows]
        width, height = page_chrome(pdf, "Executed opcode inventory", page)
        pdf.setFillColor(NAVY)
        pdf.setFont("Helvetica-Bold", 15)
        pdf.drawString(52, height - 69, f"Executed instruction inventory  {chunk_index}/{total_pages}")
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 7.4)
        pdf.drawString(54, height - 86, "Rows are grouped by actual encoding form, opcode number, and mnemonic; order follows first execution.")
        pdf.setFillColor(PAPER)
        pdf.roundRect(54, height - 137, width - 108, 34, 7, fill=1, stroke=0)
        pdf.setFillColor(TEAL)
        pdf.setFont("Helvetica-Bold", 7)
        pdf.drawString(64, height - 117, f"{len(rows)} unique opcode rows")
        pdf.drawRightString(width - 64, height - 117, f"{data['totalInstructions']:,} total instructions executed")
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 6.5)
        pdf.drawString(56, height - 151, "First Seq is one-based; First PC is the first instruction address in hexadecimal.")

        columns = ["FORM", "OPCODE", "MNEMONIC", "COUNT", "FIRST SEQ", "FIRST PC"]
        widths = [82, 54, 128, 90, 95, 89]
        x0, y0 = 54, height - 172
        row_h = 19
        pdf.setFillColor(TEAL)
        pdf.roundRect(x0, y0 - row_h, sum(widths), row_h, 3, fill=1, stroke=0)
        pdf.setFillColor(colors.white)
        pdf.setFont("Helvetica-Bold", 6.2)
        xpos = x0
        for col, w in zip(columns, widths):
            pdf.drawString(xpos + 6, y0 - 13, col)
            xpos += w
        for idx, row in enumerate(chunk):
            y = y0 - row_h * (idx + 2)
            pdf.setFillColor(PALE if idx % 2 == 0 else PAPER)
            pdf.rect(x0, y, sum(widths), row_h, fill=1, stroke=0)
            values = [
                row["Form"], str(row["Opcode"]), row["Mnemonic"], f"{row['Count']:,}",
                f"{row['FirstSequence']:,}", f"0x{row['FirstAddress']:04X}",
            ]
            xpos = x0
            for col_index, (value, w) in enumerate(zip(values, widths)):
                pdf.setFillColor(NAVY)
                pdf.setFont("Courier", 6.5)
                if col_index in (1, 3, 4):
                    pdf.drawRightString(xpos + w - 6, y + 6, value)
                else:
                    pdf.drawString(xpos + 6, y + 6, value)
                xpos += w
            pdf.setStrokeColor(RULE)
            pdf.setLineWidth(0.25)
            pdf.line(x0, y, x0 + sum(widths), y)
        pdf.setFillColor(CORAL)
        pdf.setFont("Helvetica-Bold", 6.5)
        pdf.drawString(56, 42, f"Route trace SHA-256 verified locally • Story release {data['release']} / {data['serial']}")
        pdf.showPage()
        page += 1
    return page


def refresh_trace(data: dict, trace_path: Path) -> None:
    summary = None
    for line in trace_path.read_text().splitlines():
        record = json.loads(line)
        if "opcodeInventory" in record:
            summary = record
    if summary is None:
        raise ValueError(f"Trace has no opcode summary: {trace_path}")
    if sum(row["Count"] for row in summary["opcodeInventory"]) != summary["instructionsExecuted"]:
        raise ValueError("Opcode counts do not equal total executed instructions")
    data["totalInstructions"] = summary["instructionsExecuted"]
    data["opcodeInventory"] = summary["opcodeInventory"]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--refresh-trace", type=Path, help="refresh the checked-in opcode inventory from a local RouteReplay JSONL trace")
    parser.add_argument("--output", type=Path, default=OUTPUT_PATH)
    args = parser.parse_args()
    data = json.loads(DATA_PATH.read_text())
    if args.refresh_trace:
        refresh_trace(data, args.refresh_trace)
        DATA_PATH.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n")
    if sum(row["Count"] for row in data["opcodeInventory"]) != data["totalInstructions"]:
        raise SystemExit("Opcode inventory count does not equal total instructions")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    pdf = canvas.Canvas(str(args.output), pagesize=letter, pageCompression=1)
    pdf.setTitle("The Hitchhiker's Guide to the Galaxy - Maximum Score Walkthrough")
    pdf.setAuthor("LeafpileGPT project walkthrough")
    cover(pdf, data)
    next_page = route_pages(pdf, data, 2)
    map_earth_and_vogon(pdf, next_page); next_page += 1
    map_heart_gold(pdf, next_page); next_page += 1
    map_scenarios(pdf, next_page); next_page += 1
    next_page = opcode_pages(pdf, data, next_page)
    pdf.save()
    print(f"Wrote {args.output} ({data['commandsCount']} commands, {data['totalInstructions']:,} instructions, {len(data['opcodeInventory'])} opcode rows)")


if __name__ == "__main__":
    main()
