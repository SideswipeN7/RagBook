# US-15 — Streaming odpowiedzi (SSE)

**Epik:** Czat / RAG | **Priorytet:** P1 | **Zależności:** US-14 | **Szacunek:** M

## Historia
Jako użytkownik widzę odpowiedź generowaną na żywo, token po tokenie, z możliwością przerwania — aby korzystanie z czatu było płynne i responsywne.

## Kontekst / decyzje projektowe
- Łańcuch: Anthropic .NET SDK (streaming) → endpoint SSE w ASP.NET Core (`text/event-stream`) → Angular (fetch + ReadableStream; EventSource nie wspiera POST, więc fetch-stream).
- Protokół zdarzeń SSE (kontrakt frontendu): `event: delta` (fragment tekstu), `event: sources` (lista cytatów po zakończeniu — US-16), `event: done` (metadane końcowe), `event: error` (kod + komunikat).
- Przerwanie: `AbortController` po stronie klienta → anulowanie `CancellationToken` → anulowanie wywołania SDK (nie płacimy za niepotrzebne tokeny).

## Kryteria akceptacji
### AC-1: Strumieniowanie
- GIVEN zadane pytanie
- WHEN generowana jest odpowiedź
- THEN pierwsze tokeny pojawiają się w UI niezwłocznie po rozpoczęciu generacji; tekst dopisywany płynnie (bez migotania całego bloku)

### AC-2: Przerwanie przez użytkownika
- GIVEN trwający streaming
- WHEN użytkownik klika „Zatrzymaj"
- THEN strumień zamknięty; częściowa odpowiedź pozostaje w rozmowie oznaczona jako przerwana; wywołanie do providera anulowane (weryfikowalne w logach/mocku)

### AC-3: Błąd w trakcie strumienia
- GIVEN streaming przerwany błędem providera/sieci w połowie
- WHEN klient odbiera `event: error` lub strumień urywa się bez `done`
- THEN UI pokazuje częściowy tekst + wyraźny komunikat błędu z akcją „Spróbuj ponownie" (nie „urwany tekst bez wyjaśnienia")

### AC-4: Kolejność zdarzeń
- GIVEN poprawna generacja
- WHEN strumień się kończy
- THEN sekwencja to: N×`delta` → `sources` → `done`; frontend renderuje cytaty dopiero po `sources`

### AC-5: Rozłączenie klienta
- GIVEN użytkownik zamyka kartę w trakcie streamingu
- WHEN serwer wykrywa rozłączenie
- THEN `CancellationToken` anuluje generację (brak wiszących wywołań)

## Zakres techniczny
- **Backend:** endpoint `POST /api/chat/stream` zwracający SSE; `IAsyncEnumerable` z SDK mapowane na zdarzenia; heartbeat/comment co ~15 s (proxy Cloud Run); poprawne nagłówki (`Cache-Control: no-store`, wyłączony buffering).
- **Frontend:** serwis fetch-stream z parserem SSE; stan wiadomości: streaming/complete/interrupted/error; przycisk Stop; auto-scroll z możliwością „odklejenia".
- **Infra:** weryfikacja działania SSE na Cloud Run (timeout requestu ustawiony powyżej maksymalnego czasu generacji).

## Przypadki brzegowe
- Bardzo szybki koniec (krótka odpowiedź) → sekwencja zdarzeń nadal pełna.
- Dwa pytania wysłane szybko po sobie → poprzedni strumień anulowany, tylko najnowszy aktywny (jedna aktywna generacja per rozmowa).

## Poza zakresem
Wznowienie strumienia po zerwaniu, streaming przez WebSocket.

## Definition of Done
AC-1–AC-5 (backend testowany na zamockowanym SDK, parser SSE unit-testowany); protokół zdarzeń opisany w README (sekcja „Streaming SSE").
