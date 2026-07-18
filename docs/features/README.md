# RagBook — specyfikacja user stories (pakiet pod spec-kit)

Asystent RAG do własnych dokumentów: upload PDF/TXT/MD → indeksowanie (pgvector) → pytania w języku naturalnym → odpowiedzi streamowane z klikalnymi cytatami. Wersja case-study: BYOK (klucz Anthropic użytkownika) + tryb demo, limit 10 plików, foldery zagnieżdżone (max 3 poziomy, plik w dokładnie jednym folderze).

**Stack:** .NET (vertical slices, `Result<T>`, Wolverine) · Angular · PostgreSQL + pgvector · Anthropic .NET SDK (streaming/SSE) · embeddingi przez osobnego providera (scentralizowane) · Docker · GCP Cloud Run.

## Mapa user stories

| ID | Tytuł | Epik | Priorytet | Zależności |
|----|-------|------|-----------|------------|
| US-01 | Sesja użytkownika (izolacja danych) | Fundament | P1 | — |
| US-02 | Konfiguracja klucza AI (BYOK) | Fundament | P1 | 01 |
| US-03 | Tryb demo | Fundament | P1 | 01 (pełna wartość po 14–17) |
| US-04 | Upload dokumentu | Dokumenty | P1 | 01, 05, (09) |
| US-05 | Limit plików (quota) | Dokumenty | P1 | 01 |
| US-06 | Przetwarzanie w tle (chunking + embeddingi) | Dokumenty | P1 | 04 |
| US-07 | Drzewo folderów i lista dokumentów | Dokumenty | P1 | 04, 09 |
| US-08 | Usuwanie dokumentu | Dokumenty | P1 | 04, 05, 07 |
| US-09 | CRUD folderów (hierarchia, materialized path) | Foldery | P1 | 01 |
| US-10 | Przenoszenie plików (drag & drop) | Foldery | P2 | 07, 09 |
| US-11 | Przenoszenie folderów (z poddrzewem) | Foldery | P2 | 09, 10 |
| US-12 | Operacje zbiorcze | Foldery | P3 | 07, 08, 10 |
| US-13 | Zakres pytania (scope czatu) | Czat/RAG | P1 | 06, 09 |
| US-14 | Zadanie pytania z RAG | Czat/RAG | P1 | 02/03, 06, 13 |
| US-15 | Streaming odpowiedzi (SSE) | Czat/RAG | P1 | 14 |
| US-16 | Cytaty źródeł | Czat/RAG | P1 | 14, 15 |
| US-17 | Brak podstaw do odpowiedzi | Czat/RAG | P1 | 14 |
| US-18 | Historia rozmowy | Czat/RAG | P2 | 14–16 |
| US-19 | Obsługa błędów i stany brzegowe | Wykończenie | P1 | przekrojowo |
| US-20 | Deployment i pakiet case study | Wykończenie | P1 | wszystkie |

## Kamienie milowe — **wszystkie 20/20 ukończone ✅**

1. **M1 — Pion danych (7/7):** US-01 → US-05 → US-09 → US-04 → US-06 → US-07 → US-08. Efekt: upload → indeksacja → drzewo → delete end-to-end.
2. **M2 — Rdzeń RAG (6/6):** US-02 → US-13 → US-14 → US-15 → US-16 → US-17. Efekt: pełny czat z cytatami i groundingiem.
3. **M3 — UX folderów (3/3):** US-10 → US-11 → US-12. Efekt: drag&drop i operacje zbiorcze.
4. **M4 — Wykończenie (4/4):** US-03 → US-18 → US-19 → US-20. Efekt: demo publiczne, obsługa błędów i pakiet case study (Docker Compose + Cloud Run).

Cały roadmap zrealizowany przez przepływ spec-kit; każda historia dostarczona za zielonymi 4 tierami testów + CI.

## Jak używać ze spec-kit

