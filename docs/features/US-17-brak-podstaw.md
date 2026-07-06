# US-17 — Brak podstaw do odpowiedzi („nie znaleziono w dokumentach")

**Epik:** Czat / RAG | **Priorytet:** P1 (rdzeń value prop) | **Zależności:** US-14 | **Szacunek:** M

## Historia
Jako użytkownik, gdy moje dokumenty nie zawierają odpowiedzi, dostaję jednoznaczne „Nie znalazłem tego w dokumentach" zamiast zmyślonej odpowiedzi — aby móc ufać każdej treściwej odpowiedzi systemu.

## Kontekst / decyzje projektowe
- Dwie linie obrony:
  1. **Deterministyczna (przed LLM):** wszystkie trafienia poniżej `SimilarityThreshold` → odpowiedź stanu generowana przez backend bez wywołania modelu (oszczędność i pewność).
  2. **Promptowa (w LLM):** fragmenty przeszły próg, ale nie zawierają odpowiedzi → prompt zobowiązuje model do ustalonego zwrotu odmowy (dokładna fraza-sentinel w kontrakcie promptu), którą backend wykrywa i mapuje na stan `NoAnswerFound`.
- Stan `NoAnswerFound` to **metadana wiadomości**, nie tylko tekst — UI renderuje go odmiennie (neutralnie, nie jak błąd), z podpowiedziami: zmień scope na szerszy / sprawdź, czy dokument jest Ready / przeformułuj pytanie.
- Częściowa odpowiedź („dokumenty odpowiadają na część pytania") jest dozwolona — model ma odpowiedzieć na to, co ma pokrycie, i wprost wskazać brakującą część.

## Kryteria akceptacji
### AC-1: Odcięcie progiem (bez LLM)
- GIVEN pytanie niezwiązane z dokumentami (np. „jaka jest stolica Australii" przy dokumentach o umowach)
- WHEN retrieval nie zwraca trafień ≥ próg
- THEN odpowiedź stanu pojawia się natychmiast; brak wywołania API Anthropic (weryfikowalne mockiem); brak listy źródeł

### AC-2: Odmowa promptowa
- GIVEN fragmenty tematycznie bliskie, ale bez odpowiedzi (np. pytanie o karę umowną, gdy umowa jej nie zawiera)
- WHEN model odpowiada frazą-sentinelem
- THEN backend mapuje odpowiedź na `NoAnswerFound`; UI pokazuje stan „nie znaleziono" + sekcję zwijaną „przeszukane fragmenty" (transparentność)

### AC-3: Rozróżnienie od błędu
- GIVEN stan `NoAnswerFound`
- WHEN renderowany w UI
- THEN wygląda jak neutralna informacja (ikona/kolor informacyjny), wyraźnie inaczej niż błędy techniczne (US-19); zawiera podpowiedzi kolejnych kroków

### AC-4: Odpowiedź częściowa
- GIVEN pytanie dwuczęściowe, gdzie dokumenty pokrywają jedną część
- WHEN model odpowiada
- THEN odpowiedź zawiera część z pokryciem (z cytatami) + jawne wskazanie, czego w dokumentach nie ma; stan wiadomości pozostaje „zwykły" (nie NoAnswerFound)

### AC-5: Testowalność progu
- GIVEN zestaw ewaluacyjny (min. 10 par pytanie–oczekiwany stan na dokumentach demo)
- WHEN uruchamiany jest test integracyjny pipeline'u
- THEN przypadki „poza tematem" trafiają w ścieżkę AC-1 (deterministycznie), a dobrane wartości progu są udokumentowane

## Zakres techniczny
- **Backend:** logika progu w slice `Chat/AskQuestion` przed generacją; detekcja frazy-sentinela po/w trakcie generacji (jeśli w streamie — po `done` przed `sources`, patrz US-15/16); enum `MessageState { Normal, NoAnswerFound, Interrupted, Error }`.
- **Frontend:** wariant renderowania wiadomości dla `NoAnswerFound`.

## Przypadki brzegowe
- Sentinel pojawia się w środku dłuższej odpowiedzi → traktuj jako odpowiedź normalną (sentinel liczy się tylko jako pełna/otwierająca fraza); reguła opisana w kontrakcie promptu.
- Streaming a sentinel: fraza wykrywalna dopiero po kilku tokenach → dopuszczalne krótkie mignięcie tekstu przed przełączeniem na stan (odnotować; alternatywa z buforowaniem początku strumienia jako future work).

## Poza zakresem
Automatyczne rozszerzanie scope, sugestie pytań, ewaluacja LLM-as-judge.

## Definition of Done
AC-1–AC-5 (w tym mini-zestaw ewaluacyjny w testach integracyjnych); sekcja README „Grounding i odmowa odpowiedzi" — to sekcja, którą czyta się na rozmowie o halucynacjach.
