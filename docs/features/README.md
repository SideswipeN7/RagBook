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

## Sugerowana kolejność implementacji (kamienie milowe)

1. **M1 — Pion danych:** US-01 → US-05 → US-09 → US-04 → US-06 → US-07 → US-08. Efekt: upload → indeksacja → drzewo → delete działa end-to-end.
2. **M2 — Rdzeń RAG:** US-02 → US-13 → US-14 → US-15 → US-16 → US-17. Efekt: pełny czat z cytatami i groundingiem.
3. **M3 — UX folderów:** US-10 → US-11 → (US-12). Efekt: drag&drop i operacje zbiorcze.
4. **M4 — Wykończenie:** US-03 → US-18 → US-19 → US-20. Efekt: demo publiczne + case study.

Kandydaci do cięcia przy braku czasu: US-12, US-18 (reszta to rdzeń).

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
- Limity: wszystkie w konfiguracji (`QuotaOptions`, `DemoOptions`, `RagOptions`, `ChatOptions`, `BulkOptions`) — zero magic numbers.
- Operacje zbiorcze (US-12): semantyka **all-or-nothing** — serwer waliduje **wszystkie** pozycje przed jakąkolwiek zmianą i stosuje je w jednej transakcji; jeśli którakolwiek pozycja jest niepoprawna, cała operacja jest odrzucana z listą `failures: [{ id, code }]` (`422 document.bulk_validation_failed`) i **nic** się nie zmienia. Świadomy trade-off wobec „częściowego sukcesu": half-applied bulk delete/move byłby gorszy niż brak zmiany — użytkownik dostaje przewidywalny, odwracalny stan i precyzyjnie oznaczone pozycje do poprawy, kosztem konieczności ponowienia po korekcie zaznaczenia. Cudzy/nieistniejący ID raportowany jako `document.not_found` (bez ujawniania istnienia); pusta/za duża lista → `400`. Limit listy z `BulkOptions.MaxItems` (quota-ready).
