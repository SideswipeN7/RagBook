# US-16 — Cytaty źródeł (weryfikowalność odpowiedzi)

**Epik:** Czat / RAG | **Priorytet:** P1 (rdzeń value prop) | **Zależności:** US-14, US-15 | **Szacunek:** L

## Historia
Jako użytkownik widzę przy odpowiedzi listę źródeł (plik + fragment + strona), a po kliknięciu — podgląd fragmentu z podświetleniem, aby móc zweryfikować każdą informację.

## Kontekst / decyzje projektowe
- Mechanizm: prompt (US-14) wymusza znaczniki `[n]` odnoszące się do numerów przekazanych fragmentów; backend zna mapowanie `n → chunk` (bo sam zbudował prompt), więc cytaty są **deterministyczne** — nie parsujemy „domysłów" modelu, tylko mapujemy jego odwołania na znane chunki.
- Zdarzenie `sources` (US-15) niesie listę: `{ n, documentId, fileName, pageNumber?, snippet (pierwsze ~200 znaków chunka), chunkId }`.
- Znaczniki `[n]` w tekście odpowiedzi renderowane jako klikalne odnośniki przewijające do pozycji na liście źródeł / otwierające podgląd.
- Podgląd fragmentu: panel/dialog z pełnym tekstem chunka + metadane (plik, strona) + wyróżnienie. Podgląd całego dokumentu z nawigacją do strony — poza MVP (świadome cięcie: podświetlamy chunk, nie pozycję w oryginalnym PDF).

## Kryteria akceptacji
### AC-1: Lista źródeł
- GIVEN zakończona odpowiedź używająca fragmentów
- WHEN strumień dostarczy `sources`
- THEN pod odpowiedzią pojawia się lista źródeł z nazwą pliku, numerem strony (PDF) i snippetem; tylko fragmenty faktycznie użyte (`[n]` obecne w tekście) są wyróżnione, pozostałe przekazane do kontekstu — w sekcji zwijanej „pozostałe przeszukane fragmenty"

### AC-2: Klikalne znaczniki
- GIVEN odpowiedź ze znacznikiem `[2]`
- WHEN użytkownik klika `[2]`
- THEN otwiera się podgląd fragmentu nr 2 (pełny tekst chunka, plik, strona)

### AC-3: Spójność mapowania
- GIVEN dowolna odpowiedź
- WHEN model użył znacznika `[n]`
- THEN `n` zawsze mapuje się na fragment przekazany w prompcie (mapowanie po stronie backendu; znacznik bez pokrycia — patrz przypadki brzegowe)

### AC-4: Cytat po usunięciu dokumentu
- GIVEN historyczna odpowiedź cytująca usunięty dokument
- WHEN użytkownik klika cytat
- THEN stan „Dokument został usunięty" ze snippetem zachowanym w danych wiadomości (snippet zapisujemy przy odpowiedzi — cytaty historyczne nie zależą od istnienia chunka)

### AC-5: Odpowiedź bez podstaw
- GIVEN ścieżka „nie znaleziono" (US-17)
- WHEN odpowiedź jest odmowna
- THEN lista źródeł nie jest renderowana (lub pokazuje wyłącznie sekcję „przeszukane fragmenty")

## Zakres techniczny
- **Backend:** builder promptu zwraca mapę `n → ChunkRef`; po generacji parsowanie znaczników `[n]` z tekstu (regex) i złożenie `sources`; zapis snippetów w danych wiadomości (US-18).
- **Frontend:** renderer wiadomości zamieniający `[n]` na komponent odnośnika; lista źródeł; panel podglądu fragmentu.

## Przypadki brzegowe
- Model użył `[n]` spoza zakresu przekazanych fragmentów → znacznik renderowany jako zwykły tekst + log ostrzeżenia (obserwowalność jakości promptu).
- Model nie użył żadnego znacznika mimo treściwej odpowiedzi → lista pokazuje wszystkie przekazane fragmenty z adnotacją; log ostrzeżenia (sygnał do strojenia promptu).
- Znacznik w środku zdania/w liście — renderer nie łamie formatowania markdown.

## Poza zakresem
Podgląd oryginalnego PDF z nawigacją do strony i podświetleniem w dokumencie, eksport odpowiedzi z bibliografią.

## Definition of Done
AC-1–AC-5 z testami (parser znaczników unit; mapowanie integracyjnie na mocku generatora); GIF „odpowiedź z klikalnym cytatem" w README — to główny materiał demo projektu.
