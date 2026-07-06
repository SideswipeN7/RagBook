# US-18 — Historia rozmowy (wieloturowość)

**Epik:** Czat / RAG | **Priorytet:** P2 (kandydat do „later") | **Zależności:** US-14, US-15, US-16 | **Szacunek:** M

## Historia
Jako użytkownik prowadzę wieloturową rozmowę, w której mogę dopytywać („a co z drugim punktem?"), a system pamięta kontekst — w ramach mojej sesji.

## Kontekst / decyzje projektowe
- Model danych: `Conversation(id, user_session_id, scope, title, created_at)` + `Message(id, conversation_id, role, content, state, sources_json, created_at)`.
- **Retrieval wykonywany od nowa dla każdego pytania** (na bazie bieżącego pytania — bez query rewriting w MVP); historia wiadomości (ostatnie N par, np. 6) dokładana do promptu jako kontekst konwersacyjny obok świeżych fragmentów. Trade-off: pytanie czysto nawiązujące („rozwiń") może mieć słabszy retrieval — odnotować w README z pomysłem na future work (condensing question).
- Cytaty (`sources_json`) zapisywane ze snippetami — historia nie zależy od istnienia chunków (spójnie z US-16 AC-4).
- Lista rozmów per sesja; „Nowa rozmowa" czyści kontekst; tytuł = pierwsze pytanie skrócone do 60 znaków (bez wywołań LLM do tytułowania).

## Kryteria akceptacji
### AC-1: Kontekst wieloturowy
- GIVEN rozmowa z pytaniem i odpowiedzią o punktach umowy
- WHEN użytkownik pyta „rozwiń punkt drugi"
- THEN prompt zawiera ostatnie wiadomości rozmowy + świeże fragmenty z retrievalu; odpowiedź odnosi się do właściwego punktu

### AC-2: Nowa rozmowa
- GIVEN aktywna rozmowa
- WHEN użytkownik klika „Nowa rozmowa"
- THEN powstaje pusta konwersacja (scope domyślny „Wszystkie" lub dziedziczony — decyzja: domyślny); poprzednia dostępna na liście

### AC-3: Wczytanie historii
- GIVEN rozmowy z poprzednich wizyt (ta sama sesja)
- WHEN użytkownik otwiera aplikację i wybiera rozmowę z listy
- THEN wiadomości renderują się z zachowanymi stanami (NoAnswerFound, Interrupted) i klikalnymi cytatami (ze snippetów)

### AC-4: Limit kontekstu
- GIVEN długa rozmowa (>N par)
- WHEN budowany jest prompt
- THEN do promptu trafia maks. N ostatnich par (konfiguracja `ChatOptions.HistoryPairs`); starsze tylko w UI

### AC-5: Izolacja
- GIVEN rozmowy sesji A
- WHEN sesja B odpytuje ich ID
- THEN 404 (spójnie z US-01)

## Zakres techniczny
- **Backend:** slices `Conversations/Create`, `Conversations/List`, `Conversations/Get`; zapis wiadomości w `Chat/AskQuestion`; budowa promptu z historią.
- **DB:** tabele `conversations`, `messages` (z `sources_json jsonb`, `state`).
- **Frontend:** panel listy rozmów, przełączanie, „Nowa rozmowa".

## Przypadki brzegowe
- Usunięcie rozmowy → hard delete z wiadomościami (kaskada); potwierdzenie.
- Rozmowa ze scope na usunięty folder → wczytuje się normalnie; nowe pytanie → `ScopeNotFound` (US-13).

## Poza zakresem
Query rewriting/condensing, wyszukiwanie w historii, eksport rozmowy, tytułowanie przez LLM.

## Definition of Done
AC-1–AC-5 z testami (obowiązkowo budowa promptu z historią i limit N par); trade-off retrievalu wieloturowego opisany w README.
