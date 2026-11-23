# Pozycja gracza i nawigacja Hidden Cube

## Wynik analizy, 23 listopada 2026

Obecny jawny odczyt sieciowy nie zapewnia ciągłych współrzędnych własnego gracza
podczas samego biegania. Nie jest to naprawione samym szybszym renderowaniem.
W kontrolowanym teście obejmującym postój, bieg i buff wystąpiła **29,05 s przerwa
między potwierdzonymi pozycjami**, a po ostatnim odczycie kolejne około 35 s bez pozycji.
To wniosek z tego testu i obsługiwanych struktur, nie dowód, że żadna przyszła
wersja protokołu nie może zawierać dodatkowego źródła.

Pierwotne problemy w kodzie:

1. `PlayerPositionTracker.Raw` obsługiwał tylko mapę, teleport i wybrane własne efekty.
2. PktMon przechwytywał oba kierunki, lecz konsument przekazywał dalej tylko S→C.
3. `HiddenCubeTracker.Nearest` po 3 s od pozycji zwracał `null`; kontroler ukrywał wskaźnik.
4. Render kontrolera czekał na odczyt kamery, a animacja igły dodawała 80 ms opóźnienia.
5. Surowy pakiet MapLoad trafia przed zdarzeniami parsera. `LocalIdentityLostEvent`
   emitowany przez resolver mógł skasować właśnie odczytaną pozycję z tej samej ramki.

## Przechwytywanie i składanie strumienia

