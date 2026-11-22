# Śledzenie mapy petów — 22.11.2026

## Aktualny wynik testu w grze

Użytkownik potwierdził: po zatrzymaniu mapy okręgi są poprawnie ustawione,
a podczas przeciągania odrywają się od terenu. Zadanie pozostaje nierozwiązane.
Nie należy utożsamiać dokładności testu na przekształconym zdjęciu z poprawnym
zakotwiczeniem w czasie wyświetlania rzeczywistej gry.

W teście wersji z osobnymi wątkami zebrano 903 próbek, w tym 234 próbki nowych
obrazów z ruchem. Mediana od początku odczytu do aktualizacji właściwości WPF
wyniosła 24,62 ms, percentyl 95: 38,12 ms. Percentyl 95 wieku obrazu DXGI w tym
momencie: 54,01 ms. Nie jest to pomiar chwili prezentacji przez DWM na monitorze.
Przy ruchu 1000 px/s sam wiek obrazu rzędu 54 ms odpowiada różnicy 54 pikseli.
To wyjaśnia możliwe rozjeżdżanie w ruchu i powrót zgodności po zatrzymaniu,
ale nadal wymaga odróżnienia od błędu śledzenia na jednym punkcie terenu.

Wspólna transformacja, stałe współrzędne świata i LK/RANSAC są już używane.
Nowy OCR nie usuwa opóźnienia wynikającego z przechwytywania obrazu i osobnej
prezentacji nakładki. Nakładka nie ma dostępu do kamery mapy ani do chwili
prezentacji klatek gry. Nie należy obiecywać pełnej synchronizacji na podstawie
samego przyspieszenia obliczeń czy dodania kolejnej interpolacji.

Rozważane kierunki:

- Własna mapa Aurum: obraz terenu i znaczniki należą do jednej sceny i korzystają
  z tej samej transformacji w tej samej klatce. Eliminuje konieczność śledzenia
  cudzej kamery podczas przeciągania. Zmienia jednak sposób korzystania z mapy.
- Pozostanie na mapie gry: najpierw jeden jawny znacznik diagnostyczny oraz
  niezależny pomiar położenia punktu terenu w przechwyconym obrazie. Pozwala
  oddzielić błąd geometrii od opóźnienia prezentacji, bez deklaracji naprawy.

Wybór kierunku został przedstawiony użytkownikowi; zachowanie aplikacji nie
zostało zmienione w ramach tej diagnozy.

## Wykryta przyczyna

Mapa w zastanej wersji nie używała OCR. OCR Windows jest osobną funkcją sklepu
i pozostał bez zmian. Bezwzględną pozycję mapy wyznaczało dopasowanie SIFT do
obrazu referencyjnego, a kolejne klatki śledził Optical Flow.

Pełne dopasowanie wykonywało się w tej samej sekcji krytycznej co szybki odczyt.
Okresowa kalibracja lub utrata punktów blokowała następne klatki. Jej wynik
natychmiast zastępował poprzednią transformację. Punkty nie były uzupełniane,
nie było kontroli przepływu wstecznego ani resetu stanu obrazu przy ponownym M.
Pobierany pulpit mógł zawierać własne okręgi. Drobne błędy rotacji w kolejnych
dopasowaniach mogły się kumulować mimo nierotującej mapy gry.

## Wdrożona architektura

1. DXGI pobiera najnowszy dostępny obraz. Brak nowej klatki nie uruchamia ponownie
   Optical Flow. GDI pozostaje metodą zapasową z raportowaniem powodu awarii DXGI.
2. Analiza dotyczy centralnego obszaru mapy: x 18–82%, y 10,5–94,5% okna gry,
   pomniejszonego współczynnikiem odpowiadającym maksymalnie 960 px pełnej szerokości.
   Reszta interfejsu nie jest analizowana. Maska wyklucza kursor, inne okna Aurum
   i jasne napisy/ikony. Własne okręgi korzystają z WDA_EXCLUDEFROMCAPTURE;
   powodzenie tej operacji jest dostępne w diagnostyce. To nie jest gwarancja
   wykluczenia każdego możliwego znacznika interfejsu gry.
3. PetMapOpticalTracker używa piramidalnego LK, kontroli zgodności przód/tył,
   RANSAC i kontroli rozproszenia punktów. Uzupełnia punkty terenu do 240.
   Model końcowy ma wspólną skalę i przesunięcie, bez przypadkowej rotacji.
   Przy większym skoku zoomu lokalne dopasowanie ORB stanowi ścieżkę odzyskania.
4. PetMapCalibrator ma własny detektor SIFT i indeksy FLANN. Jedno zadanie na
   osobnym wątku o niższym priorytecie dopasowuje obraz do wzorca mapy. Nie czeka
   na nie ani szybki odczyt, ani renderowanie. Nie powstaje kolejka starych klatek.
   Kalibracja następuje przy otwarciu/odzyskaniu mapy i okresowo co około 2,5 s.
