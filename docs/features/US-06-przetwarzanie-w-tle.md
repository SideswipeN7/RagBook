# US-06 — Przetwarzanie dokumentu w tle (chunking + embeddingi)

**Epik:** Dokumenty | **Priorytet:** P1 | **Zależności:** US-04 | **Szacunek:** L

## Historia
Jako użytkownik po wgraniu pliku widzę, jak dokument przechodzi stany Processing → Ready (lub Failed z powodem), a po zakończeniu mogę o niego pytać — bez blokowania interfejsu.

## Kontekst / decyzje projektowe
- Przetwarzanie jako handler Wolverine na komunikat `DocumentUploaded` — lokalna kolejka in-process (durable inbox/outbox Wolverine na Postgres, żeby restart nie gubił zadań).
- **Chunking:** podział po strukturze (akapity/nagłówki dla MD, strony+akapity dla PDF) z docelowym rozmiarem ~800–1200 znaków i overlapem ~150 znaków; parametry w `ChunkingOptions`. Zachowujemy `page_number` (PDF) do cytatów.
- **Embeddingi scentralizowane** (klucz aplikacji, nie BYOK): jeden model dla całego indeksu — indeks i zapytania MUSZĄ używać tego samego modelu. Provider za abstrakcją `IEmbeddingProvider` (Voyage AI; wymiar wektora w konfiguracji, np. 1024). Zmiana modelu = pełna reindeksacja (odnotować w README).
- Ekstrakcja tekstu z PDF: biblioteka .NET (np. PdfPig); TXT/MD czytane wprost.

## Kryteria akceptacji
### AC-1: Szczęśliwa ścieżka
- GIVEN dokument w statusie Processing
- WHEN handler zakończy ekstrakcję, chunking i embeddingi
- THEN chunki z wektorami zapisane w bazie, `Status=Ready`, `ChunkCount` uzupełnione; UI odzwierciedla zmianę bez odświeżania strony (polling co 2 s lub SSE — patrz Zakres)

### AC-2: Plik nieczytelny
- GIVEN PDF zaszyfrowany/uszkodzony lub bez warstwy tekstowej
- WHEN handler nie może wyekstrahować tekstu
- THEN `Status=Failed` + `FailureReason` (czytelny, np. „PDF nie zawiera tekstu — skany nie są obsługiwane"); UI pokazuje powód i akcję „Usuń"

### AC-3: Błąd providera embeddingów
- GIVEN chwilowy błąd/timeout providera
- WHEN handler przetwarza dokument
- THEN retry z backoffem (3 próby, polityka Wolverine); po wyczerpaniu — `Status=Failed(EmbeddingProviderError)`; częściowe chunki z nieudanego przebiegu usunięte (idempotencja)

### AC-4: Idempotencja
- GIVEN komunikat dostarczony ponownie (at-least-once)
- WHEN handler startuje dla dokumentu już Ready lub z istniejącymi chunkami
- THEN wynik końcowy jest identyczny — brak duplikatów chunków (czyszczenie po `document_id` przed zapisem lub klucz `(document_id, index)`)

### AC-5: Batchowanie embeddingów
- GIVEN dokument z 200 chunkami
- WHEN generowane są embeddingi
- THEN wywołania do providera są batchowane (np. po 64), nie per chunk

## Zakres techniczny
- **Backend:** handler `ProcessDocumentHandler`; `ITextExtractor` (per typ), `IChunker`, `IEmbeddingProvider`; durable messaging Wolverine na Postgres.
- **DB:** `chunks(id, document_id FK ON DELETE CASCADE, index, text, page_number NULL, embedding vector(1024))`; unikalny indeks `(document_id, index)`; indeks wektorowy HNSW (`vector_cosine_ops`).
- **Frontend:** status na liście (spinner/badge); polling `GET /api/documents` co 2 s tylko gdy istnieją dokumenty Processing (SSE statusów jako opcjonalne ulepszenie — odnotować, nie blokować).

## Przypadki brzegowe
- Dokument usunięty w trakcie przetwarzania → handler przerywa cicho (rekord nie istnieje).
- Bardzo krótki plik (1 akapit) → minimum 1 chunk, bez overlapu.
- Tekst z PDF w innym kodowaniu/dziwne znaki → normalizacja whitespace, usunięcie znaków sterujących.

## Poza zakresem
OCR, lokalny model embeddingów (abstrakcja na to pozwala — odnotować), reindeksacja on-demand.

## Definition of Done
AC-1–AC-5 z testami (w tym idempotencja i retry); `ChunkingOptions` i wybór modelu embeddingów opisane w README (sekcja „Pipeline indeksowania" z diagramem).
