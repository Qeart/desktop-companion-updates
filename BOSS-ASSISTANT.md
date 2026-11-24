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
Krao Cave to nazwa dungeona. Jego główny boss **Ultimate Berk** ma już profil
`boss-2300171` z timerami Wave of Fury oraz Drana Wave. Nie ma jeszcze potwierdzonego
przypisania obrotowego ataku do skilla; `1800170` jest obecnie oznaczony „Możliwy
obrót”, zgodnie z sugestią użytkownika i trzema odczytami poprzedzającymi serię
`1800160`. Nie przypisujemy mu stałego cooldownu ani prawdziwego czasu castowania.

### Poprawka po walce Hard (0.2.7-beta.4)

W lokalnym dzienniku 143-sekundowej walki `1800170` wystąpił trzykrotnie, lecz
profil miał tylko dwie reguły timerów. Lista ODCZYT nie zastępowała ostrzeżenia,
a podstawowe ataki wypierały z niej istotne użycia. Profil v2 dodaje reakcje na
Rush i Wrathful Blade, prowizoryczny obrót oraz ogólny alarm dla nieprzypisanego
`1800190`. Ten ostatni nie ma zgadywanej animacji ani kierunku uniku.
`quietSkills` usuwa powtarzalne podstawowe/systemowe użycia wyłącznie z listy,
zachowując je w dzienniku. Ostrzeżenie obrotu pozostaje 7 s; to czas wyświetlania,
nie zmierzony cast. Wrathful Blade ostrzega o możliwym obrocie bez liczbowej prognozy.

Oba odczyty `1800150 → 1800053` dały 8,399/8,402 s, potwierdzając historyczne
8,4 s (40 próbek / 21 walk). `alertOnAnchor` uruchamia komunikat przygotowania
od razu po kotwicy; `windupFromAnchor` pokazuje narastający pasek PRZYGOT. z ≈czasem
i alarmami progów. Potwierdzony skill docelowy usuwa przewidywany pasek. Bez niego
pasek znika po terminie z 750 ms tolerancji i nie uruchamia kolejnego cyklu.
To pomiar od sygnału przygotowania do użycia, a nie odczyt paska castowania gry.
Pole `alertSeconds` (0,5–30 s, domyślnie 4,5 s) kontroluje trwałość komunikatu.

Nie każdy widoczny w grze cast ma obecnie rozpoznany początek/koniec w protokole.
Samego trafienia nie przedstawiamy jako wczesnego ostrzeżenia. Casty świeżo
ogłoszonych innych NPC przy aktywnym bossie są zapisywane jako `unmappedCast`
z osobnym `sourceActor`/`sourceNpcCode` i Confidence=0. Nie uruchamiają mechanik,
nie udają castu bossa i nie trafiają do uczenia cooldownu. Ograniczona diagnostyka
ostatnich 16 takich odczytów jest w `BossAssistant.Adapter.UnmappedCasts`;
`ActiveTimers`/`ActiveWarnings` pokazują stan faktycznie przeznaczony do renderu.

Replay zanonimizowanych względnych czasów Hard sprawdza trzy ostrzeżenia obrotu,
obie kotwice Fali, narastanie paska i usunięcie na rzeczywistym skillu, brak
fikcyjnego cooldownu, trwałość alarmu mimo podstawowych ataków oraz wygaśnięcie
bez kolejnych pakietów. Potwierdzenie animacji obrotu, aktualności sygnałów i
opóźnień na żywo pozostaje do kolejnej walki; użytkownik jest już poza grupą.

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
Powtarzalne trafienia są zapisywane najwyżej co 500 ms na bossa i skill, HP co 500 ms
na bossa. Kontakt drużyny nie wypiera ataków bossa o innych ID. Nie ogranicza
to przetwarzania walki ani zapisu castów, zmian faz i resetów; chroni miejsce dla
ważnych późniejszych zdarzeń w długiej walce raidowej.
Analiza obejmuje maksymalnie 50 najnowszych plików / około 250 tys. zdarzeń.
`timing-proposals.json` zawiera medianę, MAD, zgodność, liczbę próbek i walk.
Uczenie cooldownów wymaga >=4 odstępów, >=2 walk oraz zgodności i pewności >=80%.
Użytkownik włącza je osobno; nie podnoszą pomiarów do rangi zweryfikowanej mechaniki.
Po restarcie przy włączonym uczeniu pomiary są ponownie analizowane w tle.

## Weryfikacja

- 505 testów: m.in. odczyt skilla kontra trafienie, kontakt własnej grupy, brak
  fikcyjnego pulla w środku walki, nieznane HP, duplikaty AoE, progi alarmów,
  fazy, śmierć, reset HP, zmiana mapy/tożsamości, utrata capture, odtwarzanie JSONL,
  walidacja wbudowanych profili i rozdzielenie danych demonstracyjnych.
