# 0.2.4

- Hidden Cube: wspólny stan pozycji, walidacja czasu/mapy i krótka predykcja wyłącznie z wiarygodnej pary pomiarów.
- Odrębne stany LIVE / SZACUNEK / NIEAKTUALNA POZYCJA; brak starego dystansu przedstawianego jako aktualny.
- Wykryta skrzynka pozostaje widoczna po utracie aktualności pozycji. Render co 33 ms niezależnie od odczytu kamery i pakietów.
- Osobna kontrola aktualności orientacji; usunięto dodatkowe opóźnienie animacji igły.
- Poprawiono kasowanie pozycji podczas obsługi tej samej ramki zmiany mapy.
- Diagnostyka obu kierunków PktMon, luk/duplikatów TCP, opóźnienia kolejki i źródeł pozycji.
- Ograniczenie: długi bieg bez nowych współrzędnych nie daje ciągłego dokładnego śledzenia. Nie dodano zgadywania niepotwierdzonych opcode'ów ani Ring-Fix. Szczegóły i testy w HIDDEN-CUBE-POSITION.md.

# 0.2.3

- Automatyczne wykrywanie własnej postaci po uruchomieniu Auruma w trakcie gry, bez oczekiwania wyłącznie na pakiet logowania.
- Potwierdzenie ID z bieżących komunikatów własnych statystyk; rozpoznawanie klasy z własnych umiejętności.
- Osobna pamięć nazw/serwerów/klas bez zapisywania starych ID jako bieżącej postaci.
- Wspólna identyfikacja dla DPS, podpowiedzi arkan/itemów, petów i pozycji.
- Komunikat synchronizacji, gdy gra jeszcze nie wysłała danych postaci; ochrona przed pomyleniem gracza z osobą obok.

# 0.2.2

- Osobne plany PvE DPS dla 8 klas: 104 plany slotów wyposażenia i 40 planów kart arkan.
- Własne statystyki, rodziny kart, kolejność skilli, bonusy zestawu i warunkowe alternatywy dla każdej klasy.
- Uzupełniono m.in. Inspiring Spell, Pummel / Punishment, Gale Arrow, Wish of Concentration i Mental Focus.
- Oddzielono główne cele obrażeń od dodatkowych celów leczenia i przeżywalności.
- Dodano opis wyborów i źródeł w BIS-RESEARCH.md oraz kontrolę legalnych skilli na kartach.
- Rozpoznawanie skilli arkan toleruje typowe pomyłki OCR (I/l/1, ł zamiast t/l, 0 zamiast o).
- Dopasowania dotyczą całych nazw i muszą być jednoznaczne; inne i ucięte nazwy nie są zgadywane.
- Niepotwierdzony odczyt nie jest przedstawiany jako pewny brak skilla na karcie.

# 0.2.1

- Naprawiono pomijanie DPS/HPS graczy w dungeonach, gdy ich tożsamość nie zawierała serwera.
- W instancji uwzględniani są jej nazwani uczestnicy, również po uruchomieniu licznika w trakcie dungeona.
- Poza dungeonem pozostaje filtrowanie własnego party/force; NPC i nierozpoznane efekty nie są graczami.
- Dodano diagnostykę kwalifikowania uczestników i testy restartu, leczenia, przywołań oraz wyjścia z instancji.

# 0.2.0

- Pierwszy instalator Windows x64 z aktualizacjami z GitHub Releases.
- Kanał stabilny i testowy; pobranie aktualizacji bez przerywania gry.
- Podmiana dopiero po kliknięciu „Uruchom ponownie”.
- Ustawienia, kolekcja petów i historia poza katalogiem wymienianym przy aktualizacji.
- Import danych ze starszej wersji przenośnej.
- Zachowane DPS/HEAL, mapa petów, Hidden Cube, liczniki dusz i wskazówki sprzętu/arkan.
- Obsługa uciętych nazw kart petów i arkan.

To pierwsze wydanie instalatora; nie ma jeszcze skonfigurowanego podpisu cyfrowego wydawcy.
