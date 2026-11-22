# Mapa Aurum — pierwsza wersja

Otwieranie/zamykanie: **Ctrl+Shift+G** lub menu Aurum przy zegarze.
Mapa ma własne okno, tekstury świata i stałe współrzędne występowania petów.
Nie przechwytuje obrazu gry i nie korzysta z OCR ani śledzenia obrazu.
Mapa gry pod M pozostaje obsługiwana przez grę.

Przeciąganie: lewy przycisk myszy. Zoom: rolka w miejscu kursora lub + / −.
„Cała mapa” obejmuje pełny teren; „Do peta” przybliża wybrane obszary.
„Na mnie” centruje ostatnią znaną pozycję gracza. „Śledź gracza” ponawia
centrowanie po otrzymaniu świeżej pozycji; przeciągnięcie wyłącza śledzenie.
Esc i przycisk × zamykają mapę. Można zmieniać rozmiar okna i przesuwać je
za nagłówek. Po przełączeniu na inną aplikację mapa się ukrywa.

Wyszukiwarka, filtry rodzajów oraz „Poniżej MAX” i „W tej krainie” korzystają
z tej samej kolekcji petów co licznik. Wybór rodzajów jest zapamiętywany.
Filtry mają teraz formę przycisków: „Do zdobycia” i „Ta kraina” oraz kafelków
rodzajów z ikonami i liczbami petów dostępnych przy bieżącym wyszukiwaniu.
Złoty stan oznacza aktywny wybór. „Wszystkie / Wyczyść” zmienia cały zestaw.
Każdy wpis listy ma lokalną miniaturę 32 px, przypisaną przez dokładny Pet ID.
Obrazy nie są pobierane podczas działania. Ich pochodzenie i licencja znajdują się
w `data/pet-icons/NOTICE.md`.
Tekstury są dostępne dla dziewięciu krain. Dane występowania petów obejmują
Altgard, Verteron i Chaotic Lower Reshanta. Brak danych nie jest uzupełniany
wymyślonymi znacznikami.

## Znacznik gracza

Złota kropka wskazuje ostatnią potwierdzoną pozycję. Źródła: wejście do krainy,
teleport i własne efekty/umiejętności postaci (ten sam układ pakietów co CubeArrow).
PktMon/Npcap jest wykorzystywany zgodnie z wybranym źródłem DPS; nie powstaje
drugie przechwytywanie sieci. Stan kamery mapy nie zależy od pakietów gracza.

Po trzech sekundach bez odczytu punkt przygasa i opis pokazuje wiek pozycji.
Nie są tworzone przewidywane kroki ani pozycja wyliczona z pobliskich mobów.
Przy braku pozycji użyj umiejętności albo mantry. Ciągłe śledzenie podczas
samego biegania nie jest jeszcze potwierdzone. Po zmianie postaci/krainy stary
znacznik jest odrzucany. Uruchomienie w środku sesji wykorzystuje zapisaną
tożsamość postaci; pozycja wymaga nowego pakietu gry.

## Architektura i weryfikacja

OwnedMapSurface rysuje teksturę, obszary, gracza i punkt diagnostyczny w jednym
DrawingContext pod tą samą transformacją MapViewport. Nie ma osobnego overlayu,
który próbuje podążać za obrazem gry. MapViewport przechowuje przesunięcie i skalę,
zachowuje punkt pod kursorem podczas zoomu oraz stałe współrzędne świata.

Shift+klik na mapie stawia różowy punkt testowy w wybranym miejscu terenu.
Diagnostyka: `user-data/owned-map-status.json`, błędy: `owned-map-error.txt`.
`session-status.json` zawiera tryb OwnedMap, ScreenCapture=false i ostatni
PlayerPosition wraz z czasem i źródłem danych.

184 testy jednostkowe przeszły. Test renderu rzeczywistego kontrolera WPF:
przesunięcie (160, -90) px i powrót — 0 px różnicy punktu i tekstury;
zoom 1,3× — 0,5 px różnicy. Weryfikator mierzy piksele różowego znacznika
i niezależnie dopasowuje fragment tekstury z wykluczeniem znacznika.
Test wymaga powtórzenia interakcji w działającej grze po instalacji.

Pliki kodu: OwnedMapSurface.cs, OwnedPetMapWindow.cs, OwnPetMapView.cs;
logika kamery i pozycji: AionMeter.Core/PetMaps/MapViewport.cs oraz
PlayerPositionTracker.cs. Poprzednia metoda nakładania na mapę gry pozostała
w źródłach; domyślne UseOwnPetMap=true wybiera własną mapę.

Autorstwo i licencje tekstur/danych: data/NOTICE.md oraz NOTICE.txt.
