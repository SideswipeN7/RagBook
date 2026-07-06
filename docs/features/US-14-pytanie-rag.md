# US-14 — Zadanie pytania z RAG

**Epik:** Czat / RAG | **Priorytet:** P1 | **Zależności:** US-02 (lub US-03), US-06, US-13 | **Szacunek:** L

## Historia
Jako użytkownik zadaję pytanie w języku naturalnym i otrzymuję odpowiedź opartą wyłącznie na fragmentach moich dokumentów z wybranego zakresu — bez zmyśleń.

## Kontekst / decyzje projektowe
- Pipeline: embedding pytania (ten sam model co indeks!) → top-K najbliższych chunków (`<=>`, cosine) z filtrem sesji+scope → odcięcie progiem podobieństwa → budowa promptu z fragmentami → wywołanie Claude (klucz BYOK lub demo).
- Parametry w `RagOptions { TopK = 8, SimilarityThreshold = 0.75, MaxContextChars }` — konfiguracja, nie stałe (strojenie opisać w README).
- **Prompt groundingu** (kontrakt): fragmenty numerowane `[1]..[K]` z metadanymi (plik, strona); instrukcje: odpowiadaj WYŁĄCZNIE na podstawie fragmentów; oznaczaj twierdzenia numerami źródeł `[n]`; jeśli fragmenty nie zawierają odpowiedzi — odpowiedz dokładnie ustalonym zwrotem odmowy (kontrakt dla US-17); odpowiadaj w języku pytania.
- Fragmenty ponad `MaxContextChars` przycinane od najsłabszych trafień.

## Kryteria akceptacji
### AC-1: Poprawna odpowiedź z zakresu
- GIVEN dokumenty Ready zawierające odpowiedź
- WHEN użytkownik pyta w ich scope
- THEN odpowiedź oparta na treści dokumentów, z odwołaniami `[n]` mapowalnymi na przekazane fragmenty (US-16)

### AC-2: Filtr retrievalu
- GIVEN dokumenty w scope i poza scope zawierające podobną treść
- WHEN pytanie w wybranym scope
- THEN żaden chunk spoza scope/sesji nie trafia do promptu (test integracyjny na warstwie retrievalu)

### AC-3: Próg podobieństwa
- GIVEN pytanie zupełnie niezwiązane z dokumentami
- WHEN retrieval zwraca wyłącznie trafienia poniżej progu
- THEN LLM nie jest wywoływany albo dostaje pusty kontekst → ścieżka „nie znaleziono" (US-17); zachowanie deterministyczne i przetestowane

### AC-4: Konfigurowalność
- GIVEN zmiana `TopK`/`SimilarityThreshold` w konfiguracji
- WHEN pipeline działa
- THEN nowe wartości obowiązują bez zmian w kodzie

### AC-5: Błędy providera
- GIVEN błąd/timeout API Anthropic (zły klucz, 429, 5xx)
- WHEN wywołanie zawodzi
- THEN `Result.Failure` z rozróżnialnym kodem (`InvalidApiKey` / `RateLimited` / `ProviderUnavailable`); UI mapuje na czytelne komunikaty (US-19)

## Zakres techniczny
- **Backend:** slice `Chat/AskQuestion` — kroki: walidacja scope → embedding pytania (`IEmbeddingProvider`) → retrieval (zapytanie z US-13) → `IPromptBuilder` → `IAnswerGenerator` (Anthropic .NET SDK); zapis pytania/odpowiedzi/użytych chunków do rozmowy (US-18).
- **DB:** indeks HNSW już z US-06; retrieval jednym zapytaniem.
- **Frontend:** pole pytania + wysyłka; render odpowiedzi w US-15/16.

## Przypadki brzegowe
- Pytanie puste / >2000 znaków → walidacja 400.
- Wszystkie dokumenty w scope Processing → ścieżka pustego scope (US-13 AC-5).
- Bardzo długie chunki → przycinanie kontekstu wg dystansu (najlepsze zostają).

## Poza zakresem
Re-ranking (np. cross-encoder), hybrydowe BM25+wektor, query rewriting — wymienić w README jako świadome uproszczenia z uzasadnieniem.

## Definition of Done
AC-1–AC-5 z testami (retrieval integracyjnie na pgvector; generacja za interfejsem — mock w testach); prompt groundingu w repo jako plik/stała z komentarzem; sekcja README „Pipeline RAG" z diagramem.
