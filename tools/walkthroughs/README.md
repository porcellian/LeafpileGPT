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

## Sorcerer maximum-score guide

`make_sorcerer_walkthrough.py` rebuilds the Sorcerer guide, including its
228-command route, four hand-drawn maps covering all 62 route-discovered
locations, score checkpoints, and the six-column opcode inventory:

```sh
python3 tools/walkthroughs/make_sorcerer_walkthrough.py
```

After a fresh local replay, refresh the opcode table from its ignored trace:

```sh
dotnet run --project tools/Leafpile.RouteReplay -- stories/sorcerer.z3 \
  .agent/regression-routes/sorcerer-maximum-score.json \
  .agent/regression-routes/sorcerer-maximum-score.trace.jsonl
python3 tools/walkthroughs/make_sorcerer_walkthrough.py \
  --refresh-trace .agent/regression-routes/sorcerer-maximum-score.trace.jsonl
```

The route reaches 400/400 in 379 moves with the winning Circle of Enchanters
ending. It intentionally includes one player death at Hollow, required for the
verified maximum-score route, and avoids additional deaths. Keep the story,
manual, hints, route, saves, and raw trace local; automated tests do not require
those assets.

## Wishbringer 100-point walkthrough

`make_wishbringer_walkthrough.py` builds the Starcross-style Wishbringer guide
from the committed route, checkpoint, map, and trace summary in
`walkthroughs/source-data/wishbringer.json`:

```sh
python3 tools/walkthroughs/make_wishbringer_walkthrough.py
```

After replaying the ignored local route with a fresh Wishbringer story, copy the
trace summary's instruction total and opcode inventory into the source data
before rebuilding. The guide documents the 205-command, seed-1 route to 100/100
in 189 moves without using a wish or dying. Its three field sketches name all
52 room objects, including four separately tagged rooms displayed as
Underground. Keep the story, manual, hints, route, saves, and raw trace local;
the PDF generator only needs the committed source data.

## Sea Stalker no-death walkthrough

`make_seastalker_walkthrough.py` rebuilds the Sea Stalker 100-point guide from
`walkthroughs/source-data/seastalker.json`. It includes the 202-input route,
score checkpoints, field-sketch maps for the laboratory, Aquadome, and ocean
encounters, and the six-column opcode inventory:

```sh
python3 tools/walkthroughs/make_seastalker_walkthrough.py
```

To refresh the inventory from a new local replay, run the ignored route and
trace, then rebuild from that trace:

```sh
dotnet run --project tools/Leafpile.RouteReplay -- stories/seastalker.z3 \
  .agent/regression-routes/seastalker-no-death.json \
  .agent/regression-routes/seastalker-no-death.trace.jsonl
python3 tools/walkthroughs/make_seastalker_walkthrough.py \
  --refresh-trace .agent/regression-routes/seastalker-no-death.trace.jsonl
```

The deterministic seed-1035 route reaches 100/100 in 213 moves and completes
the mission with no player deaths. Keep the Sea Stalker story, manual, hints,
route, saves, and raw trace local; automated tests do not require those files.

## Cutthroats maximum-score walkthrough

`make_cutthroats_walkthrough.py` builds the Sao Vera guide from
`walkthroughs/source-data/cutthroats.json`. The source data contains the clean
282-command route, score checkpoints, hand-sketched maps, and trace-derived
opcode inventory:

```sh
python3 tools/walkthroughs/make_cutthroats_walkthrough.py
```

To refresh the instruction total and opcode inventory from a fresh local run:

```sh
dotnet run --project tools/Leafpile.RouteReplay -- stories/cutthroats.z3 \
  .agent/regression-routes/cutthroats-maximum-score.json \
  .agent/regression-routes/cutthroats-maximum-score.trace.jsonl
python3 tools/walkthroughs/make_cutthroats_walkthrough.py \
  --refresh-trace .agent/regression-routes/cutthroats-maximum-score.trace.jsonl
```

The seed-1 route reaches 250/250, earns the “rich diver” rank, and returns the
treasure to the Mary Margaret without a player death. Keep the Cutthroats story,
manual, hints, route, saves, and raw trace local; automated tests do not require
those assets.

## A Mind Forever Voyaging Z4 best-ending guide

The local Z4 walkthrough is built from the checked-in route and map data in
`walkthroughs/source-data/mind.json`:

```sh
python3 tools/walkthroughs/make_mind_walkthrough.py
```

After a clean local replay, refresh the six-column opcode inventory and total
instruction count directly from its ignored trace, then rebuild the PDF:

```sh
dotnet run --project tools/Leafpile.RouteReplay -c Release -- stories/mind.z4 \
  .agent/regression-routes/mind-maximum-score.json \
  .agent/regression-routes/mind-maximum-score.trace.jsonl
python3 tools/walkthroughs/make_mind_walkthrough.py \
  --refresh-trace .agent/regression-routes/mind-maximum-score.trace.jsonl
```

The route documents the scoreless New Plan ending, 1,080 commands, and the
instruction/opcode counts from a fresh replay. The story, manual, hints, route,
saves, and raw trace remain local and are not needed by CI. The walkthrough
uses the locally available Z4 release because the requested Z3 file is absent.

## Spellbreaker maximum-score walkthrough

`make_spellbreaker_walkthrough.py` builds the Starcross-style guide from the
checked-in 348-command route, four hand-drawn maps of all 58 route-discovered
locations, score checkpoints, and trace-derived opcode inventory in
`walkthroughs/source-data/spellbreaker.json`:

```sh
python3 tools/walkthroughs/make_spellbreaker_walkthrough.py
```

To refresh the opcode table from a fresh local run:

```sh
dotnet run --project tools/Leafpile.RouteReplay -c Release -- stories/spellbre.z3 \
  .agent/regression-routes/spellbre-maximum-score.json \
  .agent/regression-routes/spellbre-maximum-score.trace.jsonl
python3 tools/walkthroughs/make_spellbreaker_walkthrough.py \
  --refresh-trace .agent/regression-routes/spellbre-maximum-score.trace.jsonl
```

The seed-1 route reaches 600/600 and the Scientist ending in 455 moves, then
quits cleanly. The random vault selection is x7 on this seed. Keep the story,
manual, hints, route, saves, and raw trace local; automated tests require none
of those files.

## Ballyhoo maximum-score walkthrough

`make_ballyhoo_walkthrough.py` builds the 200-point rescue guide from
`walkthroughs/source-data/ballyhoo.json`. It includes the 388-command route,
three hand-drawn maps of all 34 route-discovered locations, score checkpoints,
and the trace-derived six-column opcode inventory:

```sh
python3 tools/walkthroughs/make_ballyhoo_walkthrough.py
```

To replay and refresh the opcode inventory from the local story:

```sh
dotnet run --project tools/Leafpile.RouteReplay -c Release -- stories/ballyhoo.z3 \
  .agent/regression-routes/ballyhoo-maximum-score.json \
  .agent/regression-routes/ballyhoo-maximum-score.trace.jsonl
python3 tools/walkthroughs/make_ballyhoo_walkthrough.py \
  --refresh-trace .agent/regression-routes/ballyhoo-maximum-score.trace.jsonl
```

The deterministic seed-1035 route reaches 200/200 in 385 moves and ends with
Chelsea rescued and the circus safe. The story deliberately prints a death
banner after one fall, then says the report is exaggerated and the player only
suffers injured pride; the hints explicitly describe this as a lie. The route
does not restart or lose progress. Keep the story, manual, hints, route, saves,
and raw trace local; automated tests require none of them.
