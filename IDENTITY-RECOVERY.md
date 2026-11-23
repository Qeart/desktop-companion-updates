# Starting Aurum after the game

## Failure addressed

On 23 November the installed 0.2.2 instance captured game traffic successfully,
but had no SelfId, map 0 and an Unknown local class. The login identity had
preceded capture, and the previous session cache was older than its 45-minute
limit. Party/force filtering excluded the unconfirmed player; both advice
features correctly refused to invent a class. The UI nevertheless said that
capture was active, without explaining the missing identity.

The fix does not extend the lifetime of actor ids. They can be reused after
a zone or character change.

## Recovery path

- The parser validates the complete own-stat update (`4A 36`): actor varint,
  count byte, six bytes per changed stat and eight trailing bytes. Count zero
  is legitimate; the user's current Global client sends these regularly.
- Three consecutive valid updates for one actor, no more than ten seconds
  apart, establish its local identity. Nearby-player announcements, HP updates,
  kill names and the ambiguous party-scope channel do not establish ownership.
- The actor's live player record can provide its name/server/class. Otherwise
  one own cast or two consistent own damage/healing records establish its
  class. Other actors' skills do not select the local class.
- Stable saved metadata is only a name/server/class hint. It is used after
  a live identity for the independently confirmed own actor matches the name
  and server. It never supplies an actor id or elects a nearby player.
- An official SelfInfo record remains authoritative. Earlier-timestamped stat
  updates cannot move the identity back. A real zone transition drops the old
  binding, combat buffer and evidence before the new actor is identified.
- One shared resolver feeds the meter, pets, position and Hidden Cube. Item
  and Arcana advice read the resulting exact local class from CombatTracker.
- Up to 1,024 combat events from the preceding 15 seconds wait for identity.
  They are delivered once when ownership is established, and ordinary party/
  force/dungeon filtering still applies. Events from before a zone change
  are discarded. This avoids double counting early hits.

`known-characters.json` stores up to 64 stable profiles outside the installation,
alongside the other user data. There are no actor ids, map ids or rosters in it.
An older names-cache file can migrate the last confirmed profile even when its
session ids have expired. The short-lived session cache keeps its previous TTL.

## UI and diagnostics

While identity is missing, the meter says "Synchronizacja postaci" and prompts
for a skill/buff instead of displaying an unqualified active-capture message.
Once the actor is known but the class is not, it asks for a class skill.

`session-status.json / LocalPlayer` records the stage, source, actor, class,
stat streak/count, queued combat events and number of remembered profiles.
No OCR or game-memory access is used for this recovery path. An idle game must
still send live evidence; the implementation does not promise instant recovery
when the server sends none. Missing map, roster or NPC announcements are separate
from local identity and are not fabricated by this change.

## Protocol provenance and checks

The own-stat layout and its local scope were cross-checked against
[A2Tools-DPS-Meter](https://github.com/taengu/A2Tools-DPS-Meter/tree/a9c18e035f7258f91d9f63e86b76ba532f203387/src-tauri/src/capture),
notably `abnormal.rs` (which credits Seralth's Daevalog, GPL-3.0).
The C# length validation/resolver/store implementation is local. Upstream
notices remain in NOTICE.txt and LICENSE.

An independent one-minute capture of the user's running game observed own-stat
updates for actors 13776 and 2289 across a zone change; both were independently
named as self by official identity records. Zero-count frames were confirmed.
This is observed protocol evidence, not an assumption from the source comments.

Automated tests cover cold start without SelfInfo, class recovery, exact
name/server matching, stale/reused ids, character/zone changes, NPCs and effects,
ambiguous scope observations, invalid stat records, bounded buffering and stable
metadata migration. A separate live probe deliberately suppresses all SelfInfo
events and starts with no character cache to exercise the new path in game.
That cold test recovered the expected actor 2289 and Chanter class in 2,792 ms,
received 64 own-stat records in a minute and counted local-player damage. No
SelfInfo event arrived during the test. This validates the recovery path in
that Global session; timing still depends on live traffic.
