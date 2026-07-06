# US-10 — Przenoszenie plików (drag & drop)

**Epik:** Foldery | **Priorytet:** P2 | **Zależności:** US-07, US-09 | **Szacunek:** M

## Historia
Jako użytkownik przeciągam plik na folder w drzewie (lub na root), aby szybko zmienić jego miejsce — z natychmiastową reakcją interfejsu.

## Kontekst / decyzje projektowe
- Backend to prosty `UPDATE documents SET folder_id` — cała wartość US jest we frontendzie: Angular CDK DragDrop + **optimistic update z rollbackiem** (temat na rozmowę techniczną, opisać w README).
- Fallback dostępności: menu kontekstowe „Przenieś do…" z wyborem folderu (drag&drop nie może być jedyną ścieżką).
- Zmiana folderu nie dotyka indeksu wektorowego (chunki bez zmian) — zaleta modelu „folder jako atrybut".

## Kryteria akceptacji
### AC-1: Przeciągnięcie na folder
- GIVEN plik w rootcie i folder „Umowy"
- WHEN użytkownik przeciąga plik na węzeł „Umowy" i upuszcza
- THEN UI natychmiast pokazuje plik w „Umowy" (optimistic); w tle idzie `PATCH /api/documents/{id}/folder`; po sukcesie stan zostaje

### AC-2: Rollback przy błędzie
- GIVEN scenariusz jak wyżej, ale API zwraca błąd (np. folder w międzyczasie usunięty)
- WHEN odpowiedź błędu dociera
- THEN plik wraca wizualnie do poprzedniego miejsca + toast z powodem

### AC-3: Wskaźniki celu
- GIVEN trwające przeciąganie
- WHEN kursor jest nad prawidłowym celem (folder lub strefa root)
- THEN cel jest podświetlony; cele nieprawidłowe (sekcja Demo, sam plik) nie reagują

### AC-4: Przeniesienie do roota
- GIVEN plik w folderze
- WHEN użytkownik upuszcza go na strefę root
- THEN `folder_id = NULL`

### AC-5: Fallback bez myszy
- GIVEN użytkownik korzystający z menu kontekstowego pliku
- WHEN wybiera „Przenieś do…" i wskazuje folder
- THEN efekt identyczny jak przy drag&drop

## Zakres techniczny
- **Backend:** slice `Documents/Move` (walidacja: własność pliku i folderu docelowego, folder istnieje; dokument demo → `ReadOnlyResource`).
- **Frontend:** `DragDropModule` (połączone drop-listy na węzłach drzewa), stan optimistic w store, animacja upuszczenia.

## Przypadki brzegowe
- Upuszczenie pliku na folder, w którym już jest → no-op (bez wywołania API).
- Przeciąganie podczas Processing → dozwolone (folder to metadana, nie wpływa na pipeline).

## Poza zakresem
Drag&drop wielu plików naraz (bulk ma własny UX — US-12), reordering plików w folderze.

## Definition of Done
AC-1–AC-5 z testami (backend integracyjne, frontend przynajmniej optimistic/rollback unit); nagranie GIF do README.