- Ten katalog (`docs/features/`) zawiera po jednym pliku na US: historia, kontekst i decyzje projektowe, kryteria akceptacji (Given/When/Then), zakres techniczny, przypadki brzegowe, poza zakresem, Definition of Done.
- Rekomendowany przepływ: dla każdego kamienia milowego uruchom `/specify` podając treść plików US wchodzących w milestone (spec-kit złoży z nich spec funkcjonalny), potem `/plan` (wskaż stack z nagłówka tego README jako constraints), następnie `/tasks` i implementacja.
- Sekcje „Kontekst / decyzje projektowe" traktuj jako wiążące constraints dla `/plan` — zawierają rozstrzygnięcia (materialized path, pre-filtering pgvector, BYOK bez persystencji, fraza-sentinel), które mają być odzwierciedlone w planie, nie ponownie otwierane.
- Sekcje „Poza zakresem" przenoś do speca wprost — chronią przed rozrostem zakresu podczas generowania planu.

## Decyzje przekrojowe (obowiązują we wszystkich US)

- Izolacja danych: `UserSessionId` na każdej encji, filtr wymuszony globalnie, cudzy zasób = 404 (US-01).
- Błędy: `Result<T>` → `ProblemDetails` ze stabilnym `code`; katalog kodów w US-19.
- Hierarchia: materialized path (segmenty = ID), max 3 poziomy, nazwa unikalna w rodzicu (US-09).
- RAG: jeden model embeddingów dla całego indeksu; parametry (`TopK`, próg) w konfiguracji; grounding przez prompt + próg + sentinel (US-14/17).
- Sekrety: klucz użytkownika tylko w session store (nigdy w DB); klucze aplikacji w Secret Manager (US-02/03/20).
- Tryb demo (US-03): dokumenty demo (`Origin=Demo`) są **globalne, tylko do odczytu**, seedowane idempotentnie przy starcie pod stałym `DemoConstants.DemoSessionId` (interceptor sesji nietknięty); czytane po `Origin==Demo` z pominięciem filtra sesji (retrieval + sekcja „Demo" w drzewie). Generowanie demo idzie na **kluczu aplikacji** (`Anthropic:ApplicationKey`, tylko env/Secret Manager, nigdy w repo), z pominięciem guardu klucza sesji; limity w `DemoOptions` — per-sesja (lifetime, `chat.demo_limit_reached` → 429) i per-IP (godzinowe okno → 429 + `Retry-After`). Demo nie wlicza się do quoty (US-05). Mutacje demo przez cudzą sesję są niewidoczne (404, izolacja); sekcja „Demo" w UI nie ma akcji przenieś/usuń.
- Limity: wszystkie w konfiguracji (`QuotaOptions`, `DemoOptions`, `RagOptions`, `ChatOptions`, `BulkOptions`) — zero magic numbers.
- Operacje zbiorcze (US-12): semantyka **all-or-nothing** — serwer waliduje **wszystkie** pozycje przed jakąkolwiek zmianą i stosuje je w jednej transakcji; jeśli którakolwiek pozycja jest niepoprawna, cała operacja jest odrzucana z listą `failures: [{ id, code }]` (`422 document.bulk_validation_failed`) i **nic** się nie zmienia. Świadomy trade-off wobec „częściowego sukcesu": half-applied bulk delete/move byłby gorszy niż brak zmiany — użytkownik dostaje przewidywalny, odwracalny stan i precyzyjnie oznaczone pozycje do poprawy, kosztem konieczności ponowienia po korekcie zaznaczenia. Cudzy/nieistniejący ID raportowany jako `document.not_found` (bez ujawniania istnienia); pusta/za duża lista → `400`. Limit listy z `BulkOptions.MaxItems` (quota-ready).
- Obsługa błędów (US-19): każdy failure domenowy → `ProblemResults.Problem(Error)` → RFC 9457 `ProblemDetails { code, traceId, detail, status }`; `code` jest stabilny (`module.snake_case`). Nieobsłużone wyjątki → `GlobalExceptionHandler` → `error.unexpected` (500) bez stack trace (tylko log). **Correlation id**: jedno źródło (`Activity.Current?.Id`, fallback `TraceIdentifier`) w trzech builderach + naglówek `X-Trace-Id` na każdej odpowiedzi (`TraceHeaderMiddleware`); ten sam id trafia do logów (OTel scopes). Front: **jeden słownik** `core/error-messages.ts` (`messageForCode`) — komplet kodów, jeden fallback tylko dla nieznanych; globalny **offline banner**.

## Katalog kodów błędów (US-19)

Stabilne kody (`module.snake_case`) → status HTTP → znaczenie → zachowanie UI (komunikat ze słownika `core/error-messages.ts`).

| Kod | Status | Znaczenie | Zachowanie UI |
|---|---|---|---|
| `session.resource_not_found` | 404 | Zasób nie istnieje / cudzy | Komunikat „zasób nie istnieje" |
| `session.name_required` | 400 | Wymagana nazwa | Walidacja inline |
| `session.resource_already_exists` | 409 | Nazwa zajęta | Walidacja inline |
| `session.concurrency_conflict` | 409 | Konflikt równoległej zmiany | „Odśwież i spróbuj ponownie" |
| `document.unsupported_file_type` | 400 | Zły typ pliku | Błąd przy uploadzie |
| `document.empty_file` | 400 | Pusty plik | Błąd przy uploadzie |
| `document.not_found` | 404 | Plik nie istnieje | Notka/rollback |
| `document.read_only` | 409 | Plik demo tylko-do-odczytu | Notka przy move/delete |
| `document.bulk_validation_failed` | 422 | All-or-nothing: `failures[]` | Oznaczenie pozycji + notka |
| `document.bulk_empty` | 400 | Puste zaznaczenie | Notka |
| `document.bulk_too_large` | 400 | Za dużo pozycji | Notka |
| `quota.exceeded` | 409 | Limit liczby plików | Błąd uploadu |
| `quota.conflict` | 409 | Konflikt limitu | „Spróbuj ponownie" |
| `quota.invalid_size` | 400 | Zły rozmiar | Walidacja |
| `quota.total_size_exceeded` | 409 | Limit rozmiaru sesji | Błąd uploadu |
| `quota.file_too_large` | 400 | Plik za duży | Walidacja/upload |
| `folder.invalid_name` | 400 | Zła nazwa folderu | Walidacja inline |
| `folder.max_depth_exceeded` | 400 | Za głębokie zagnieżdżenie | Walidacja/move |
| `folder.duplicate_name` | 409 | Nazwa zajęta w rodzicu | Walidacja inline |
| `folder.not_empty` | 409 | Folder niepusty | Notka przy delete |
| `folder.not_found` | 404 | Folder nie istnieje | Notka/rollback |
| `folder.conflict` | 409 | Konflikt operacji | „Spróbuj ponownie" |
| `folder.circular_move` | 409 | Cykl w move | Notka przy move |
| `chat.scope_not_found` | 404 | Zakres nie istnieje | „Przełącz na Wszystkie" |
| `chat.invalid_question` | 400 | Puste/za długie pytanie | Walidacja inline |
| `chat.provider_rate_limited` | 429 | Provider throttluje | „Spróbuj za chwilę" |
| `chat.provider_unavailable` | 503 | Provider niedostępny | Panel „chwilowo niedostępne" + Retry |
| `chat.conversation_not_found` | 404 | Rozmowa nie istnieje | Notka |
| `chat.demo_limit_reached` | 429 | Limit pytań demo (sesja) | Licznik + zachęta BYOK |
| `chat.demo_rate_limited` | 429 | Limit demo per-IP (`Retry-After`) | „Spróbuj później" |
| `chat.demo_unavailable` | 503 | Demo niedostępne (brak app key) | „Chwilowo niedostępne" |
| `settings.invalid_api_key` | 400 | Klucz odrzucony | „Klucz odrzucony — sprawdź ustawienia" |
| `settings.validation_unavailable` | 503 | Nie można zweryfikować klucza | „Spróbuj za chwilę" |
| `settings.api_key_missing` | 401 | Brak klucza | „Skonfiguruj klucz API" |
| `settings.too_many_attempts` | 429 | Za wiele prób zapisu klucza | „Odczekaj chwilę" |
| `validation.failed` | 400 | Błąd walidacji (FluentValidation) | Walidacja inline |
| `error.unexpected` | 500 | Nieobsłużony wyjątek | Komunikat + krótki id zgłoszenia (`X-Trace-Id`) |
