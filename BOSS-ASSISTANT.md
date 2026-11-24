# Asystent bossów Aurum — analiza i założenia

Moduł jest częścią istniejącego Auruma i korzysta z jego PktMon oraz parsera.
Nie powstaje druga aplikacja ani druga sesja przechwytywania. Wzorem funkcjonalnym
są DBM/BigWigs; kod silnika, adaptera i interfejsu powstaje w Aurumie.

## Dostępne dane

| Źródło | Znaczenie | Ograniczenie |
| --- | --- | --- |
| NpcSeenEvent / 41 36 | Aktor, kod NPC, opcjonalny max HP | Utożsamienie z bossem przez istniejący katalog |
| CastEvent / 02 38 | Aktor, skill, cel, czas odebrania użycia | Brak potwierdzonego czasu castowania i końca castu |
| HitNoticeEvent / 03 38, DamageEvent / 04 38 | Trafienie / obrażenia | Trafienie nie jest rozpoczęciem castowania |
| NpcHpEvent / 00 8D, 1B 92 | HP | Procent tylko przy znanym max HP |
| DeathEvent / 42 36, kill credit | Śmierć | Odróżniamy istniejący martwy obiekt od śmierci w walce |
| ZoneChangedEvent | Zmiana mapy | Reset modułu przy rzeczywistej zmianie lokacji |
| BattleStateEvent / 21 8D | Wejście/wyjście NPC z walki | Istniejący parser; nie potwierdzono ciągłego występowania na Global |

Nie dodajemy fikcyjnego CAST_END. Upływ czasu z profilu nie jest odebranym końcem
castu. Nieznany skill ma jawny identyfikator. Brak max HP nie uruchamia progów
procentowych. Początek obserwacji w środku walki nie daje prawdziwego czasu pulla.

## Referencja i dane pomiarowe

