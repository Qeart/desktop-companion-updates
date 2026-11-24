# Boss profiles

The Aurum schema, engine and demo are original implementation. Real profiles adapt
NPC/skill IDs and measured timing facts from `mods/dungeon-timers.json` in
https://github.com/dr89mh/aion2-raid-ansager, commit
`8473d1d9055ae8ff3a2ed3c2a1dff4ea14fff64c`, GPL-3.0. Attribution: dr89mh and contributors.
See the root LICENSE and NOTICE.txt. No reference application source is bundled.

Historical community measurements are predictions, not verified cast durations or
guarantees for the current game patch. Median timings come from measurement notes,
not deliberately early warning bar offsets. Confidence is the lower of the reported
score and the measured inlier ratio. Unknown skills keep explicit IDs.
Pull-only measurements were deliberately omitted because starting observation in an
ongoing fight does not establish the real pull timestamp.

`demo.json` has synthetic negative IDs, fictional timings and reactions. It can only
run in DEMO/REPLAY, never in the live engine. No real encounter reaction (jump, dodge,
stack etc.) is invented. Add verified local corrections in the user `bossmods` folder.