5. Ruch od momentu wykonania zdjęcia kalibracyjnego jest składany w osobną
   transformację. Zanim wynik z tła zostanie użyty, przeliczamy go do bieżącej
   klatki. Zmiana sesji lub utrata ciągłości ruchu unieważnia stary wynik.
6. MapPoseStabilizer nakłada zmierzony ruch i zoom natychmiast. Wygładza wyłącznie
   błąd kalibracji całej mapy: do 24 pikseli/s, z limitem kroku odpowiadającym 50 ms.
   Słabe dopasowania są odrzucane. Rozbieżność powyżej 12 px wymaga dwóch
   zgodnych kalibracji; powyżej 80 px albo przy innej mapie następuje ponowne
   pozyskanie pozycji zamiast przesuwania okręgów przez niepowiązane obszary.
7. PetMapRings przesuwa wszystkie obszary jedną MatrixTransform. Promienie są
   zapisane w jednostkach mapy i skalują się razem z nią. Ruch wokół punktu zoomu
   wynika z oszacowanego przesunięcia macierzy; nie wymaga zgadywania kursora.
8. M, powrót po utracie fokusu, rozmiar okna i zmiana regionu unieważniają stare
   śledzenie. Podczas niepewnego odczytu okręgi są ukrywane.

Phase Correlation dobrze nadaje się do samego przesuwu, ale samodzielnie nie
rozwiązuje zoomu. Wybrano istniejący LK rozszerzony o RANSAC, walidację i niezależną
kalibrację, zamiast wprowadzać kolejny model ruchu i przepisywać funkcje dodatku.

## Diagnostyka i testy

Po pierwszym teście w grze użytkownik nadal zgłosił pływanie okręgów. W zapisie
pojawiły się klatki, dla których analiza/przechwytywanie trwały około 15 ms,
a cała ścieżka aktualizacji około 120 ms. Dobre wyniki na przekształconym zdjęciu
nie potwierdziły więc działania całego interfejsu w grze.

Kolejna wersja dodaje PetMapFramePump: stały wątek przechwytywania i śledzenia,
publikujący wyłącznie najnowszy wynik. PetMapHost uruchamia okna mapy i ich
dispatcher na osobnym wątku STA, niezależnym od interfejsu DPS. Renderer nie
czeka na Task.Run ani na kolejne wyniki. SubmitDelayMilliseconds pokazuje czas
od końca analizy do pobrania wyniku przez renderer. Osobny test wątku potwierdził,
że kontynuuje przetwarzanie bez odpytywania przez UI i odrzuca nieaktualną sesję.
Ta wersja wymaga ponownego sprawdzenia wizualnego w działającej grze.

`user-data/pet-map-status.json`: przesunięcie, stosunek skal, aktualna skala
Alignment.Scale, pewność/inliery, błąd dopasowania i przepływu wstecznego, stan
kalibracji, czas zadania kalibracji, wielkość korekty, odrzucone stare wyniki,
czas przechwytywania/obliczeń/renderowania, wiek klatki DXGI i rzeczywiste
liczby odświeżeń. NewFramesPerSecond oddziela nowe obrazy od powtórzeń.

`user-data/pet-map-frames.json`: ostatnie 180 próbek do sprawdzenia przeskoków,
zapisywane w tle. `pet-map-error.txt` zawiera błędy wykonania.
W diagnostyce pole CalibrationState zastępuje proponowane korekty OCR, ponieważ
w tej ścieżce nie ma odczytu tekstowych współrzędnych.

171 testów jednostkowych obejmuje m.in. składanie transformacji, zoom wokół
punktu, korekty bez opóźniania ruchu, weryfikację dużych rozbieżności, zmianę
regionu i odrzucanie słabych odczytów.

Natywny test `work/PetMapProbe` wykorzystuje zapisany rzeczywisty obraz mapy
i znane transformacje: przeciąganie, ciągły/skokowy zoom, zatrzymanie, stałe
adnotacje, utratę obrazu, ponowne otwarcie i kalibrację w tle podczas ruchu.
Pełne wyniki są w `work/pet-map-tracking-suite.json` poza archiwum źródeł aplikacji.
Testy nie stanowią pomiaru pełnego opóźnienia nakładki w działającej grze.

Do sprawdzenia na żywo pozostają zachowanie podczas rzeczywistego przeciągania
i rolki, zmiany regionu/teleportacji oraz współpraca wykluczania nakładki z danym
trybem ekranu. Wymagają uruchomionej gry. Zerowe opóźnienie między grą a osobnym
oknem WPF nie jest gwarantowane; diagnostyka oddziela czas analizy od całej ścieżki.

Dokumentacja metod: [OpenCV LK](https://docs.opencv.org/4.13.0/dc/d6b/group__video__track.html),
[OpenCV affine/RANSAC](https://docs.opencv.org/4.13.0/d9/d0c/group__calib3d.html),
[Windows capture affinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity).
