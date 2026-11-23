# Menu, licznik i przechwytywanie okien

Od wersji 0.2.5 aktualizacje otwiera się przez okrągłą ikonę Aurum → Aktualizacje.
Wiersz sygnalizuje dostępną wersję i otwiera istniejące okno pobierania/restartu.
Pozostaje też dostęp z menu ikony obok zegara. Aktualizacja wymaga świadomego
kliknięcia pobrania i restartu; otwarcie menu nie przerywa walki.

Ze stopki DPS/HEAL usunięto przycisk aktualizacji, ikonę informacji i tekst
o przechwytywaniu. Pozostała Historia. Status odczytu/błędu jest w menu Aurum
w opisie licznika. Usunięto także etykietę PARTY / FORCE, w tym komunikat o solo.
Filtr członkostwa nadal działa — opis poprawki zmiany mapy jest w GROUP-SCOPE.md.

Przyczyną znikania ikony Aurum i podpowiedzi ze zrzutów było wywołanie
`SetWindowDisplayAffinity(hwnd, 0x11)`, czyli `WDA_EXCLUDEFROMCAPTURE`.
Ta flaga ukrywa okno przed obsługiwanymi metodami przechwytywania Windows.
[Dokumentacja Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity).
Usunięto ją z menu/ikony Aurum, podpowiedzi sprzętu, podpowiedzi arkan i kompasu.

Kontrola nowych HWND przez `GetWindowDisplayAffinity` potwierdziła wartość 0
dla wszystkich czterech okien. Sam test tworzył niewidoczne okna i nie zmieniał
ustawień systemowych ani uruchomionej gry. Zrzut lub nagranie konkretnym narzędziem
trzeba wykonać po uruchomieniu nowej wersji — stare okna zachowują dawną flagę.

Widoczna w przechwyceniu podpowiedź arkan nie może zostać rozpoznana jako część
gry: wiersze od nagłówka AURUM / ARKANA są odcinane przed parsowaniem i budową
próbek cache. Zapobiega to przesuwaniu podpowiedzi coraz niżej i analizie własnych
zaleceń. Testy obejmują dwie postacie nagłówka. Ponowny odczyt rzeczywistego screena
arkany, najpierw bez podpowiedzi, potem z wyrenderowaną podpowiedzią, dał ten sam
typ karty, statystykę, położenie końca Set Effects i zgodny cache obrazu.

Podgląd menu: `--render-hub <katalog>`.
Podgląd licznika: `--render-demo <katalog>` i `--render-narrow <katalog>`.
Weryfikacja odczytu arkan: `--render-arcana-advice <obraz> <katalog>`.
