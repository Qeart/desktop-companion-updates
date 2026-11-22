# Plany BiS klas — PvE DPS, Global Season 1

Przegląd: 22 listopada 2026. Zakres: 8 klas wersji Global, 13 kategorii sprzętu
na klasę i 5 kart arkan na klasę. `Elementalist` jest identyfikatorem Spiritmastera
w programie. Brawler ma identyfikator protokołu, ale nie ma sprawdzonego profilu
Global; program nie podstawia mu planu innej klasy.

To wybrane, opisane warianty PvE DPS. Nie są obliczeniem DPS całego ekwipunku.
Kolejność priorytetów nie oznacza, że przedmiot musi mieć wszystkie wymienione
linie. Dobre +1 zależy od poziomów skilli, specjalizacji i posiadanych arkan.
Wartości maksymalnego rolla zależą od konkretnego przedmiotu i jego poziomu.

## Sposób sprawdzenia

- Dane klienta EU/NA publikowane przez Aion2.gg: pule skilli, podział kart,
  powiązanie Vigor/Magic ze statystyką oraz podstawowe zasady Soul Binding.
  Strony wskazują klient 2.0.10.0, ekstrakcja 5 listopada 2026.
- Zalecenia klasowe porównano z osobnymi poradnikami Global i analizami buildów
  nerfplz. Popularność w próbce postaci jest obserwacją, nie dowodem najlepszego DPS.
- Materiał SolAshura dla Templara i strona Evripidesa dla Spiritmastera są
  dodatkowymi źródłami autora konkretnego buildu.
- Aion2-guides rozdziela źródła Global i KR/TW. Koreańskie udziały kart i nowsze
  zestawy nie są bezpośrednio przenoszone do Global. Część poradników łączy
  leczenie lub tankowanie z obrażeniami; te cele nie stają się głównymi celami DPS.
- Tam, gdzie źródła się różnią, wybór oraz alternatywa są jawne. Nie dokładamy
  niesprawdzonych progów Accuracy, krytyka ani automatycznej oceny jakości rolla.

W bazie wszystkie 104 plany sprzętu i 40 planów kart zapisano przy właściwej
klasie. Katalog nie ma wspólnego planu zastępczego. Statystyki mogą się powtarzać
między klasami, kiedy dana linia jest przydatna dla obu — nie tworzymy pozornych
różnic, takich jak Intelligence dla maga zamiast występującego w tej grze Might.

## Wybrane warianty i rozbieżności

| Klasa | Vigor / Magic | Kompas | Lustro | Dlaczego / alternatywa |
|---|---|---|---|---|
| Chanter | 3 / 2 | Freedom | Wisdom | Wariant ofensywny pod Double Chance. 4 Vigor z Illusion jest osobną opcją, zalecaną m.in. przez Aion2.gg. |
| Gladiator | 3 / 2 | Freedom | Wisdom | Accuracy przy ataku z przodu i Double Chance; zestaw MP może pomóc w długiej walce. Illusion daje opcję 4 Vigor. |
| Templar | 3 / 2 | Freedom | Wisdom | Wariant Global z Accuracy. Death ma sens przy wystarczającej Accuracy; nie narzucamy 4 Magic na podstawie koreańskich udziałów. |
| Assassin | 4 / 1 | Freedom | Illusion | Cooldowny i Heart Gore; w tym wariancie lustro nie jest kopiowane z Chantera. |
| Ranger | 2 / 3 | Death | Wisdom | Deadshot / Gale Arrow i statystyki ofensywne. Gdy brakuje trafień, Freedom; pełne 4 Vigor wymaga również Illusion. |
| Sorcerer | 4 / 1 | Freedom | Illusion | Wariant Global Wish of Concentration / Hellfire. Nie kopiujemy wyłącznie starszego priorytetu Hellfire. Wisdom jest alternatywą. |
| Spiritmaster | 3 / 2 | Freedom | Wisdom | Mental Focus, Spirit Strike i Elemental Fusion. Wariant Evripidesa dopuszcza Illusion zamiast Wisdom. |
| Cleric | 4 / 1 | Freedom | Illusion | Wariant Condemnation / Bolt pod cooldowny. Death zamiast Freedom lub inna rodzina pergaminu są wariantami; celów leczenia nie nazywamy BiS DPS. |

W każdym wybranym planie kielich i pergamin są Vigor, dzwon Magic. Vigor daje
bonus przy HP co najmniej 70%; sam program nie potwierdza wyposażenia całego
zestawu. Zmiana pojedynczej karty może zdjąć jego próg. Nie istnieje automatyczny
odczyt pełnych statystyk i poziomów postaci, który uzasadniałby zmianę wariantu
bez wiedzy gracza.

## Korekty względem poprzedniej bazy

- Usunięto jeden wpisany w kod plan 4 Vigor dla wszystkich klas.
- Chanter: Inspiring Spell wraca do priorytetów pasywnych. Incandescent Blow
  pozostaje pierwszym celem kompasu. Earth's Promise jest celem warunkowym.
- Gladiator: Attack Preparation i Experienced Counterstrike poprzedzają
  pasywki dodatkowe; Overhead Slam i Rending Blow prowadzą cele aktywne.
