# Walkthrough PDF refresh

`refresh_walkthroughs.py` rebuilds the Zork I, Zork II, Deadline, and Zork III
guides using the immutable pages in `walkthroughs/source-pdfs/`, the checked-in
trace summaries in `walkthroughs/source-data/`, and the Starcross-style opcode
table and page frame. The source PDFs preserve each route, explanatory text, and
hand-drawn map; the generated PDFs replace only their previous opcode appendix.

To reproduce the PDFs from the committed source data on macOS, install Python 3,
ReportLab (`python3 -m pip install reportlab`), and the Swift command line tools,
then run:

```sh
python3 tools/walkthroughs/refresh_walkthroughs.py
```

To refresh counts from new clean replays, generate a JSONL trace for each local
story with `Leafpile.RouteReplay`, then pass each trace to the script:

```sh
dotnet run --project tools/Leafpile.RouteReplay -- stories/zork1.z3 \
  .agent/regression-routes/zork1-maximum-score.json /tmp/zork1-trace.jsonl
python3 tools/walkthroughs/refresh_walkthroughs.py \
  --refresh-traces zork1 /tmp/zork1-trace.jsonl
```

Repeat the replay and option for `zork2`, `deadline`, and `zork3`. Story files,
routes, and raw traces remain local. The generator checks that the opcode counts
sum to the total instruction count before writing each guide.

## Suspended no-death guide

The Suspended guide has its own ReportLab generator and committed source data:

```sh
python3 tools/walkthroughs/make_suspended_walkthrough.py
```

To refresh its opcode table after a clean local replay, run the ignored route and
trace, then pass the trace to the generator:

```sh
dotnet run --project tools/Leafpile.RouteReplay -- stories/suspend.z3 \
  .agent/regression-routes/suspended-no-death.json \
  .agent/regression-routes/suspended-no-death.trace.jsonl
python3 tools/walkthroughs/make_suspended_walkthrough.py \
  --refresh-trace .agent/regression-routes/suspended-no-death.trace.jsonl
```

The route uses no numeric score global; it validates casualty checkpoints from
the story text and tracks the current robot's parent room as its location. Keep
the Suspended story, manual, hints, route, and raw trace local.
