# Stabilna linia 0.2.7

Baza: 0.2.6, commit 347547cbc7f3236f74062b76db77ea696a139c53. Na prośbę
użytkownika z 2026-11-26 cofnięto eksperymentalny moduł bossów i zmiany beta.
Historia Git zachowuje poprzedni kod. Pety, sklep, mapa Aurum, sprzęt/arkana,
Hidden Cube oraz DPS/HEAL pochodzą ze stabilnej bazy. Dodatki tej wersji to
naprawa wariantu CP oraz szacowany czas zabicia bossa.

## CP

W zapisach zgłoszonego dungeona są obrażenia wszystkich pięciu uczestników,
lecz CP innych niż własna postać wynosi 0. To nie jest wyłącznie problem etykiety.
Czytnik `02 97` szukał w drugiej części rekordu tego samego serwera co w DBID.
Odwiedzający może mieć tam numer aktualnego świata zamiast macierzystego serwera.
Taki wpis urywał odczyt dalszych członków, czasem także własnej postaci potrzebnej
do potwierdzenia składu.

Czytnik zachowuje istniejący wariant i dodaje wariant odwiedzających: wiarygodny
numer świata, znacznik 1–4 i CP w dozwolonym zakresie. Znacznik chroni przed
odczytem o jeden bajt za wcześnie i CP pomnożonym przez 256. Tożsamość pozostaje
przypisana do serwera macierzystego. Nie zgadujemy CP z DPS i nie rozszerzamy
uprawnień do rankingu na podstawie samej mocy.

Układ porównano z udokumentowanymi testami A2Tools-DPS-Meter (GPL-3.0),
`src-tauri/src/capture/stream_processor.rs`: `roster_reads_a_member_on_another_world`
i `roster_reads_a_visitors_combat_power_on_any_world`. Implementacja i krótkie
syntetyczne rekordy regresji są w Aurumie; nie dołączamy prywatnych pakietów.
Brak pakietu składu nadal oznacza brak wiarygodnego CP. Na docelowym kliencie
wariant i kompletność składu wymagają jeszcze potwierdzenia w grupie.

## Czas zabicia

`BossKillTimeEstimator` wykorzystuje potwierdzone odczyty HP bieżącego bossa:
pozostałe HP / tempo ubywania HP w oknie około 10 s. Dzięki temu pomija obrażenia
addów, odmienne przeliczniki obrażeń na HP i niepełny ranking uczestników.
To bieżąca prognoza przy utrzymaniu ostatniego tempa, bez wiedzy o przyszłych fazach.

Minimum obserwacji to 3 s. Brak świeżego HP lub spadku HP przez ponad 3 s ukrywa
liczbę. Leczenie, luka odczytu i zmiana bossa kasują stare próbki. Kolejność czasu
jest walidowana, próbki są ograniczone do około 100 na bossa. Szacunek nie jest
utrwalany ani odtwarzany z historii. Znak ≈ oraz podpowiedź wskazują ograniczenia.

431 testów obejmuje wizytę na innym świecie, przesunięcie bajtu, dalszych członków,
CP po ponownym nadaniu ID, stały/zmienny DPS, szybkie próbki HP, plateau, leczenie,
nieaktualny/nieuporządkowany odczyt, addy, śmierć i zapis historii. Testy w grze
pozostają potrzebne dla aktualnego składu grupy i faz konkretnego bossa.
