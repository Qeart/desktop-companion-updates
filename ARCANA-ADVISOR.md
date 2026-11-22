# Arcana advice below Set Effects

The Arcana screen automatically shows a compact Aurum panel below the last visible Set
Effects line. Ctrl is not required. It shares the hub/tray toggle "BiS sprzętu i arkan"
with the Ctrl equipment-hover advisor. The profile remains PvE DPS, independent of
the DPS/HEAL tab. Class comes from the exact local player in CombatTracker.

## Content

Five card slots and eight verified classes have card-specific skill goals. The Chanter
plan is four Primal Vigor cards and Bell of Magic; the set's HP >=70% condition is printed
in the panel. Other profiles use this explicitly stated DPS starting plan and legal skill
pools; they are not simulations of every gear/MP/tanking situation. Alternative card stats
are shown separately. The panel does not assert that an entire set is already equipped.

The selected screenshot Chalice has the recommended Time [Siel] stat (Combat Speed),
and carries Dark Crush and Onslaught. Presence checks are green. Skill bonus numbers
are shown only if OCR reads them confidently; otherwise the panel says "masz" and does
not invent a value. A +1 existing skill is not labelled a maximum/finished BiS roll.

Skill recognition compares complete names against the known recommendation catalogue.
It tolerates only specified OCR glyph confusions (I/l/1, Polish ł in place of t/l,
and zero in place of o) and a short leading effect-icon token. A visual match must
be unique; partial names and arbitrary spelling changes are not guessed. A skill
not confirmed by OCR is labelled "nie potwierdzono", rather than asserted absent.

## Reading and positioning

- OCR first verifies all four Arcana menu labels, then the right card panel must contain
  a known card type, Equipped Effect and Set Effects. Other game screens get no panel.
- Card type comes first from the smaller Item Level category; the decorative title can
  be clipped, partially misread or absent. A unique explicit type prefix such as Parch…
  also works. An unreadable Vigor/Magic suffix stays unknown and does not block slot advice.
  Conflicting categories and unknown card types are rejected.
- Capture is limited to the small central header and the right information column. It
  does not capture/process the animated card in the centre of the screen.
- Recognition runs separately from 100 ms image checks. All learned header/body patches
  must match a fresh frame before any cached recommendation is displayed.
- Close/change/resize/foreground/settings changes hide the panel. A late OCR result cannot
  display directly over a newer scene. Failed parses retry instead of caching a permanent miss.
- The advice anchor is the measured bottom of the actual set text, with a section-gap guard.
  It does not cover the native Set Effects text or lower action buttons; if it cannot fit it hides.
- The advice window is click-through, does not activate, and excludes itself from capture.
- Diagnostics: session-status.json / ArcanaAdvice. Errors: arcana-advice-error.txt.
- Preview: --render-arcana-advice <screenshot> <directory> writes arcana-check.json and a
  rendered full-screen example. Fixture checks exercise cache reuse, close and changed content.

## Sources checked 22 November 2026

Short factual card/stat/skill names are recorded in data/arcana-advice.json; guide prose
is not copied. Source URLs are stored per class:

- https://aion2.gg/guides/chanter-pve-build — card pools, DPS skill targets, card main stats,
  four-Vigor-plus-Bell-of-Magic plan and the health condition.
- https://aion2.gg/guides/gladiator-pve-build
- https://aion2.gg/guides/templar-pve-build
- https://aion2.gg/guides/assassin-pve-build
- https://aion2.gg/guides/ranger-pve-build
- https://aion2.gg/guides/sorcerer-pve-build
- https://aion2.gg/guides/spiritmaster-pve-build
- https://aion2.gg/guides/cleric-pve-build
- https://raidrelay.com/aion-2-arcana-guide — Chalice any skill; other card skill-pool split.

The guides publish EU/NA client 2.0.10.0 data extracted 5 November 2026. DPS-oriented
selections are restricted to their listed legal pools; heal/tank-only targets were not
promoted simply because they were popular in a different role.

## Validation

Core tests cover five card types, class selection, legal per-card goals, skill bonus rows,
missing selection/sections, clipped/missing decorative titles, unique clipped type prefixes,
conflicting categories, the observed decorative-title OCR typo, and the set anchor.
The user's screenshot passes recognition/cache/close/changed-frame checks and visual layout
review. Actual opening, changing cards, closing and positioning at the user's game UI scale
still require a live game check.