- Templar: uzupełniono Pummel / Punishment na kielichu oraz Insulting Roar
  na dzwonie. Defensywnych skilli lustra nie umieszczono w głównych celach DPS.
- Assassin: odrębny plan z Illusion; Heart Gore, Insignia Explosion i Quick Slice
  współdzielą pergamin. Krytyk ma inną rolę niż u Chantera.
- Ranger: Gale Arrow jest ważnym celem kielicha/kompasu; Focused Eye na lustrze.
- Sorcerer: dodano Wish of Concentration i Robe of Earth do właściwych celów.
- Spiritmaster: Mental Focus ma wyższy priorytet niż cele obronne;
  Summon: Fire Spirit / Elemental Fusion nie są przypisywane do kompasu.
- Cleric: Bolt i Divine Aura weszły do planu obrażeń. Healing Enhancement
  i Radiant Benediction są oddzielnymi celami użyteczności.
- Double Chance na naramiennikach ma potwierdzenie w opisie sprzętu Global
  Hwadesa oraz w tabeli slotów; wcześniej ta linia była pominięta jako sporna.
- Broń/off-hand zachowują priorytet statystyk obrażeń. Wskazówki +1 do aktywnych
  skilli skupiają się na pierścieniach, zamiast sugerować wypieranie linii broni.

## Źródła

Mechanika i sloty:

- [Pule Soul Binding — Aion2.gg](https://aion2.gg/guides/soulbind-and-substats-explained)
- [Statystyki według slotu — RaidRelay](https://raidrelay.com/aion-2-best-stats-guide)

Każda klasa ma też własne źródła zapisane w obu plikach JSON:

| Klasa | Dane klienta i porady | Porównanie buildu |
|---|---|---|
| Chanter | [Aion2.gg](https://aion2.gg/guides/chanter-pve-build) | [notXeon / Arthars / WallyJTV, zestawienie](https://aion2-guides.info/en/chanter/) |
| Gladiator | [Aion2.gg](https://aion2.gg/guides/gladiator-pve-build) | [nerfplz PvE](https://aion.nerfplz.com/builds/gladiator/pve-overhead-slam/), [porównanie źródeł](https://aion2-guides.info/en/gladiator/) |
| Templar | [Aion2.gg](https://aion2.gg/guides/templar-pve-build) | [SolAshur](https://solashur.com/builds/templar-global-build.html), [nerfplz PvE](https://aion.nerfplz.com/builds/templar/pve-judgment/), [Global kontra KR](https://aion2-guides.info/en/templar/) |
| Assassin | [Aion2.gg](https://aion2.gg/guides/assassin-pve-build) | [nerfplz PvE](https://aion.nerfplz.com/builds/assassin/pve-heart-gore/), [Arthars / XifanGG, zestawienie](https://aion2-guides.info/en/assassin/) |
| Ranger | [Aion2.gg](https://aion2.gg/guides/ranger-pve-build) | [nerfplz PvE](https://aion.nerfplz.com/builds/ranger/pve-deadshot/), [Fubgun / Whelps, zestawienie](https://aion2-guides.info/en/ranger/) |
| Sorcerer | [Aion2.gg](https://aion2.gg/guides/sorcerer-pve-build) | [Hwades / EUTOPIA, zestawienie i odnośniki](https://aion2-guides.info/en/sorcerer/) |
| Spiritmaster | [Aion2.gg](https://aion2.gg/guides/spiritmaster-pve-build) | [Evripides Global](https://aion2sm.com/global/), [nerfplz PvE](https://aion.nerfplz.com/builds/spiritmaster/pve-fire-spirit/), [porównanie](https://aion2-guides.info/en/elementalist/) |
| Cleric | [Aion2.gg](https://aion2.gg/guides/cleric-pve-build) | [nerfplz Battle Cleric](https://aion.nerfplz.com/builds/cleric/pve-battle-cleric/), [Whelps / Lucia, zestawienie](https://aion2-guides.info/en/cleric/) |

Przechowujemy krótkie nazwy statystyk i skilli oraz własne opisy wyborów.
Treść poradników i ilustracje autorów nie są kopiowane.

## Walidacja

Testy ładują rzeczywiste dostarczane pliki JSON. Sprawdzają kompletność wszystkich
klas/slotów, legalne pule 6+6 aktywnych i 5+5 pasywnych, zgodność celów z kartą,
rozdzielenie aktywnych i pasywnych linii sprzętu, rodziny/statystyki kart,
liczbę kart Vigor, zmianę klasy i brak zastępczego profilu. Zaznaczenie statystyki
na zielono oznacza jej odczyt, a nie maksymalny roll ani gotowy zestaw.

Testy automatyczne i zrzuty ekranu nie zastępują testu tooltipów na każdej
z ośmiu klas w grze. Ten etap pozostaje do sprawdzenia na odpowiednich postaciach.

`AurumMeter.exe --render-bis-catalog <katalog>` zapisuje 144 podglądy i raport
`catalogue-layout.json`, bez uruchamiania przechwytywania gry. Kontrola obejmuje
wysokości wszystkich paneli oraz zmieszczenie arkan poniżej kotwicy 670 px
przy rozdzielczości 1080p. Nie jest to pomiar OCR na tych postaciach.
