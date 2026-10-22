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

## Witness conviction guide

`make_witness_walkthrough.py` rebuilds the Witness guide, including the numbered
route, two hand-sketched room maps, and the six-column opcode inventory. Its
committed data lives in `walkthroughs/source-data/witness.json`.

After a fresh local replay, refresh the inventory from the ignored trace and
rebuild the PDF with:

```sh
dotnet run --project tools/Leafpile.RouteReplay -- stories/witness.z3 \
  .agent/regression-routes/witness-no-death.json \
  .agent/regression-routes/witness-no-death-trace.jsonl
python3 tools/walkthroughs/make_witness_walkthrough.py \
  --refresh-trace .agent/regression-routes/witness-no-death-trace.jsonl
```

The fresh route ends with the police-chief letter confirming Monica Linder's
conviction. Witness exposes no numeric score global, so the guide verifies the
best-ending result instead. The story, manual, hints, route, saves, and trace
stay local and remain unnecessary for CI.

## Enchanter maximum-score guide

The Enchanter guide is generated from `walkthroughs/source-data/enchanter.json`:

```sh
python3 tools/walkthroughs/make_enchanter_walkthrough.py
```

After a fresh local replay, refresh its inventory from the ignored route trace:

```sh
dotnet run --project tools/Leafpile.RouteReplay -- stories/enchante.z3 \
  .agent/regression-routes/enchanter-maximum-score.json \
  .agent/regression-routes/enchanter-maximum-score.trace.jsonl
python3 tools/walkthroughs/make_enchanter_walkthrough.py \
  --refresh-trace .agent/regression-routes/enchanter-maximum-score.trace.jsonl
```

The seed-1 route reaches 400/400 and the Circle ending in 284 moves without a
player-death event. Keep the Enchanter story, manual, hints, route, and raw trace
local; automated tests do not require them.

## Infidel maximum-score no-death guide

The Infidel PDF includes the numbered route, four hand-drawn maps of all rooms
discovered during the route, score checkpoints, and the trace-derived six-column
opcode inventory. Rebuild it from the committed source data with:

```sh
python3 tools/walkthroughs/make_infidel_walkthrough.py
```

After a fresh local replay, refresh the inventory from the ignored trace and
rebuild the guide:

```sh
dotnet run --project tools/Leafpile.RouteReplay -- stories/infidel.z3 \
  .agent/regression-routes/infidel-maximum-score.json \
  .agent/regression-routes/infidel-maximum-score.trace.jsonl
python3 tools/walkthroughs/make_infidel_walkthrough.py \
  --refresh-trace .agent/regression-routes/infidel-maximum-score.trace.jsonl
```

The seed-1 route reaches 400/400 in 237 moves and exits without a player death.
It quits after the statues release the sarcophagus: opening it is the fatal
ending, so it is intentionally excluded from the no-death walkthrough. Keep the
Infidel story, manual, hints, route, saves, and trace local; automated tests do
not require those story assets.
