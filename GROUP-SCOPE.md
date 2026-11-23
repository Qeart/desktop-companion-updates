# Party / Force — zakres naliczania

Aurum ustawia `MeterOptions.GroupOnly=true`. Poza dungeonem solo kwalifikuje się
własna postać. Pozostali gracze muszą mieć potwierdzenie w składzie własnej grupy
oraz dopasowaną tożsamość (nick i serwer). Zasięg widoczności, klasa i CP nie
ustalają członkostwa w otwartym świecie.

W dungeonach (rozpoznana mapa instancji 6xxxxx) licznik uwzględnia nazwanych
uczestników tej instancji. Gra może nie wysyłać ich domowego serwera, a po
restarcie licznika może też zabraknąć kolejnego komunikatu o składzie. Te braki
nie odrzucają już DPS/HPS. NPC, anonimowi aktorzy rozpoznani tylko po skillu oraz
nieprzypisane efekty nadal nie tworzą wierszy graczy. Przywołania trafiają do
rozpoznanego właściciela. Po wyjściu z instancji wraca filtr party/force.

Filtr działa przed utworzeniem walki i naliczeniem obrażeń/leczenia. Obejmuje
sumy, udziały procentowe, szczegóły umiejętności i nowe zapisy historii.
Stare pliki historii zachowują pierwotnie zarejestrowaną zawartość.

Skład `02 97` jest łączony z postaciami przez nick i serwer. Składy innych grup,
np. podglądy Party Findera, są odrzucane. Niepełny skład nie usuwa potwierdzonych
członków. Czytnik obsługuje także wolne miejsca między członkami i do 12 slotów
zgodnie z rozpoznanym układem protokołu.

Test solo na docelowym kliencie wykazał 31 identyfikatorów obiektów w kanałach
pomocniczych. Pakiety HP `1B 92` i scope `06 38` NIE kwalifikują więc gracza do
rankingu. Służą wyłącznie diagnostyce. W otwartym świecie przy braku serwera
sam identyczny nick nie wystarcza. Skład jest potwierdzany przez roster obejmujący własną postać;
po uruchomieniu w trwającej grupie może być potrzebny kolejny komunikat o składzie.

Dokładne rekordy `13 92 00 00` i `1D 97 00 00` oznaczają opuszczenie grupy.
Zmiana potwierdzonego składu kończy poprzedni segment i czyści bieżące wiersze;
nowa walka obejmuje nowy skład. Zmiana lokacji unieważnia identyfikatory aktorów,
zachowując rozpoznane nazwiska/nicki składu. Przy ponownym wiązaniu własnego ID
skład jest zawieszony, aż bieżący odczyt potwierdzi tę samą nazwę i serwer właściciela.
Samo anonimowe własne ID lub klasa nie aktywują zachowanego składu. Inna postać
albo opuszczenie grupy kasuje go. Nowe składy otrzymane podczas ładowania są
sprawdzane po potwierdzeniu postaci. Skład nie jest przywracany z pliku cache.

Regresja z wersji 0.2.3–0.2.4: resolver wysyłał `LocalIdentityLostEvent` również
przy zwykłej zmianie lokacji. Licznik wykonywał wtedy `ForgetSelf` dla grupy,
kasując skład zanim dotarło zdarzenie nowej mapy. Instancje ukrywały ten problem
dzięki osobnemu kwalifikowaniu nazwanych uczestników. Od 0.2.5 zdarzenie odróżnia
zmianę mapy od utraty tożsamości z innego powodu; tylko zmiana mapy pozwala
zawiesić i następnie ponownie potwierdzić skład.

Przywołania są przypisywane do potwierdzonego właściciela. Nierozpoznane efekty
nie stają się osobnymi graczami; do 5 sekund ich zdarzeń może zostać przypisane
po otrzymaniu wiarygodnego powiązania właściciela. Bufor jest ograniczony
do 1024 zdarzeń i czyszczony przy zmianie składu. Brak potwierdzonego właściciela
może oznaczać pominięcie części obrażeń przywołania.

Diagnostyka: `session-status.json` zawiera PlayerScope=PartyOrForce lub
DungeonParticipants i Group (GroupOnly, SelfKnown, NamedMembers, MembershipSource,
MapId, Players z flagą Eligible, ObservedActors).
`AwaitingGroupOwnerConfirmation` informuje o oczekiwaniu na potwierdzenie
właściciela zachowanego składu.
ObservedActors to liczba obserwowanych obiektów, a nie liczba członków grupy.

Testy obejmują: obcy gracz w otwartym świecie nie rozpoczyna walki i nie zwiększa DPS/HEAL,
zgodność sum i udziałów, nicki na różnych serwerach, spóźniona identyfikacja,
wyjście i zmiana składu, wygaśnięcie potwierdzenia aktora, przywołania,
niekompletne listy, odrzucanie samych wiadomości pomocniczych i 12-osobowy skład.
Regresja dungeonów obejmuje brak serwera, brak rosteru, restart z cache, podgrupę,
sumy i udziały DPS/HPS, historię, przywołania i przywrócenie filtra po wyjściu.
Testy integracji z resolverem obejmują party/12-osobowy force po zmianie lokacji,
nowe ID aktorów, DPS i leczenie, obcego gracza, zmianę postaci/serwera, wyjście
podczas ładowania i nowy skład otrzymany przed potwierdzeniem własnej postaci.
Weryfikacja na żywo w party w Abyssie pozostaje do wykonania — przy przygotowaniu
0.2.5 użytkownik grał solo. Brak bieżącego rosteru po uruchomieniu Auruma w już
istniejącej grupie oraz brak serwera w tożsamości innego gracza nadal wymagają
potwierdzonych danych; nie kwalifikujemy z tego powodu wszystkich osób w pobliżu.

Układy kanałów grupowych, opuszczenia grupy i wolnych slotów porównano
z A2Tools-DPS-Meter `src-tauri/src/capture/stream_processor.rs` (GPL-3.0).
Nie korzystamy z funkcji presence ani nie wysyłamy danych grupy do tej usługi.
