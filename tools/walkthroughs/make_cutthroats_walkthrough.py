#!/usr/bin/env python3
"""Build the Cutthroats Sao Vera guide from the committed route summary."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.pagesizes import letter
from reportlab.pdfbase.pdfmetrics import stringWidth
from reportlab.pdfgen import canvas


ROOT = Path(__file__).resolve().parents[2]
DATA_PATH = ROOT / "walkthroughs" / "source-data" / "cutthroats.json"
OUTPUT_PATH = ROOT / "walkthroughs" / "cutthroats-maximum-score.pdf"

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
    result: list[str] = []
    line = ""
    for word in text.split():
        candidate = word if not line else f"{line} {word}"
        if stringWidth(candidate, font, size) <= width:
            line = candidate
        else:
            if line:
                result.append(line)
            line = word
    if line:
        result.append(line)
    return result


def chrome(pdf: canvas.Canvas, section: str, page: int) -> tuple[float, float]:
    width, height = letter
    pdf.setPageSize(letter)
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
    pdf.drawString(36, 15, "CUTTHROATS  |  SAO VERA  |  250 / 250")
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
    width, height = chrome(pdf, "A field guide to the Sao Vera", 1)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 29)
    pdf.drawString(52, height - 91, "CUTTHROATS")
    pdf.setFillColor(CORAL)
    pdf.setFont("Helvetica-Bold", 14)
    pdf.drawString(54, height - 115, "Sao Vera maximum-score walkthrough")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 8.5)
    pdf.drawString(54, height - 133, "A fresh-start route from Hardscrabble Island to the recovered gold")

    pdf.setFillColor(CARD)
    pdf.roundRect(48, height - 226, width - 96, 75, 12, fill=1, stroke=0)
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 8)
    pdf.drawString(64, height - 169, "VERIFIED FINISH")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 23)
    pdf.drawString(64, height - 201, "250 / 250")
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica-Bold", 8)
    pdf.drawRightString(width - 64, height - 169,
                        f"{data['commandsCount']} commands  |  no player deaths")
    pdf.setFont("Helvetica", 7.5)
    pdf.drawRightString(width - 64, height - 189,
                        f"{data['totalInstructions']:,} executed instructions  |  seed {data['randomSeed']}")
    pdf.drawRightString(width - 64, height - 207,
                        f"{len(data['opcodeInventory'])} opcode rows  |  Release {data['release']}  |  Serial {data['serial']}")

    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 11)
    pdf.drawString(54, height - 252, "How to play this route")
    y = height - 270
    for text in data["intro"]:
        y = paragraph(pdf, text, 56, y, width - 112, size=7.5, leading=9)
        y -= 4

    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 10.5)
    pdf.drawString(54, y - 1, "Score checkpoints")
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
        pdf.setFont("Helvetica-Bold", 7.5)
        pdf.drawString(x + 8, top - 15, item["heading"])
        pdf.setFillColor(NAVY)
        pdf.setFont("Helvetica", 6.2)
        for j, line in enumerate(wrap(item["detail"], "Helvetica", 6.2, card_width - 16)[:2]):
            pdf.drawString(x + 8, top - 27 - j * 7, line)

    box_y = 287
    pdf.setFillColor(CARD)
    pdf.roundRect(54, box_y, width - 108, 40, 7, fill=1, stroke=0)
    pdf.setFillColor(TEAL)
    pdf.setFont("Helvetica-Bold", 7)
    pdf.drawString(64, box_y + 25, "ENDING VERIFIED")
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica", 6.6)
    for index, line in enumerate(wrap(data["endingVerification"], "Helvetica", 6.6, width - 178)[:2]):
        pdf.drawString(159, box_y + 25 - index * 8, line)
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 7)
    pdf.drawString(54, 264, "The field sketches cover the island, the Mary Margaret, and every room reached in the Sao Vera wreck.")
    pdf.drawString(54, 252, "The giant squid is asleep: pass through without looking at or touching it.")
    pdf.showPage()


def route_pages(pdf: canvas.Canvas, data: dict, start: int, page: int) -> None:
    commands = data["commands"]
    end = min(start + 76, len(commands))
    width, height = chrome(pdf, "Numbered route", page)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 16)
    pdf.drawString(52, height - 69, f"Commands {start + 1:03d}-{end:03d}")
    section = next((s for s in data["routeSections"] if s["start"] <= start + 1 <= s["end"]), None)
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 8)
    pdf.drawString(54, height - 86, section["caption"] if section else "Follow the numbered route from a fresh game.")
    y_top = height - 111
    col_width = (width - 108 - 24) / 2
    rows = 38
    for col in range(2):
        x = 54 + col * (col_width + 24)
        first = start + col * rows
        last = min(first + rows, end)
        y = y_top
        for idx in range(first, last):
            number = idx + 1
            cmd = commands[idx]
            pdf.setFillColor(TEAL)
            pdf.setFont("Helvetica-Bold", 6.7)
            pdf.drawRightString(x + 24, y, f"{number:03d}.")
            pdf.setFillColor(NAVY)
            pdf.setFont("Courier", 6.65)
            lines = wrap(cmd, "Courier", 6.65, col_width - 34)
            for line_index, line in enumerate(lines):
                pdf.drawString(x + 30, y - line_index * 8, line)
            y -= 12 + (len(lines) - 1) * 8
            for cp in data["routeNotes"].get(str(number), []):
                pdf.setFillColor(CORAL)
                pdf.setFont("Helvetica-Bold", 5.9)
                pdf.drawString(x + 30, y + 2, cp)
                y -= 8
        pdf.setStrokeColor(RULE)
        pdf.line(x + col_width + 10, 70, x + col_width + 10, height - 104)
    pdf.showPage()


def rough_box(pdf: canvas.Canvas, x: float, y: float, w: float, h: float,
              label: str, detail: str = "", icon: str = "") -> None:
    pdf.saveState()
    pdf.setStrokeColor(PENCIL)
    pdf.setLineWidth(0.9)
    # Two slightly irregular passes make the route cards feel hand-sketched.
    for dx, dy in [(0, 0), (1.1, -0.7)]:
        p = pdf.beginPath()
        p.moveTo(x + dx + 2, y + dy + 1)
        p.lineTo(x + dx + w - 2, y + dy + 2)
        p.lineTo(x + dx + w - 1, y + dy + h - 2)
        p.lineTo(x + dx + w / 2 + 1, y + dy + h)
        p.lineTo(x + dx + 1, y + dy + h - 1)
        p.close()
        pdf.drawPath(p, fill=0, stroke=1)
    pdf.setFillColor(CORAL)
    pdf.circle(x + 10, y + h - 11, 2.2, fill=1, stroke=0)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 7.3)
    for i, line in enumerate(wrap(label, "Helvetica-Bold", 7.3, w - 24)[:2]):
        pdf.drawString(x + 18, y + h - 17 - i * 9, line)
    if detail:
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 5.8)
        for i, line in enumerate(wrap(detail, "Helvetica", 5.8, w - 16)[:3]):
            pdf.drawString(x + 8, y + 13 - i * 7, line)
    if icon:
        draw_icon(pdf, icon, x + w - 19, y + h - 18)
    pdf.restoreState()


def draw_icon(pdf: canvas.Canvas, kind: str, x: float, y: float) -> None:
    pdf.saveState()
    pdf.setStrokeColor(TEAL)
    pdf.setLineWidth(1)
    if kind == "bed":
        pdf.line(x - 9, y - 6, x + 9, y - 6)
        pdf.line(x - 8, y - 6, x - 8, y + 5)
        pdf.line(x + 8, y - 6, x + 8, y + 1)
        pdf.line(x - 7, y, x + 7, y)
        pdf.circle(x - 4, y + 3, 2, fill=0, stroke=1)
    elif kind == "light":
        pdf.rect(x - 5, y - 9, 10, 17, fill=0, stroke=1)
        pdf.line(x - 7, y + 8, x + 7, y + 8)
        pdf.line(x - 5, y + 11, x + 5, y + 11)
        for dx in [-10, 0, 10]:
            pdf.line(x + dx * .4, y + 13, x + dx, y + 17)
    elif kind == "coin":
        pdf.circle(x, y, 8, fill=0, stroke=1)
        pdf.circle(x, y, 5, fill=0, stroke=1)
        pdf.setFont("Helvetica-Bold", 5)
        pdf.drawCentredString(x, y - 1.8, "S")
    elif kind == "store":
        pdf.rect(x - 9, y - 8, 18, 12, fill=0, stroke=1)
        for i in range(3):
            pdf.line(x - 9 + i * 6, y + 4, x - 6 + i * 6, y + 9)
        pdf.line(x - 9, y + 9, x + 9, y + 9)
        pdf.rect(x - 2, y - 8, 5, 7, fill=0, stroke=1)
    elif kind == "boat":
        p = pdf.beginPath()
        p.moveTo(x - 11, y - 6); p.lineTo(x + 11, y - 6); p.lineTo(x + 7, y - 11); p.lineTo(x - 7, y - 11); p.close()
        pdf.drawPath(p, fill=0, stroke=1)
        pdf.line(x, y - 6, x, y + 9)
        pdf.line(x, y + 8, x + 7, y + 1)
    elif kind == "fish":
        pdf.ellipse(x - 9, y - 4, x + 6, y + 4, fill=0, stroke=1)
        p = pdf.beginPath(); p.moveTo(x + 6, y); p.lineTo(x + 12, y + 5); p.lineTo(x + 12, y - 5); p.close()
        pdf.drawPath(p, fill=0, stroke=1)
        pdf.circle(x - 5, y + 1, 0.7, fill=1, stroke=0)
    elif kind == "treasure":
        pdf.rect(x - 9, y - 7, 18, 12, fill=0, stroke=1)
        pdf.line(x - 10, y + 5, x + 10, y + 5)
        pdf.arc(x - 8, y + 1, x + 8, y + 12, 0, 180)
        pdf.circle(x, y, 1.2, fill=1, stroke=0)
    pdf.restoreState()


def arrow(pdf: canvas.Canvas, x1: float, y1: float, x2: float, y2: float,
          label: str = "", selected: bool = False) -> None:
    pdf.saveState()
    color = CORAL if selected else PENCIL
    pdf.setStrokeColor(color)
    pdf.setFillColor(color)
    pdf.setLineWidth(1.35 if selected else .85)
    pdf.setDash(3, 2) if not selected else pdf.setDash()
    # Slightly bowed paths keep the diagram lively and distinguish crossing links.
    midx, midy = (x1 + x2) / 2, (y1 + y2) / 2
    bend = 4 if (int(x1 + y1 + x2 + y2) % 2 == 0) else -4
    p = pdf.beginPath(); p.moveTo(x1, y1); p.curveTo(midx + bend, midy + bend, midx - bend, midy - bend, x2, y2)
    pdf.drawPath(p, fill=0, stroke=1)
    import math
    angle = math.atan2(y2 - midy, x2 - midx)
    for offset in [-.45, .45]:
        a = angle + math.pi + offset
        pdf.line(x2, y2, x2 + 5 * math.cos(a), y2 + 5 * math.sin(a))
    if label:
        pdf.setDash()
        pdf.setFont("Helvetica-Bold", 5.7)
        pdf.drawCentredString(midx, midy + 4, label.upper())
    pdf.restoreState()


def map_page(pdf: canvas.Canvas, title: str, subtitle: str, page: int,
             nodes: list[dict], links: list[tuple[int, int, str, bool]], note: str) -> None:
    width, height = chrome(pdf, "Hand-sketched route map", page)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", 16)
    pdf.drawString(42, height - 67, title)
    pdf.setFillColor(MUTED)
    pdf.setFont("Helvetica", 7.5)
    pdf.drawString(44, height - 83, subtitle)
    # Links are painted first so room boxes stay crisp and readable.
    for a, b, label, selected in links:
        n1, n2 = nodes[a], nodes[b]
        if label == "port / starboard":
            # The port-hole route wraps below the lower deck, keeping it clear
            # of the ladder-to-ocean-floor connection in the sketch.
            start_x = n1["x"] + n1["w"]
            start_y = n1["y"] + n1["h"] / 2
            target_x = n2["x"] + n2["w"] / 2
            target_y = n2["y"]
            pdf.saveState()
            pdf.setStrokeColor(CORAL if selected else PENCIL)
            pdf.setFillColor(CORAL if selected else PENCIL)
            pdf.setLineWidth(1.35 if selected else .85)
            p = pdf.beginPath()
            p.moveTo(start_x, start_y)
            p.lineTo(405, start_y)
            p.lineTo(405, 184)
            p.lineTo(target_x, 184)
            p.lineTo(target_x, target_y)
            pdf.drawPath(p, fill=0, stroke=1)
            import math
            angle = math.pi / 2
            for offset in [-.45, .45]:
                a = angle + math.pi + offset
                pdf.line(target_x, target_y, target_x + 5 * math.cos(a), target_y + 5 * math.sin(a))
            pdf.setFont("Helvetica-Bold", 5.7)
            pdf.drawCentredString(453, 188, label.upper())
            pdf.restoreState()
            continue
        x1 = n1["x"] + n1["w"] / 2; y1 = n1["y"] + n1["h"] / 2
        x2 = n2["x"] + n2["w"] / 2; y2 = n2["y"] + n2["h"] / 2
        dx, dy = x2 - x1, y2 - y1
        scale = min((n1["w"] / 2 - 2) / max(1, abs(dx)), (n1["h"] / 2 - 2) / max(1, abs(dy)))
        scale = min(1, scale)
        scale2 = min((n2["w"] / 2 - 2) / max(1, abs(dx)), (n2["h"] / 2 - 2) / max(1, abs(dy)))
        scale2 = min(1, scale2)
        arrow(pdf, x1 + dx * scale, y1 + dy * scale, x2 - dx * scale2, y2 - dy * scale2, label, selected)
    for node in nodes:
        rough_box(pdf, node["x"], node["y"], node["w"], node["h"], node["label"], node.get("detail", ""), node.get("icon", ""))
    pdf.setFillColor(CARD)
    pdf.roundRect(42, 42, width - 84, 38, 8, fill=1, stroke=0)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica", 6.5)
    for i, line in enumerate(wrap(note, "Helvetica", 6.5, width - 104)[:2]):
        pdf.drawString(52, 64 - i * 8, line)
    pdf.showPage()


def maps(pdf: canvas.Canvas, data: dict, first_page: int) -> None:
    island = [
        {"x": 48, "y": 580, "w": 118, "h": 57, "label": "Your Room", "detail": "Red Boar Inn • note and key", "icon": "bed"},
        {"x": 240, "y": 580, "w": 132, "h": 57, "label": "The Shanty", "detail": "Breakfast • meet Johnny", "icon": "store"},
        {"x": 430, "y": 580, "w": 132, "h": 57, "label": "Lighthouse", "detail": "9:30 meeting", "icon": "light"},
        {"x": 240, "y": 475, "w": 132, "h": 57, "label": "Wharf Road", "detail": "Inn • shops • waterfront"},
        {"x": 48, "y": 475, "w": 132, "h": 57, "label": "Mariners' Trust", "detail": "Withdraw $500", "icon": "coin"},
        {"x": 430, "y": 475, "w": 132, "h": 57, "label": "Outfitters", "detail": "Flashlight • repellent", "icon": "store"},
        {"x": 48, "y": 370, "w": 132, "h": 57, "label": "Point Lookout", "detail": "Johnny's cash test"},
        {"x": 240, "y": 370, "w": 132, "h": 57, "label": "Shore Road", "detail": "Road west to Ferry Landing"},
        {"x": 430, "y": 370, "w": 132, "h": 57, "label": "McGinty's Office", "detail": "Enter from window"},
        {"x": 48, "y": 265, "w": 132, "h": 57, "label": "Ferry Landing", "detail": "McGinty • Weasel handoff", "icon": "boat"},
        {"x": 240, "y": 265, "w": 132, "h": 57, "label": "Vacant Lot", "detail": "Wait for McGinty to leave"},
        {"x": 430, "y": 265, "w": 132, "h": 57, "label": "Back Alley", "detail": "Office window access"},
    ]
    island_links = [(0,1,"east / in",True),(1,3,"out",True),(3,2,"SW, NW",True),(3,4,"SE / north",True),
                    (4,6,"south",True),(6,7,"SE",False),(7,5,"road",True),(5,3,"out",True),
                    (3,7,"south",False),(7,9,"west",True),(9,10,"east",False),(10,11,"west",True),(11,8,"window",True)]
    map_page(pdf, "Hardscrabble Island", "Street paths are simplified; red arrows trace the meetings, cash and envelope route.", first_page,
             island, island_links, "The route uses a deterministic seed that makes Johnny's clue the Sao Vera coin. Wait through the lighthouse meeting and let McGinty leave his office before entering the window.")

    boat = [
        {"x": 48, "y": 574, "w": 128, "h": 58, "label": "Wheelhouse", "detail": "Status • navigation"},
        {"x": 242, "y": 574, "w": 128, "h": 58, "label": "Aft Deck", "detail": "Compressor • 600-ft hose", "icon": "boat"},
        {"x": 432, "y": 574, "w": 128, "h": 58, "label": "Fore Deck", "detail": "Boat arrival"},
        {"x": 432, "y": 466, "w": 128, "h": 58, "label": "Crew Quarters", "detail": "Bunk • envelope hidden below it", "icon": "bed"},
        {"x": 242, "y": 466, "w": 128, "h": 58, "label": "Storage Locker", "detail": "Deep-sea suit • machine"},
        {"x": 432, "y": 358, "w": 128, "h": 58, "label": "Galley", "detail": "Stew • water"},
        {"x": 432, "y": 250, "w": 128, "h": 58, "label": "Lounge", "detail": "Passage aft"},
        {"x": 242, "y": 250, "w": 128, "h": 58, "label": "Captain's Cabin", "detail": "Give envelope to Johnny"},
    ]
    boat_links = [(0,1,"aft",True),(0,2,"fore",False),(2,3,"down",True),(3,4,"north",True),
                  (3,5,"south",True),(5,6,"south / north",True),(6,7,"south / north",True)]
    map_page(pdf, "The Mary Margaret", "Deck connections and the crew sequence before the Sao Vera dive.", first_page + 1,
             boat, boat_links, "Hide the envelope under the bunk, wait for delivery, then set latitude 40 and longitude 45. Eat and drink before sleeping. After arrival, expose the Weasel; from the Galley go up to the Wheelhouse, then aft to the compressor.")

    wreck = [
        {"x": 230, "y": 645, "w": 152, "h": 46, "label": "Top Deck", "detail": "Bow • hole in deck", "icon": "boat"},
        {"x": 230, "y": 558, "w": 152, "h": 46, "label": "Mast Cabin", "detail": "Broken ladder • rope above"},
        {"x": 230, "y": 471, "w": 152, "h": 46, "label": "Iron-Bar Room", "detail": "Take an iron bar"},
        {"x": 230, "y": 384, "w": 152, "h": 46, "label": "Middle-Deck Galley", "detail": "Cask in the wreckage"},
        {"x": 430, "y": 384, "w": 142, "h": 46, "label": "Crew Bunk Cabin", "detail": "Bunks block the aft doorway"},
        {"x": 430, "y": 297, "w": 142, "h": 46, "label": "Bunk Passage", "detail": "Ladder down through deck"},
        {"x": 230, "y": 210, "w": 152, "h": 46, "label": "Lower Ladder Cabin", "detail": "Broken bottom rungs"},
        {"x": 48, "y": 210, "w": 142, "h": 46, "label": "Sleeping Squid", "detail": "Walk straight through", "icon": "fish"},
        {"x": 48, "y": 297, "w": 142, "h": 46, "label": "Oak-Chest Hold", "detail": "Port hole to ocean floor", "icon": "treasure"},
        {"x": 48, "y": 384, "w": 142, "h": 46, "label": "Skeleton Lounge", "detail": "Scabbard holds sword"},
        {"x": 48, "y": 471, "w": 142, "h": 46, "label": "Maple-Chest Cabin", "detail": "Push chest aft"},
        {"x": 430, "y": 210, "w": 142, "h": 46, "label": "Ocean Floor", "detail": "Wait for orange line"},
    ]
    wreck_links = [(0,1,"down / up rope",True),(1,2,"south / cask north",True),
                   (2,3,"south / cask north",True),(3,4,"south",True),
                   (4,5,"south",True),(5,6,"down / chest up",True),
                   (6,7,"north",True),(7,8,"north / chest south",True),
                   (8,9,"north / chest south",True),(9,10,"north / chest south",True),
                   (8,11,"port / starboard",True)]
    map_page(pdf, "The Sao Vera", "A room-by-room sketch of every wreck space entered on the 250-point route.", first_page + 2,
             wreck, wreck_links, "The sketch is not to scale; labels are the game's compass directions. The oak chest exits through the port hole and rises on the orange line. Return starboard; move the maple chest south past the sleeping squid to climb the broken ladder. Roll the cask to the mast, cut the rope, then ascend to the Mary Margaret.")


def opcode_pages(pdf: canvas.Canvas, data: dict, start_page: int) -> None:
    rows = data["opcodeInventory"]
    per_page = 22
    for offset in range(0, len(rows), per_page):
        page = start_page + offset // per_page
        width, height = chrome(pdf, "Executed opcode inventory", page)
        pdf.setFillColor(NAVY)
        pdf.setFont("Helvetica-Bold", 15)
        pdf.drawString(42, height - 68, f"Opcode table {offset // per_page + 1} of {(len(rows) + per_page - 1) // per_page}")
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 7)
        pdf.drawString(44, height - 84, "Rows are ordered by first appearance in the successful fresh replay.")
        pdf.setFillColor(CARD)
        pdf.roundRect(42, height - 111, width - 84, 20, 6, fill=1, stroke=0)
        pdf.setFillColor(TEAL)
        pdf.setFont("Helvetica-Bold", 7)
        pdf.drawString(51, height - 104, f"{len(rows)} unique form/opcode/mnemonic rows")
        pdf.drawRightString(width - 51, height - 104, f"{data['totalInstructions']:,} total instructions executed")
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 6.2)
        pdf.drawString(44, height - 124, "First Seq is one-based execution order; First PC is the address in hexadecimal.")

        x0, table_top = 42, height - 145
        widths = [87, 52, 123, 68, 79, 77]
        headers = ["FORM", "OPCODE", "MNEMONIC", "COUNT", "FIRST SEQ", "FIRST PC"]
        row_h = 23
        x = x0
        pdf.setFillColor(TEAL)
        pdf.roundRect(x0, table_top - row_h, sum(widths), row_h, 5, fill=1, stroke=0)
        pdf.setFillColor(colors.white)
        pdf.setFont("Helvetica-Bold", 6.3)
        for header, w in zip(headers, widths):
            pdf.drawString(x + 7, table_top - 15, header)
            x += w
        y = table_top - row_h
        for row_index, row in enumerate(rows[offset:offset + per_page]):
            y -= row_h
            pdf.setFillColor(PALE if row_index % 2 else CARD)
            pdf.rect(x0, y, sum(widths), row_h, fill=1, stroke=0)
            values = [row["Form"], str(row["Opcode"]), row["Mnemonic"], f"{row['Count']:,}",
                      f"{row['FirstSequence']:,}", f"0x{row['FirstAddress']:x}"]
            x = x0
            pdf.setFillColor(NAVY)
            pdf.setFont("Courier", 6.45)
            for value, w in zip(values, widths):
                pdf.drawString(x + 7, y + 8, value)
                x += w
        pdf.setFillColor(MUTED)
        pdf.setFont("Helvetica", 6)
        pdf.drawRightString(width - 43, 41, f"Inventory count check: {sum(r['Count'] for r in rows):,} = {data['totalInstructions']:,} instructions.")
        pdf.showPage()


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
    DATA_PATH.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--refresh-trace", type=Path)
    args = parser.parse_args()
    if args.refresh_trace:
        refresh_trace(args.refresh_trace)
    data = json.loads(DATA_PATH.read_text())
    if sum(row["Count"] for row in data["opcodeInventory"]) != data["totalInstructions"]:
        raise ValueError("Opcode counts do not sum to the instruction total")
    pdf = canvas.Canvas(str(OUTPUT_PATH), pagesize=letter, pageCompression=1)
    cover(pdf, data)
    for start, page in zip(range(0, len(data["commands"]), 76), range(2, 2 + (len(data["commands"]) + 75) // 76)):
        route_pages(pdf, data, start, page)
    map_start = 2 + (len(data["commands"]) + 75) // 76
    maps(pdf, data, map_start)
    opcode_start = map_start + 3
    opcode_pages(pdf, data, opcode_start)
    pdf.save()
    print(f"Wrote {OUTPUT_PATH}")


if __name__ == "__main__":
    main()