- `AurumMeter.exe --render-boss-demo <folder>` generuje sześć stanów pasków i
  ostrzeżeń oraz raport. Nie uruchamia PktMon, audio, zapisu ustawień ani updatera.
- Parser `02 38` przyjmuje cel 0 dla siedmiocyfrowych skilli NPC, zachowując granice
  aktora/skilla/celu. Układ sprawdzono z dekoderem RaidAnsager i testami granicznymi;
  w nagraniu zgłoszonego Rush zdarzenia były już poprawnie zdekodowane. Nie twierdzimy,
  że ten konkretny brak był spowodowany celem 0. Trafienie nie zastępuje castu.
- 13 profili / 17 timerów obejmuje tylko pomiary z kotwicą w rzeczywistym skillu.
  Profil Berka z Krao Cave ma dwie mechaniki; obrót pozostaje do zidentyfikowania.
  Odczytane skille mają osobną listę informacyjną, również bez profilu timerów.
- **Do sprawdzenia w grze:** odbiór wskazanych skillId na aktualnym buildzie,
  rzeczywiste przesunięcie ostrzeżeń, wipe/re-pull, fazy i osobne położenie obu
  nakładek podczas walki. Sam test replay nie potwierdza aktualności mechanik.

## Test Krao Cave / Ultimate Berk

1. W ustawieniach asystenta wybierz **Test Krao Cave** przed walką z głównym bossem.
2. Przy zaobserwowanym obrocie/ważnej mechanice naciśnij **Ctrl+Shift+B**. Skrót jest
   rejestrowany wyłącznie w trakcie testu, gdy gra lub Aurum jest na pierwszym planie.
   Jeśli jest zajęty, panel pokazuje informację i pozostaje przycisk znacznika.
3. Po walce wybierz **Zapisz raport testu**. Zatrzymuje to test i zapisuje JSON do
   `%LocalAppData%\DesktopCompanionData\boss-reports`. W portable: `user-data/boss-reports`.
4. Do sprawdzenia powtarzalności zbierz co najmniej dwie walki. Raport nie potwierdza
   mechaniki automatycznie; pokazuje kandydatów z 3 s przed i 2 s po znaczniku.

Test obserwuje kody 2300171–2300174, 2310171–2310174, 2320171–2320174. Ich wspólną
nazwę i dungeon potwierdzają własne tabele NPC (12 rekordów). Nie rozszerza to
automatycznie profilu timerów na niezmierzone formy. `HasTimerProfile` w raporcie
pozwala odróżnić samo zarejestrowanie zdarzeń od posiadania profilu dla danej formy.

Znacznik ma `SkillId=0`, niezależną chwilę obserwacji i źródło opisujące niepotwierdzone
powiązanie. Cast i trafienie pozostają osobnymi zdarzeniami. Demo jest wykluczone.
Raport zawiera wyłącznie zdarzenia NPC, bez nicków graczy, adresów ani pakietów;
zostaje lokalnie. Maksymalnie 20 tys. zdarzeń przez 15 minut. Utracone zdarzenia są
oznaczone w `Dropped`; taki raport jest niepełny. Bieżący postęp testu jest w pamięci,
więc raport należy zapisać przed zamknięciem Auruma. Zdarzenia są też przekazywane
do zwykłego lokalnego dziennika podczas testu, nawet przy wyłączonym ogólnym zapisie.

## Brak Rush na nakładce — regresja beta.3

Rzeczywisty dziennik zgłoszonej walki zawierał Rush `1800030` i `1800031`.
Przyczyną widocznego braku było uzależnienie ostrzeżeń od istnienia reguły w profilu,
który obejmował tylko Wave of Fury i Drana Wave. Odczytany cast był zapisywany, ale
nie trafiał na ekran. Od beta.3 snapshot zawiera niezależne `ObservedSkills`.

Lista **Ostatnie użycia** pokazuje maksymalnie 4 wpisy przez 5 s, z tagiem ODCZYT,
bez odliczania i dodatkowego dźwięku. Nie zastępuje centralnego ostrzeżenia i nie
tworzy niepotwierdzonego przyszłego timera. Domyślnie jest włączona; ustawienie
**Pokazuj odczytane skille, również bez profilu timerów** wyłącza wyłącznie tę listę.
Krótkie duplikaty nazw/aktorów są scalane, a dziennik zachowuje surowe ID.

`Aurum.Tests/Fixtures/berk-casts.json` zachowuje kolejność i odstępy 11 odczytanych
castów z walki, z czasem przesuniętym na względną oś. Brak nicków, oryginalnych ID
aktorów, identyfikatorów sesji, adresów i pakietów. Regresja sprawdza widoczność Rush
oraz brak wymyślonego cooldownu. Dodatkowy render `boss-observed-rush.png` pokazuje
ten przypadek jako odrębny replay, bez połączenia z grą i audio.
