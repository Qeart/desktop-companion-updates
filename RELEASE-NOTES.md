# 0.2.7 — stabilna baza, CP i szacowany czas zabicia

- Powrót do kodu stabilnej wersji 0.2.6. Eksperymentalny asystent bossów z 0.2.7-beta nie jest uruchamiany w tym wydaniu.
- Naprawiono odczyt CP odwiedzających z innych serwerów: aktualny świat nie musi być ich serwerem macierzystym. Walidacja znacznika zapobiega przesunięciu liczby o bajt i utracie dalszych członków grupy.
- Pod nazwą bossa pojawia się „≈ 01:25 do zabicia”. Szacunek wynika z aktualnego tempa ubywania jego HP z ostatnich około 10 s; nie zalicza obrażeń zadanych addom.
- Minimum 3 s danych, wygaśnięcie po utracie świeżych odczytów/zatrzymaniu utraty HP, ponowne zbieranie próbek po leczeniu i resecie. Brak przewidywania dla zapisanej lub zakończonej walki.
- 431 testów. Poprawka wariantu protokołu sprawdzona na syntetycznych rekordach odwiedzających; pełne potwierdzenie CP na bieżącym kliencie wymaga kolejnej grupy.

# 0.2.6

- Nowy tracker petów: automat dla wszystkich poniżej MAX lub własna zapamiętana lista wybierana przyciskiem +.
- Filtr aktualnej krainy, konkretnego regionu albo wszystkich krain; portrety, poziomy, licznik dusz i suma pozostała do MAX.
- Ukończony pet automatycznie znika z listy. Tracker korzysta ze wspólnego postępu petów, bez OCR i drugiej sesji przechwytywania.
- Kliknięcie peta otwiera i kadruje jego obszary na mapie Aurum. Dystans do znanych spawnów tylko z aktualnych danych pozycji.
- Przesuwanie, zmiana rozmiaru, kłódka i zapis ustawień. Otwieranie z menu Aurum lub Ctrl+Shift+T.
- Oryginalna złoto-czarna ikona Aurum w 7 rozmiarach dla EXE, zasobnika, skrótów, instalatora i menu.
- 416 testów oraz kontrola przycisków WPF, ikony i podglądu 5 → 4 pety po osiągnięciu MAX. Szczegóły w PET-TRACKER.md.

# 0.2.5

- Aktualizacje w głównym menu okrągłej ikony Aurum, ze wskazaniem dostępnej wersji.
- Czystszy licznik DPS/HEAL: ze stopki usunięto aktualizacje, ikonę informacji i opis przechwytywania; usunięto napis PARTY / FORCE i etykietę solo.
- Ikona Aurum, podpowiedzi sprzętu/arkan i kompas są dostępne dla zrzutów ekranu.
- Odczyt arkan pomija własną podpowiedź, aby nie przesuwać jej i nie analizować własnych zaleceń.
- Poprawiono kasowanie potwierdzonego party/force podczas zmiany lokacji. Skład wraca po potwierdzeniu tej samej postaci i serwera, z nowymi ID aktorów.
- Zachowano odrzucanie obcych graczy, zmianę postaci i opuszczenie grupy. Nie dodano zgadywania składu z osób w pobliżu.
- 406 testów; regresja party/force po zmianie mapy odtworzona przez rzeczywisty przepływ resolvera. Test na żywo w party w Abyssie pozostaje do wykonania.

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
