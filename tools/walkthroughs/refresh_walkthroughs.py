#!/usr/bin/env python3
"""Rebuild trace-derived opcode appendices and Starcross-style PDF frames.

The immutable source guides preserve commands, prose, and maps. Opcode inventory
data is checked in under walkthroughs/source-data and can be refreshed from a
RouteReplay JSONL trace with --refresh-traces.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import tempfile
from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT, TA_RIGHT
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import ParagraphStyle
from reportlab.platypus import BaseDocTemplate, Frame, PageTemplate, Paragraph, Spacer, Table, TableStyle


ROOT = Path(__file__).resolve().parents[2]
DATA_DIR = ROOT / "walkthroughs" / "source-data"
GUIDES = ("zork1", "zork2", "deadline", "zork3")

CREAM = colors.HexColor("#F6F2E8")
TEAL = colors.HexColor("#247F7C")
NAVY = colors.HexColor("#153F4B")
MUTED = colors.HexColor("#637B80")
PALE = colors.HexColor("#E6ECE4")
PAPER = colors.HexColor("#FFFCF5")


def refresh_data(slug: str, trace_path: Path) -> None:
    data_path = DATA_DIR / f"{slug}.json"
    data = json.loads(data_path.read_text())
    rows = []
    for line in trace_path.read_text().splitlines():
        record = json.loads(line)
        if "opcodeInventory" in record:
            rows = record["opcodeInventory"]
            total = record["instructionsExecuted"]
    if not rows:
        raise ValueError(f"Trace has no opcode summary: {trace_path}")
    if sum(row["Count"] for row in rows) != total:
        raise ValueError(f"Opcode counts do not sum to {total}: {trace_path}")
    data["totalInstructions"] = total
    data["opcodeInventory"] = rows
    data_path.write_text(json.dumps(data, indent=2) + "\n")


def make_inventory_pdf(data: dict, target: Path) -> None:
    page_w, page_h = letter
    styles = {
        "title": ParagraphStyle("title", fontName="Helvetica-Bold", fontSize=19,
                                leading=22, textColor=NAVY, spaceAfter=4),
        "subtitle": ParagraphStyle("subtitle", fontName="Helvetica", fontSize=9,
                                   leading=12, textColor=MUTED),
        "summary": ParagraphStyle("summary", fontName="Helvetica-Bold", fontSize=8,
                                  leading=10, textColor=TEAL),
        "note": ParagraphStyle("note", fontName="Helvetica", fontSize=7,
                               leading=9, textColor=MUTED),
        "header": ParagraphStyle("header", fontName="Helvetica-Bold", fontSize=7,
                                 leading=8, textColor=colors.white, alignment=TA_LEFT),
        "cell": ParagraphStyle("cell", fontName="Courier", fontSize=7.1,
                               leading=9, textColor=NAVY),
        "number": ParagraphStyle("number", fontName="Courier", fontSize=7.1,
                                 leading=9, textColor=NAVY, alignment=TA_RIGHT),
    }

    def on_page(canvas, doc):
        canvas.saveState()
        canvas.setFillColor(CREAM)
        canvas.rect(0, 0, page_w, page_h, fill=1, stroke=0)
        canvas.setStrokeColor(colors.HexColor("#D9D4C8"))
        canvas.setLineWidth(0.35)
        for y in range(42, int(page_h), 28):
            canvas.line(0, y, page_w, y)
        canvas.setFillColor(TEAL)
        canvas.roundRect(34, page_h - 39, page_w - 68, 34, 12, fill=1, stroke=0)
        canvas.setFillColor(colors.white)
        canvas.setFont("Helvetica-Bold", 8)
        canvas.drawString(48, page_h - 25, "EXECUTED OPCODE INVENTORY")
        canvas.setStrokeColor(TEAL)
        canvas.setLineWidth(0.8)
        canvas.line(34, 27, page_w - 34, 27)
        canvas.setFillColor(MUTED)
        canvas.setFont("Helvetica-Bold", 6.5)
        canvas.drawString(36, 15, f"{data['title']}  |  {data['routeTitle']}")
        canvas.drawRightString(page_w - 36, 15, f"OPCODES  |  {doc.page:02d}")
        canvas.restoreState()

    frame = Frame(38, 40, page_w - 76, page_h - 94,
                  leftPadding=10, rightPadding=10, topPadding=8, bottomPadding=4,
                  id="inventory")
    doc = BaseDocTemplate(str(target), pagesize=letter, leftMargin=38,
                          rightMargin=38, topMargin=46, bottomMargin=40,
                          title=f"{data['title']} executed opcode inventory")
    doc.addPageTemplates(PageTemplate(id="inventory", frames=[frame], onPage=on_page))
    story = []
    rows = data["opcodeInventory"]
    chunks = [rows[i:i + 22] for i in range(0, len(rows), 22)]
    for index, chunk in enumerate(chunks, start=1):
        story.append(Paragraph(f"Opcode table {index} of {len(chunks)}", styles["title"]))
        story.append(Paragraph("Rows are ordered by first appearance in the successful fresh replay.", styles["subtitle"]))
        story.append(Spacer(1, 10))
        summary = Table([[Paragraph(f"{len(rows)} unique form/opcode/mnemonic rows", styles["summary"]),
                          Paragraph(f"{data['totalInstructions']:,} total instructions executed", ParagraphStyle(
                              "sumright", parent=styles["summary"], alignment=TA_RIGHT))]],
                        colWidths=[250, 260])
        summary.setStyle(TableStyle([
            ("BACKGROUND", (0, 0), (-1, -1), PAPER), ("BOX", (0, 0), (-1, -1), 0, PAPER),
            ("LEFTPADDING", (0, 0), (-1, -1), 10), ("RIGHTPADDING", (0, 0), (-1, -1), 10),
            ("TOPPADDING", (0, 0), (-1, -1), 7), ("BOTTOMPADDING", (0, 0), (-1, -1), 7),
        ]))
        story += [summary, Spacer(1, 7), Paragraph(
            "First Seq is one-based execution order; First PC is the instruction address in hexadecimal.",
            styles["note"]), Spacer(1, 13)]
        table_rows = [[Paragraph(s, styles["header"]) for s in
                       ("FORM", "OPCODE", "MNEMONIC", "COUNT", "FIRST SEQ", "FIRST PC")]]
        for row in chunk:
            table_rows.append([
                Paragraph(row["Form"], styles["cell"]),
                Paragraph(str(row["Opcode"]), styles["number"]),
                Paragraph(row["Mnemonic"], styles["cell"]),
                Paragraph(f"{row['Count']:,}", styles["number"]),
                Paragraph(f"{row['FirstSequence']:,}", styles["number"]),
                Paragraph(f"0x{row['FirstAddress']:04X}", styles["cell"]),
            ])
        table = Table(table_rows, colWidths=[76, 56, 110, 74, 92, 86], repeatRows=1)
        commands = [
            ("BACKGROUND", (0, 0), (-1, 0), TEAL),
            ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
            ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
            ("LEFTPADDING", (0, 0), (-1, -1), 7),
            ("RIGHTPADDING", (0, 0), (-1, -1), 7),
            ("TOPPADDING", (0, 0), (-1, -1), 4),
            ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
            ("LINEBELOW", (0, 0), (-1, 0), 0.6, TEAL),
        ]
        for row_index in range(1, len(table_rows)):
            commands.append(("BACKGROUND", (0, row_index), (-1, row_index),
                             PALE if row_index % 2 else PAPER))
        table.setStyle(TableStyle(commands))
        story.append(table)
        if index < len(chunks):
            from reportlab.platypus import PageBreak
            story.append(PageBreak())
    doc.build(story)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--refresh-traces", nargs=2, action="append", metavar=("GUIDE", "TRACE"),
                        help="refresh checked-in inventory data from a local RouteReplay JSONL trace")
    args = parser.parse_args()
    for entry in args.refresh_traces or []:
        slug, trace = entry
        if slug not in GUIDES:
            raise SystemExit(f"Unknown guide: {slug}")
        refresh_data(slug, Path(trace))

    with tempfile.TemporaryDirectory(prefix="leafpile-walkthrough-") as temp:
        temp_path = Path(temp)
        for slug in GUIDES:
            data_path = DATA_DIR / f"{slug}.json"
            data = json.loads(data_path.read_text())
            if sum(row["Count"] for row in data["opcodeInventory"]) != data["totalInstructions"]:
                raise SystemExit(f"Opcode counts do not sum to total for {slug}")
            tables = temp_path / f"{slug}-inventory.pdf"
            make_inventory_pdf(data, tables)
            output = ROOT / "walkthroughs" / f"{slug}-maximum-score.pdf"
            rebuilt = temp_path / f"{slug}-rebuilt.pdf"
            swift = ROOT / "tools" / "walkthroughs" / "apply_starcross_frame.swift"
            swift_env = os.environ.copy()
            swift_env["CLANG_MODULE_CACHE_PATH"] = str(temp_path / "clang-module-cache")
            swift_env["SWIFT_MODULECACHE_PATH"] = str(temp_path / "swift-module-cache")
            subprocess.run([
                "swift", "-module-cache-path", str(temp_path / "swift-module-cache"),
                str(swift), str(ROOT / "walkthroughs" / "source-pdfs" / f"{slug}-maximum-score.pdf"),
                str(tables), str(rebuilt), ",".join(map(str, data["replacePages"])),
                f"{data['title']}  |  {data['routeTitle']}",
            ], check=True, env=swift_env)
            rebuilt.replace(output)
            print(f"Updated {output.relative_to(ROOT)}: {len(data['opcodeInventory'])} rows, "
                  f"{data['totalInstructions']:,} instructions")


if __name__ == "__main__":
    main()
