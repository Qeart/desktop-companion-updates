# Party / Force — zakres naliczania

Aurum ustawia `MeterOptions.GroupOnly=true`. Solo kwalifikuje się własna postać.
Pozostali gracze muszą mieć potwierdzenie w składzie własnej grupy oraz dopasowaną
tożsamość (nick i serwer). Zasięg widoczności, klasa i CP nie ustalają członkostwa.

Filtr działa przed utworzeniem walki i naliczeniem obrażeń/leczenia. Obejmuje
sumy, udziały procentowe, szczegóły umiejętności i nowe zapisy historii.
Stare pliki historii zachowują pierwotnie zarejestrowaną zawartość.

Skład `02 97` jest łączony z postaciami przez nick i serwer. Składy innych grup,
np. podglądy Party Findera, są odrzucane. Niepełny skład nie usuwa potwierdzonych
członków. Czytnik obsługuje także wolne miejsca między członkami i do 12 slotów
zgodnie z rozpoznanym układem protokołu.

Test solo na docelowym kliencie wykazał 31 identyfikatorów obiektów w kanałach
pomocniczych. Pakiety HP `1B 92` i scope `06 38` NIE kwalifikują więc gracza do
rankingu. Służą wyłącznie diagnostyce. Przy braku serwera sam identyczny nick
nie wystarcza. Skład jest potwierdzany przez roster obejmujący własną postać;
po uruchomieniu w trwającej grupie może być potrzebny kolejny komunikat o składzie.

Dokładne rekordy `13 92 00 00` i `1D 97 00 00` oznaczają opuszczenie grupy.
Zmiana potwierdzonego składu kończy poprzedni segment i czyści bieżące wiersze;
nowa walka obejmuje nowy skład. Zmiana lokacji unieważnia identyfikatory aktorów,
zachowując rozpoznane nazwiska/nicki składu.

Przywołania są przypisywane do potwierdzonego właściciela. Nierozpoznane efekty
nie stają się osobnymi graczami; do 5 sekund ich zdarzeń może zostać przypisane
po otrzymaniu wiarygodnego powiązania właściciela. Bufor jest ograniczony
do 1024 zdarzeń i czyszczony przy zmianie składu. Brak potwierdzonego właściciela
może oznaczać pominięcie części obrażeń przywołania.

Diagnostyka: `session-status.json` zawiera PlayerScope=PartyOrForce i Group
(GroupOnly, SelfKnown, NamedMembers, MembershipSource=OwnRoster, ObservedActors).
ObservedActors to liczba obserwowanych obiektów, a nie liczba członków grupy.

203 testy przeszły, w tym: obcy gracz nie rozpoczyna walki i nie zwiększa DPS/HEAL,
zgodność sum i udziałów, nicki na różnych serwerach, spóźniona identyfikacja,
wyjście i zmiana składu, wygaśnięcie potwierdzenia aktora, przywołania,
niekompletne listy, odrzucanie samych wiadomości pomocniczych i 12-osobowy skład. Obsługa rzeczywistego party/force wymaga
sprawdzenia w grze na docelowym kliencie.

Układy kanałów grupowych, opuszczenia grupy i wolnych slotów porównano
z A2Tools-DPS-Meter `src-tauri/src/capture/stream_processor.rs` (GPL-3.0).
Nie korzystamy z funkcji presence ani nie wysyłamy danych grupy do tej usługi.