Przejrzano [aion2-raid-ansager](https://github.com/dr89mh/aion2-raid-ansager),
commit `8473d1d9055ae8ff3a2ed3c2a1dff4ea14fff64c` (GPL-3.0), jego opisy protokołu,
format Boss Mods i pomiary `mods/dungeon-timers.json`. Nie przenosimy implementacji
tego programu. Własne profile Auruma mogą zawierać przypisane źródłu fakty pomiarowe:
identyfikatory NPC/skilli, medianę odstępu, liczbę próbek i zgodność. Licencja i
atrybucja danych pozostają w NOTICE. Czasy pochodzą z opisu pomiaru, a nie z paska
referencyjnego, który bywa celowo przesunięty względem mediany.

Takie paski mają oznaczenie przewidywania społecznościowego. Uruchamia je dopiero
rzeczywiście odczytany skill właściwego bossa. Nie są potwierdzeniem aktualności
mechaniki na obecnym patchu. Nazwę skilla dobieramy z istniejących danych Auruma.
Nie kopiujemy przykładowych Krao/skillId/czasów z wklejonego prompta: nie znaleziono
tam potwierdzonego powiązania konkretnego bossa Krao z opisanym obrotowym atakiem.

## Architektura

- `AionMeter.Core/Bosses`: modele, walidowane profile JSON, silnik, adapter zdarzeń,
  analiza pomiarów, zapis/odtwarzanie. Brak zależności od Windows, audio i PktMon.
- Adapter przyjmuje zdarzenia już zdekodowane przez Aurum. Boss musi mieć rozpoznany
  kod NPC i kontakt z własną postacią/grupą lub bieżącym encounterem DPS. Sam boss
  widoczny obok nie uruchamia ostrzeżeń.
- Paski: pierwsze użycie od pulla, następne użycie po odczytanym castowaniu,
  sekwencja po innym skillu, progi HP i fazy z profilu. Prognozy nie zapętlają się
  w nieskończoność bez kolejnego zdarzenia synchronizującego.
- Jedna kolejka ostrzeżeń, deduplikacja castów i progów 10/5/3 s. Render i audio
  działają niezależnie od odbioru pakietów.
- `Aurum.App`: dwa niezależne elementy bez ramki w stylu DBM: wąskie paski
  z ikoną i czasem oraz duże ostrzeżenie na środku ekranu. Ustawienia, krótkie
  sygnały dźwiękowe, demonstracja i replay. Bez głosu Windows / syntezy mowy.
- Własne profile: katalog danych użytkownika `bossmods`; aktualizacja ich nie nadpisuje.
- Dzienniki zawierają NPC, skille, czas i fazę; bez nicków graczy, adresów sieciowych
  i treści pakietów. Zapis jest lokalny i asynchroniczny, bez wysyłania logów.

Własne obserwacje dają propozycje timerów, nie automatycznie zweryfikowane profile.
Do wyznaczania cooldownu liczą się rzeczywiste, zduplikowane casty usunięte przed
analizą, a do porównania faz tylko odczyty o znanym max HP. Demo i replay są stale
oznaczone. Syntetyczne kody są ujemne i nie mogą wejść do silnika Live.

Moduł pozostaje przy obecnym, pasywnym źródle danych Auruma. Nie czyta pamięci gry,
nie wysyła pakietów i nie ingeruje w klienta ani zabezpieczenia. Sam taki sposób
działania nie jest potwierdzeniem zgodności z regulaminem gry.

## Używanie i własne profile

Ikona Aurum → **Asystent bossów** otwiera ustawienia. Odblokuj nakładkę,
przeciągnij oddzielnie paski i tekst ostrzeżenia, a następnie zablokuj. W grze
elementy są widoczne tylko wtedy, kiedy istnieje pasek/ostrzeżenie. Brak stałego
panelu. Zwykłe sygnały dźwiękowe są opcjonalne; syntezy mowy nie ma.

**Demonstracja** trwa około minuty i używa wyłącznie ujemnych, fikcyjnych ID.
**Powrót do gry** kończy demo/replay. Odtwarzanie pliku `.jsonl` ma osobny silnik
z wirtualnym zegarem. Tryb nie przełącza się sam podczas walki.

Profile w `%LocalAppData%\DesktopCompanionData\bossmods` zastępują wbudowany
profil o tym samym `id`, jeżeli mają równą lub wyższą `version`. W wersji portable
folder jest w `user-data`. Po zmianie użyj **Wczytaj profile ponownie**.
Schemat dokumentują typy `BossProfile` i `BossMechanic` oraz pliki `data/bossmods`.
Nie przypisuj prawdziwemu bossowi czasów lub reakcji z `demo.json`.

Zapis `boss-fights/boss-<session>.jsonl` odbywa się przez ograniczoną kolejkę w tle.
Limit to 12 000 zdarzeń na walkę i 100 ostatnich plików przy uruchomieniu. Zrzucenia
z kolejki są widoczne w `session-status.json → BossAssistant.JournalDropped`.
Powtarzalne trafienia i HP są zapisywane najwyżej co 500 ms na bossa. Nie ogranicza
to przetwarzania walki ani zapisu castów, zmian faz i resetów; chroni miejsce dla
ważnych późniejszych zdarzeń w długiej walce raidowej.
Analiza obejmuje maksymalnie 50 najnowszych plików / około 250 tys. zdarzeń.
`timing-proposals.json` zawiera medianę, MAD, zgodność, liczbę próbek i walk.
Uczenie cooldownów wymaga >=4 odstępów, >=2 walk oraz zgodności i pewności >=80%.
Użytkownik włącza je osobno; nie podnoszą pomiarów do rangi zweryfikowanej mechaniki.
Po restarcie przy włączonym uczeniu pomiary są ponownie analizowane w tle.

## Weryfikacja

- 450 testów: m.in. odczyt skilla kontra trafienie, kontakt własnej grupy, brak
  fikcyjnego pulla w środku walki, nieznane HP, duplikaty AoE, progi alarmów,
  fazy, śmierć, reset HP, zmiana mapy/tożsamości, utrata capture, odtwarzanie JSONL,
  walidacja wbudowanych profili i rozdzielenie danych demonstracyjnych.
- `AurumMeter.exe --render-boss-demo <folder>` generuje sześć stanów pasków i
  ostrzeżeń oraz raport. Nie uruchamia PktMon, audio, zapisu ustawień ani updatera.
- Bieżący parser `02 38` odrzuca cel równy 0. Nie rozszerzono układu pakietu bez
  zweryfikowanego nagrania; niektóre bezcelowe/obszarowe skille mogą nie docierać
  jako CastEvent. Trafienie nie jest zamieniane w brakujące rozpoczęcie castu.
- 13 profili / 17 timerów obejmuje tylko pomiary z kotwicą w rzeczywistym skillu.
  Brak profilu Krao z potwierdzonym przypisaniem obrotu. Ogólne skille nieznanych
  bossów są rejestrowane bez spamowania alarmami.
- **Do sprawdzenia w grze:** odbiór wskazanych skillId na aktualnym buildzie,
  rzeczywiste przesunięcie ostrzeżeń, wipe/re-pull, fazy i osobne położenie obu
  nakładek podczas walki. Sam test replay nie potwierdza aktualności mechanik.
