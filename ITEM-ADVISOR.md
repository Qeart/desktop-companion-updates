# Ctrl item advice

Hover equipment, including an unequipped inventory item, so its game tooltip appears.
Hold Ctrl to show a dark/gold advice panel. Release Ctrl to hide it. Moving away, leaving
the game or disabling the feature also hides it. Hub/tray toggle: BiS sprzętu i arkan.
For the native two-panel comparison, the right tooltip is the hovered inventory item;
the equipped item on the left is excluded. An unreadable right panel must not fall back
to advice based on the equipped item.
The Arcana screen has separate automatic guidance below Set Effects (see ARCANA-ADVISOR.md).

The user's selected profile is always PvE DPS, independent of the meter's DPS/HEAL tab.
SelfClass comes from the exact local character in CombatTracker, including the existing
name cache. Another nearby player's class cannot select the profile.

## Interpretation

These are target bonus recommendations for the slot and class, not an item DPS simulator
or a command to reroll. They do not calculate skill breakpoints, caps, set bonuses or the
value of replacing the whole current loadout. Exact percentage/value maxima are omitted
because they depend on the item and its level. Unknown classes/slots get no invented profile.

Profiles cover eight Global classes and 13 explicit Soul Binding slot plans for each class.
Each has a build name, own skill priorities, stat order, conditions and multiple sources.
Shared useful stats remain shared where supported; there is no generic fallback build.
Boot/earring movement is utility, separately listed. Shoulder Double Chance is now included,
cross-checked against the Global Sorcerer gearing guide and the slot table.
Rings recommend active skill lines; armour/cloak/necklace/earrings recommend the selected
class's damage passives. Weapon/off-hand advice prioritises damage rolls rather than
displacing them with skill lines. See BIS-RESEARCH.md for the per-class decisions.

The green check means OCR recognised that target inside Soul Binding. It does not evaluate
roll quality. Base stats and gem/socket stats are excluded; a section boundary and a large
vertical gap guard prevent a socket's Attack+10 from becoming an apparent Soul Binding roll.

## Implementation

- Checks require Ctrl without Shift/Alt, a stationary pointer and the game foreground.
  Ctrl+Shift hub hotkeys suspend the checks.
- Work runs off the UI thread and is cancelled logically on input/foreground/settings changes.
- Read a bounded vertical strip extending left of the pointer to include both comparison
  panels even when the hovered inventory slot is far below/right of its tooltip. Locate
  candidate borders with OpenCV and prefer native close-cross headers. Matching horizontal
  panels use one right-panel selection rule before OCR; OCR candidates separately.
- If borders are clipped, fallback OCR splits at Item Level headings and applies the same
  selection rule. Soul Binding lines from the two items are never merged.
- Cache title, category and binding text patches; validate against a fresh capture before
  reusing. Input changes invalidate selection, and a single-panel cache expires after 250 ms
  so a newly opened comparison cannot retain advice for the previously equipped item.
- Reuse WindowsTextReader, extracted without changing the shop OCR algorithm. Each caller has
  its own engine, so the shop cannot block item recognition.
- Advice is click-through, does not activate/focus the game, and excludes itself from capture.
- Status: session-status.json / ItemAdvice. Failures: item-advice-error.txt.
- Preview: --render-item-advice <screenshot> <directory> writes OCR report and rendered samples.

## Sources, checked 22 November 2026

Only short factual stat/skill names are stored in data/gear-advice.json; no guide prose is copied.

- https://raidrelay.com/aion-2-best-stats-guide — global Soul Binding slot priorities (11 November).
- https://aion2.gg/guides/chanter-pve-build — Chanter DPS priorities, skill breakpoints and roll eligibility;
  published client-data version EU/NA 2.0.10.0, extracted 5 November.
- https://aion2-meta.com/guides/chanter/ — class damage passives.
- https://aion2-meta.com/guides/gladiator/
- https://aion2-meta.com/guides/templar/
- https://aion2-meta.com/guides/assassin/
- https://aion2-meta.com/guides/ranger/
- https://aion2-meta.com/guides/sorcerer/
- https://aion2-meta.com/guides/spiritmaster/
- https://aion2-meta.com/guides/cleric/

Class picks are damage-oriented selections from the guides, not a claim that popularity proves
a unique optimum. Tank/healing-oriented skills were not promoted into the PvE DPS profile.

## Validation

Tests cover comparison selection with a distant pointer, contour order, UI scale and unrelated
windows, slot parsing, rarity/type versus ambiguous item names, own class/cache restoration,
active/passive selection, OCR heading variants, strict Attack versus Attack increase/passives,
Boost versus Tolerance, and ignoring gems/base stats even with a missed Fuse header.
Screenshot fixture: the user's Nuakum Helm yields Helmet/Chanter, zero of the three target rolls,
without counting the visible Attack+10 socket. The comparison fixture selects Spectral Gloves
on the right, recognises Attack and excludes the left Nuakum Gloves' Impact Hit/Wind's Promise.
Shop regression fixture still yields ten cards and passes close/change/reopen/resize checks.
Actual Ctrl/inventory hover, movement and positioning require a live game check.