`PktmonCapture` używa `pktmonapi.dll`, prywatnej sesji realtime i symetrycznego filtra
TCP/13328. Nie uruchamia polecenia `pktmon start`, dlatego `--pkt-size 0` nie ma tu
zastosowania. Konfiguracja API ma `BufferMultiplier=10`, `TruncationSize=9000`, czyli
maksimum obsługiwane przez to API. Nie gwarantuje przechwycenia pakietów większych
niż 9000 bajtów. Ostrzeżenia API i pakiety za duże mają osobne liczniki.
[Dokumentacja Microsoft](https://learn.microsoft.com/en-us/windows/win32/pktmon/packetmonitor/nf-packetmonitor-packetmonitorcreaterealtimestream).

Przepływ: callback native → ograniczona kolejka 8192 pakietów → `RawTcpReader`
→ `SegmentObserved` (oba kierunki, opcjonalna diagnostyka) → dla S→C `PacketPipeline`
→ osobny `TcpReassembler` dla kierunkowego połączenia → `FrameDecoder` (ramki i LZ4)
→ tap pozycji i parser zdarzeń → resolver tożsamości → wspólne trackery.

Reassembler przycina nakładanie i duplikaty, buforuje fragmenty poza kolejnością,
po 1500 ms nierozwiązanej luki przy następnym segmencie uruchamia resynchronizację.
Dekoder ma ograniczenia rozmiaru ramek, bufora i zagnieżdżenia LZ4. PktMon może
zgłaszać tę samą transmisję na kilku warstwach stosu; nie oznacza to zduplikowanych
odczytów aplikacyjnych. Jawny dekoder serwerowy nie dostaje bajtów C→S.

Wynik lokalnego testu 90,386 s (surowe dane pozostają poza repozytorium i paczką):

| Pomiar | Wynik |
| --- | ---: |
| Segmenty S→C, przed deduplikacją | 2583 |
| Segmenty C→S, razem z ACK | 4904 |
| C→S z danymi, przed deduplikacją | 4248 |
| Zduplikowane segmenty C→S | 2124 |
| Unikalne bajty aplikacyjne C→S | 30158 |
| Heartbeaty S→C | 1803 |
| Zgłoszone straty PktMon / nieobsługiwane pakiety / luki TCP | 0 / 0 / 0 |
| Maksymalne opóźnienie konsumenta | 64 ms |
| Potwierdzone pozycje istniejącym dekoderem | 51 |

Przechwycone początki danych C→S zachowują pole długości, ale kolejne bajty zmieniają
się i nie mają spójnych jawnych opcode'ów dekodera S→C. Jest to zgodne z możliwością
szyfrowania/obfuskacji, ale sam ten test nie ustala użytego mechanizmu. Nie uzyskano
potwierdzonej struktury pozycji C→S. Nie dodano odszyfrowywania, odczytu pamięci gry,
wstrzykiwania, hooków ani obchodzenia zabezpieczeń.

## Weryfikacja wiadomości

| Opcode | Ustalenie dla dostępnych danych | Działanie |
| --- | --- | --- |
| `2a 38` | 3 ramki; 2 spełniały istniejące rygorystyczne warunki własnego efektu | Potwierdzona pozycja, centymetry → metry |
| `2b 38` | 49 potwierdzonych własnych odczytów | Jak wyżej |
| `01 37`, `00 37` | Brak w poprawnie zdekodowanym S→C w tym teście; brak potwierdzonego jawnego układu C→S | Nie dekodujemy na podstawie przypuszczenia |
| `02 38` | 53 ramki; wariant kind 2 wskazujący siebie zawierał mały wektor, nie współrzędne świata | Nie dodajemy jako źródła pozycji |
| `0e 38` | Brak w tym teście | Brak nowego dekodera |
| `21 36`, `23 36` | Brak w tym teście; istniejące implementacje i wcześniejsze testy map/teleportów | Zachowane, z walidacją i resetem prędkości |
| `1a 37`, `1b 37`, `1c 37`, `29 37` | Brak w tym teście; nie potwierdzono własnej pozycji | Nie podmieniamy swojej pozycji pozycją innego aktora |

Badania [cube-radar, FINDINGS P1–P7](https://github.com/dr89mh/aion2-cube-radar/blob/main/docs/FINDINGS.md)
(przejrzany commit `c7465407a38bd2f6be4cc278117fa189cfcf39ef`) rozróżniają pozycję celu
od pozycji rzucającego skill i opisują dodatkowe źródła dla konkretnego starszego buildu.
Nie przeniesiono tych założeń bez potwierdzenia w aktualnym strumieniu. Dwa minimalne
rekordy z obecnego testu dodano do testów regresji; pełnego przechwycenia nie publikujemy.

## Centralny stan i predykcja

`PlayerPositionTracker` przechowuje `PlayerMapPosition`: MapId, XMetres, YMetres,
HeightMetres, At (Unix ms), Source, Confidence, IsEstimated. `Snapshot()` zwraca
ostatni potwierdzony odczyt, bez odnawiania czasu i bez podmieniania go prognozą.
`Tracking(now)` zwraca dodatkowo stan, ostatni wiarygodny czas i prędkość.

Odrzucane są stare czasy, obca mapa, NaN/Infinity, niewiarygodny skok prędkości,
próba ponownego użycia estymacji jako pomiaru i sprzeczna pozycja z tym samym czasem.
Zmiana mapy/postaci unieważnia stan; jawny teleport akceptuje nową pozycję i zeruje
prędkość. Błędny lub niepełny MapLoad nie kasuje poprawnej pozycji.

Prędkość powstaje tylko z dwóch potwierdzonych próbek o pewności ≥0,8, oddalonych
domyślnie o 100–2000 ms, do 35 m/s. Duży odstęp między buffami nie jest wiarygodną
prędkością. Jedna próbka nie daje kierunku ruchu. Prognoza używa wyłącznie XY,
ostatnia wysokość pozostaje bez prognozowania. Korekta z nowego pomiaru jest natychmiastowa.

LIVE oznacza używany potwierdzony punkt do 1 s. ESTIMATED oznacza prognozę i zawsze ma
`IsEstimated=true`. Podczas wykrytego ruchu prognoza zaczyna się od pierwszego renderu
po pomiarze i od razu jest oznaczona jako szacowana — nie czekamy 1 s, żeby nagle
przesunąć igłę o sekundę ruchu. Maksymalny horyzont wynosi **2 s od ostatniego
pomiaru**. Następnie punkt jest zamrożony do progu 3 s. Pewność maleje z wiekiem.
Bez wiarygodnej pary próbek, po wyjściu z LIVE, stan przechodzi bezpośrednio do STALE.

STALE zachowuje historyczny cel/kierunek, lecz `DistanceMetres=null` i pewność 0.
Skrzynka nie znika wskutek wieku pozycji. Rzeczywiste zdarzenie usunięcia obiektu,
zmiana mapy/postaci, teleport, wyłączenie funkcji lub opuszczenie gry nadal obowiązują.

## Overlay, kamera i diagnostyka

Render co 33 ms oblicza nawigację z centralnego stanu niezależnie od nowych pakietów.
Osobny asynchroniczny odczyt co 100 ms analizuje wyłącznie minimapę. Czas orientacji
oznacza początek pobierania obrazu, a nie koniec obliczeń. Wynik spóźniony,
niejednoznaczny, z innego okna albo sprzed otwarcia mapy/sklepu jest odrzucany.
Domyślna ważność kamery wynosi 400 ms. Bez niej nie wyliczamy nowego względnego
kierunku: stara igła jest przygaszona i oznaczona, a przed pierwszym poprawnym
odczytem kamery igła w ogóle nie jest rysowana. Zmiana celu unieważnia historyczną igłę.

LIVE: normalna strzałka i dystans; ESTIMATED: przygaszona strzałka, `≈`, `SZACUNEK`;
STALE: przygaszony kierunek historyczny, `—`, `NIEAKTUALNA POZYCJA`.
Usunięto dodatkową animację 80 ms. Nie ma wygładzania maskującego stary pomiar.

`%LocalAppData%/DesktopCompanionData/session-status.json` zawiera:
`PositionTracking` (źródła, wiek, prędkość, horyzont, odrzucenia), `CaptureDiagnostics`
(kierunki, straty, opóźnienie kolejki, ostrzeżenia API, duplikaty TCP, luki,
resynchronizacje ramek) i `HiddenCube` (stan, dystans, cel i oddzielna aktualność kamery).
Nie zapisujemy w produkcyjnej diagnostyce treści pakietów.

Konfiguracja w `settings.json`, odczytywana przy uruchomieniu:

```json
{
  "CubePositionLiveMilliseconds": 1000,
  "CubePositionStaleMilliseconds": 3000,
  "CubePredictionMilliseconds": 2000,
  "CubeCameraFreshMilliseconds": 400
}
```

## Ring-Fix i weryfikacja w grze

Nie wdrożono Ring-Fix. Dostępne zdarzenia zawierają pozycje obiektów, ale
`WorldObjectGoneEvent` nie zachowuje powodu despawnu. Nie można zatem odróżnić wyjścia
z zasięgu od usunięcia/otwarcia. Nie ma też zweryfikowanych promieni per template
ani kalibracji względem gęstych własnych odczytów. Referencja wymaga m.in. tych danych
i opisuje duże odstające błędy. Samo przecięcie niezweryfikowanych okręgów nie daje GPS.

Testy automatyczne obejmują stare/future/obce/niepoprawne próbki, utratę tożsamości,
rzeczywistą kolejność zdarzeń MapLoad, teleport, ruch, zatrzymanie, długą przerwę,
ograniczenie horyzontu, powrót pomiaru, zachowanie skrzynki w STALE i aktualność kamery.
Podglądy wszystkich stanów można wygenerować przez `--render-hub <katalog>`.

**Do sprawdzenia w grze przy faktycznie wykrytej skrzynce:** dokładność kąta podczas
obrotu kamery, przejścia LIVE/ESTIMATED/STALE, bieg bez skilli, zatrzymanie, teleport,
otwarcie/zamknięcie mapy i powrót z Alt+Tab. W teście analizy nie było aktywnego
Hidden Cube, więc nie potwierdzono kompletnego wskazania do realnej skrzynki.
Bez dodatkowego zweryfikowanego źródła pozycji długi bieg nadal nie będzie dokładnym
śledzeniem; nowa wersja jawnie pokazuje to ograniczenie.
