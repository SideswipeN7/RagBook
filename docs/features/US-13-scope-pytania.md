# US-13 — Zakres pytania (scope czatu)

**Epik:** Czat / RAG | **Priorytet:** P1 | **Zależności:** US-06, US-09 | **Szacunek:** M

## Historia
Jako użytkownik wybieram zakres, w którym system szuka odpowiedzi: wszystkie dokumenty / wybrany folder (wraz z poddrzewem) / pojedynczy plik — aby odpowiedzi pochodziły z właściwego kontekstu.

## Kontekst / decyzje projektowe
- Scope realizowany jako **pre-filtering metadanych przed wyszukiwaniem wektorowym** — jedna tabela chunków, filtr w zapytaniu (nie osobne indeksy per folder). Kluczowa decyzja architektoniczna projektu (README + rozmowa techniczna).
- Folder obejmuje poddrzewo przez prefix match: `f.path LIKE @scopePath || '%'` (materialized path z US-09).
- Wzorzec zapytania:
```sql
SELECT c.id, c.text, c.page_number, d.file_name,
       c.embedding <=> @queryEmbedding AS distance
FROM chunks c
JOIN documents d ON d.id = c.document_id
LEFT JOIN folders f ON f.id = d.folder_id
WHERE d.user_session_id = @session
  AND d.status = 'Ready'
  AND (
        @scopeType = 'all'
     OR (@scopeType = 'document' AND d.id = @documentId)
     OR (@scopeType = 'folder' AND f.path LIKE @scopePath || '%')
  )
ORDER BY c.embedding <=> @queryEmbedding
LIMIT @topK;
```
- Scope jest atrybutem **rozmowy** (zapamiętany per konwersacja); zmiana scope w trakcie = kolejne pytania używają nowego zakresu.

## Kryteria akceptacji
### AC-1: Selektor zakresu
- GIVEN otwarty czat
- WHEN użytkownik rozwija selektor przy polu pytania
- THEN widzi opcje: „Wszystkie dokumenty", drzewo folderów, listę plików (tylko Ready); wybór jest widoczny jako chip nad polem pytania

### AC-2: Scope folderu obejmuje poddrzewo
- GIVEN folder „Umowy" z podfolderem „Umowy/2026" (pliki w obu)
- WHEN pytanie w scope „Umowy"
- THEN retrieval przeszukuje chunki plików z „Umowy" ORAZ „Umowy/2026" (test integracyjny na zapytaniu)

### AC-3: Scope pliku
- GIVEN wybrany pojedynczy plik
- WHEN pytanie
- THEN retrieval ograniczony do chunków tego pliku; cytaty wskazują wyłącznie ten plik

### AC-4: Scope trwały w rozmowie
- GIVEN rozmowa rozpoczęta w scope „Umowy"
- WHEN użytkownik zadaje kolejne pytania bez zmiany
- THEN każdy retrieval używa „Umowy"; scope widoczny przy każdej odpowiedzi (metadana wiadomości)

### AC-5: Pusty scope
- GIVEN folder bez dokumentów Ready
- WHEN pytanie w jego scope
- THEN natychmiastowa odpowiedź stanu „Brak dokumentów w wybranym zakresie" (bez wywołania LLM)

## Zakres techniczny
- **Backend:** typ `ChatScope { Type: All|Folder|Document, Id? }` walidowany względem sesji; scope zapisany na `Conversation` i na każdej wiadomości (audyt).
- **Frontend:** komponent selektora (reużywa dane drzewa z US-07), chip aktywnego scope, obsługa zmiany w trakcie rozmowy.

## Przypadki brzegowe
- Folder/plik ze scope usunięty w trakcie rozmowy → kolejne pytanie zwraca `ScopeNotFound`; UI proponuje przełączenie na „Wszystkie".
- Dokumenty Processing w zakresie → pomijane (tylko Ready), UI informuje delikatnie („1 dokument nadal się przetwarza").

## Poza zakresem
Scope wielokrotny (kilka folderów naraz), wykluczenia („wszystko oprócz X").

## Definition of Done
AC-1–AC-5 z testami (obowiązkowo test prefix match na poddrzewie); zapytanie scope z komentarzem w README (sekcja „Hybrid filtering").
