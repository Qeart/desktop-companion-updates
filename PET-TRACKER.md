# Tracker petów i ikona Aurum

Otwieranie: **ikona Aurum → Tracker petów** lub **Ctrl+Shift+T**.
Tracker jest oddzielnym, przesuwanym i skalowanym oknem. Kłódka blokuje pozycję
i rozmiar; przyciski wyboru nadal działają. Widoczność, pozycja, rozmiar i ustawienia
są zachowywane. Po pierwszej instalacji otwiera się go z menu, aby nie zasłonić gry
nowym oknem bez wyboru użytkownika.

## Dwa tryby

- **Wszystkie poniżej MAX**: lista wszystkich nieukończonych petów w wybranym widoku.
- **Własna lista**: przyciskiem **+** wybierz konkretne pety. Wybór przełącza na tryb
  ręczny. Ponowne włączenie automatu nie usuwa zapisanej własnej listy.

Domyślny widok dotyczy aktualnej krainy. Lista krain pozwala wybrać konkretną mapę
albo wszystkie krainy. Wyszukiwarka wyboru ręcznego działa po nazwie peta i nazwie
potwora, z którego można go zdobyć. Można zaznaczyć znalezione wyniki lub wyczyścić
wybór. Ukończone pety pozostają w zapisanym wyborze, ale znikają z trackera w obu
trybach. Dzięki temu ta sama lista może służyć również innej postaci.

Wiersz pokazuje portret, nazwę, osiągnięte poziomy, aktualny licznik np. 40/75,
pasek postępu i liczbę dusz do MAX. Podsumowanie liczy dusze **aż do poziomu 3**,
uwzględniając wszystkie pozostałe progi 5/25/75. Nowy pet potrzebuje łącznie 105,
a pet poziomu 2 z 40/75 — jeszcze 35. Liczba dusz nie oznacza gwarantowanej liczby
zabójstw: drop niektórych potworów ma szansę mniejszą od 100%.

Lista odświeża się co 100 ms, bez odczytywania ekranu. Korzysta z tego samego
`PetProgress` co liczniki dusz i sklepu. Nie tworzy drugiej sesji przechwytywania
ani niezależnego licznika zabójstw. Gdy nie ma zsynchronizowanej kolekcji, pokazuje
oczekiwanie, zamiast udawać, że gracz nie posiada żadnych petów.

## Mapa i odległość

Kliknięcie wiersza otwiera mapę Aurum, wybiera właściwego peta i kadruje jego obszary.
Wyszukiwanie na mapie jest czyszczone, a potrzebny rodzaj peta jest włączany, aby
wcześniejsze filtry nie ukryły wybranego peta.

Odległość dotyczy **najbliższego znanego punktu występowania**, nie aktualnej pozycji
żywego potwora. Strzałki w trackerze są względem północy, niezależne od kamery.
Współrzędne spawnów są w metrach, a bounds mapy w centymetrach. NaN, nieskończoności,
punkty poza granicami mapy i niepełne pary są pomijane.

Odległość jest pokazywana tylko z aktualnej lub krótko prognozowanej pozycji gracza.
Przy STALE, obcej mapie lub braku danych pozostaje „—”. Ograniczenia aktualizacji
pozycji podczas biegania opisuje HIDDEN-CUBE-POSITION.md; tracker ich nie obchodzi.
Krainy bez danych spawnów pokazują jasny komunikat i pozwalają przejść do wszystkich
krain. Nie są wymyślane brakujące miejsca występowania.

## Ikona

`AurumBrand.cs` zawiera oryginalny wektorowy znak: geometryczne złote A i ciemny
okrąg z metaliczną obwódką. Ten sam rysunek służy menu, nagłówkowi licznika oraz
ikonie programu. `Assets/aurum.ico` ma rozmiary 16/24/32/48/64/128/256 px.
Ikona jest osadzona w EXE i zasobach WPF, używana w zasobniku, a pakowanie Velopack
przekazuje ją także instalatorowi i skrótom. Wygenerowane PNG są w Assets.

Ikonę można odtworzyć: `AurumMeter.exe --render-brand <katalog>`.
Podgląd trackera i jego selektora: `AurumMeter.exe --render-pet-tracker <katalog>`.
Ten tryb używa wyłącznie fikcyjnej kolekcji, generuje widok automatyczny, stan po
osiągnięciu MAX i widok własnej listy. Nie uruchamia gry ani przechwytywania.

## Walidacja

Testy core obejmują automatyczny zakres, wszystkie krainy, własny wybór i jego
kolejność, usunięcie ukończonego peta po prawdziwym zdarzeniu `PetSoulsEvent`, sumę
pozostałych dusz, nieznaną kolekcję, zmianę profilu, brak mapy, najbliższy spawn,
estymację i ukrycie starych/obcych współrzędnych oraz wadliwe dane spawnów.

Diagnostyka WPF sprawdziła kliknięcie checkboxa wyboru, przełącznik automatu,
kłódkę, przekazanie poprawnego petId/mapId po kliknięciu wiersza i dopuszczenie
okna do zrzutów ekranu (`GetWindowDisplayAffinity = 0`). Odczyt ikony zasobnika
potwierdził 32×32, a plik ICO siedem rozmiarów. Wyrenderowany tracker zmniejszył
listę z 5 do 4 po otrzymaniu ostatniej duszy. Ostateczny test podniesienia soula
i otwarcia mapy w aktywnej grze pozostaje do wykonania po aktualizacji.
