# US-09 — CRUD folderów (hierarchia)

**Epik:** Foldery | **Priorytet:** P1 | **Zależności:** US-01 | **Szacunek:** M

## Historia
Jako użytkownik tworzę, zmieniam nazwę i usuwam foldery — także zagnieżdżone (do 3 poziomów) — aby organizować dokumenty w strukturę odpowiadającą mojej pracy.

## Kontekst / decyzje projektowe
- **Materialized path** jako reprezentacja hierarchii: `folders(id, user_session_id, name, parent_id NULL, path text)`, gdzie `path` = `/{id-root}/{id-child}/.../` (segmenty to ID). Scope po poddrzewie = `path LIKE prefix || '%'` z indeksem `text_pattern_ops` — bez rekurencyjnych CTE (kluczowa decyzja do README).
- Max głębokość **3 poziomy** — walidacja przez liczbę segmentów `path` w handlerze.
- Nazwa unikalna w obrębie rodzica (constraint `UNIQUE(user_session_id, parent_id, name)` — uwaga na NULL w `parent_id`: unikalny indeks częściowy dla roota).
- Usunięcie tylko **pustego** folderu (bez plików i podfolderów) — świadomy trade-off MVP; kaskada z potwierdzeniem jako future work.

## Kryteria akceptacji
### AC-1: Utworzenie folderu
- GIVEN użytkownik w widoku drzewa
- WHEN tworzy folder „Umowy" w rootcie, a następnie „2026" wewnątrz „Umowy"
- THEN oba foldery powstają z poprawnym `path`; drzewo pokazuje hierarchię

### AC-2: Limit głębokości
- GIVEN folder na 3. poziomie
- WHEN użytkownik próbuje utworzyć w nim podfolder
- THEN `Result.Failure(MaxDepthExceeded)`; UI komunikuje limit (i nie pokazuje opcji „Nowy folder" na 3. poziomie)

### AC-3: Unikalność nazwy
- GIVEN folder „Umowy" w rootcie
- WHEN użytkownik tworzy drugi folder „Umowy" w rootcie
- THEN `Result.Failure(DuplicateFolderName)`; w innym rodzicu nazwa „Umowy" jest dozwolona

### AC-4: Zmiana nazwy
- GIVEN folder z plikami i podfolderami
- WHEN użytkownik zmienia nazwę
- THEN nazwa zaktualizowana; `path` bez zmian (segmenty to ID, nie nazwy — rename jest O(1)); walidacja unikalności jak w AC-3

### AC-5: Usunięcie pustego / blokada niepustego
- GIVEN folder pusty → usunięcie przechodzi po potwierdzeniu
- GIVEN folder z plikiem lub podfolderem → `Result.Failure(FolderNotEmpty)` z komunikatem „Usuń lub przenieś zawartość"

### AC-6: Walidacja nazwy
- GIVEN nazwa pusta, > 100 znaków lub zawierająca `/`
- WHEN próba zapisu
- THEN `Result.Failure(InvalidFolderName)`

## Zakres techniczny
- **Backend:** slices `Folders/Create`, `Folders/Rename`, `Folders/Delete`; helper budowy/walidacji `path`; migracja z indeksami (`text_pattern_ops` na `path`, unikalność nazw).
- **Frontend:** akcje kontekstowe na węzłach drzewa (nowy folder, zmień nazwę, usuń), inline edit lub dialog.

## Przypadki brzegowe
- Równoległe utworzenie folderów o tej samej nazwie → constraint bazy łapie wyścig, handler mapuje na `DuplicateFolderName`.
- Usunięcie rodzica tuż po usunięciu ostatniego dziecka w innej karcie → walidacja pustości w transakcji.

## Poza zakresem
Kaskadowe usuwanie z zawartością, kolory/ikony folderów, ulubione.

## Definition of Done
AC-1–AC-6 z testami (w tym constraint wyścigu); sekcja README „Hierarchia folderów: materialized path" z uzasadnieniem i przykładem zapytania prefiksowego.
