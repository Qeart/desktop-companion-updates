# Pet progress appearance and shop latency

The soul pickup notification remains below the target HP bar (9.5% of game client height).
It uses the exact pet ID from the network event to load a local portrait, shows the pet name,
gain, current stage counter and progress line. Maximum level shows FULL. A pickup batch
displays all its pets. This path does not capture the screen or run OCR.

Shop cards have an opaque black/gold badge, bold 16 px counter, progress line and FULL checkmark.
No cache/source suffix is displayed.

## Shop reader

- Poll the game every 100 ms. Capture uses the existing DXGI implementation in colour;
  GDI is a fallback after an unavailable/lost duplication session.
- OCR runs on a separate task and never holds the polling/capture task while recognising.
  Completion requests a fresh poll immediately.
- Learn the three shop tab labels and matched card titles. Cache their pixel patches,
  positions and pet IDs in memory. Validate every title against a fresh frame before using
  the saved layout. Small luminance changes are tolerated; changed text invalidates it.
- Closing the shop, changing its cards or resizing the game hides badges until a matching
  layout is verified. A delayed OCR result cannot draw directly over a newer frame.
- Cached layouts survive closing/reopening; pet counts always come from current PetProgress.
- The card ROI starts at 4.5% of client width: the previous 10% crop cut the leftmost title.
- OCR input is scaled with game height to keep title recognition cost similar across resolutions.
- Full pet names and unique clipped catalogue prefixes are recognised. OCR may miss the Pet:
  prefix/colon or read the ellipsis as one/two dots; explicit clipped bare names still match
  when sufficiently long and unique. Ambiguous prefixes are rejected, including a clipped
  name that is also an exact shorter pet name (Dratona… versus Dratona Tinkerer).
- Shop/toast windows exclude their own content from capture. The existing map window uses
  its own shared scene and does not participate in shop capture.
- Stop/disable/close increments a generation so an in-flight poll cannot resurrect the window.
- Diagnostics: session-status.json / Shop.Reader exposes capture backend, poll and OCR time,
  OCR counts, cache hits, cached cards and errors. shop-status.json is written off the UI thread.

## Verification

`--render-pet-upgrade <shop screenshot> <pets-cache.json> <directory>` renders pickup/shop
previews and writes shop-check.json. This checks a full OCR pass, repeated cached frames,
shop close, changed title, reopen and resolution change. It also compares OCR scales.
These are fixture measurements; actual open/close latency requires an active AION2 shop.
