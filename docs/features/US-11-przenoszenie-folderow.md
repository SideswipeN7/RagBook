# US-11 — Przenoszenie folderów (z poddrzewem)

**Epik:** Foldery | **Priorytet:** P2 | **Zależności:** US-09, US-10 | **Szacunek:** M

## Historia
Jako użytkownik przeciągam folder do innego folderu (lub do roota) wraz z całą zawartością, aby reorganizować strukturę bez przenoszenia plików pojedynczo.

## Kontekst / decyzje projektowe
- Przeniesienie = aktualizacja `parent_id` folderu + **jeden UPDATE prefiksu `path` u wszystkich potomków**: `UPDATE folders SET path = replace(path, @oldPrefix, @newPrefix) WHERE path LIKE @oldPrefix || '%'` — w jednej transakcji. Dokumenty nie wymagają zmian (`folder_id` wskazuje ten sam folder).
- **Walidacja cyklu:** nowy rodzic nie może być potomkiem przenoszonego folderu — sprawdzenie `newParent.path LIKE moved.path || '%'` (jedno porównanie, zaleta materialized path).
- **Walidacja głębokości:** głębokość poddrzewa po przeniesieniu ≤ 3 — max liczba segmentów w poddrzewie + głębokość celu.

## Kryteria akceptacji
### AC-1: Przeniesienie z poddrzewem
- GIVEN struktura `Umowy/2026` z plikami w obu folderach
- WHEN użytkownik przeciąga „Umowy" do folderu „Archiwum"
- THEN powstaje `Archiwum/Umowy/2026`; wszystkie `path` potomków zaktualizowane; wszystkie pliki widoczne w nowych miejscach; scope czatu (US-13) po „Archiwum" obejmuje przeniesione dokumenty

### AC-2: Blokada cyklu
- GIVEN folder „A" z podfolderem „A/B"
- WHEN użytkownik próbuje przenieść „A" do „A/B"
- THEN `Result.Failure(CircularMove)`; UI nie podświetla potomków jako celu już podczas przeciągania

### AC-3: Blokada głębokości
- GIVEN folder z poddrzewem o głębokości 2, cel na poziomie 2
- WHEN próba przeniesienia (wynik: głębokość 4)
- THEN `Result.Failure(MaxDepthExceeded)` z czytelnym komunikatem

### AC-4: Konflikt nazwy w celu
- GIVEN w celu istnieje folder o tej samej nazwie
- WHEN próba przeniesienia
- THEN `Result.Failure(DuplicateFolderName)` (bez auto-merge — świadome uproszczenie)

### AC-5: Optimistic + rollback
- GIVEN przeciągnięcie folderu
- WHEN API zwraca błąd
- THEN drzewo wraca do stanu sprzed operacji + toast (spójnie z US-10)

## Zakres techniczny
- **Backend:** slice `Folders/Move` — transakcja: walidacje (własność, cykl, głębokość, nazwa) → update `parent_id` → update prefiksów `path`; test integracyjny na poprawność wszystkich ścieżek po przeniesieniu.
- **Frontend:** rozszerzenie DnD z US-10 o węzły folderów; wyłączanie nieprawidłowych celów w trakcie przeciągania.

## Przypadki brzegowe
- Przeniesienie do własnego aktualnego rodzica → no-op.
- Równoległe przeniesienia tego samego folderu → druga transakcja działa na nieaktualnym prefiksie; walidacja prefiksu w transakcji (odczyt `path` FOR UPDATE).

## Poza zakresem
Merge folderów o tej samej nazwie, kopiowanie folderów.

## Definition of Done
AC-1–AC-5 z testami (obowiązkowo test aktualizacji prefiksów i cyklu); fragment README „Przenoszenie poddrzewa jednym UPDATE".
