# US-08 — Usuwanie dokumentu

**Epik:** Dokumenty | **Priorytet:** P1 | **Zależności:** US-04, US-05, US-07 | **Szacunek:** S

## Historia
Jako użytkownik usuwam dokument (wraz z jego indeksem), aby zwolnić miejsce w quocie i utrzymać porządek w bazie wiedzy.

## Kontekst / decyzje projektowe
- Usunięcie jest **trwałe** (hard delete) — brak kosza w MVP; wymaga potwierdzenia w UI.
- Chunki usuwane kaskadowo przez FK `ON DELETE CASCADE` (decyzja: baza, nie kod aplikacji — pojedyncze źródło spójności; odnotować w README).
- Plik binarny usuwany z `IFileStorage`; kolejność: najpierw baza (transakcja), potem storage; osierocony plik przy błędzie storage jest akceptowalnym trade-offem MVP (log + odnotowane w README).

## Kryteria akceptacji
### AC-1: Usunięcie z potwierdzeniem
- GIVEN plik na liście
- WHEN użytkownik klika „Usuń" i potwierdza w dialogu
- THEN rekord dokumentu i wszystkie jego chunki znikają z bazy; plik znika z drzewa; quota (US-05) maleje natychmiast

### AC-2: Kaskada chunków
- GIVEN dokument Ready z N chunkami
- WHEN zostaje usunięty
- THEN w tabeli `chunks` nie ma żadnego wiersza z jego `document_id` (test integracyjny)

### AC-3: Usunięcie w trakcie przetwarzania
- GIVEN dokument w statusie Processing
- WHEN użytkownik go usuwa
- THEN usunięcie przechodzi; handler przetwarzania (US-06) po dojściu do zapisu wykrywa brak rekordu i przerywa cicho

### AC-4: Własność
- GIVEN dokument innej sesji
- WHEN wywoływany jest DELETE po jego ID
- THEN 404 (spójnie z US-01)

### AC-5: Cytaty po usunięciu źródła
- GIVEN historyczna odpowiedź w czacie cytująca usunięty dokument
- WHEN użytkownik klika cytat
- THEN UI pokazuje stan „Dokument został usunięty" zamiast błędu

## Zakres techniczny
- **Backend:** slice `Documents/Delete`; transakcja: delete rekordu (kaskada z FK) → commit → best-effort delete z `IFileStorage`.
- **Frontend:** akcja w wierszu pliku + dialog potwierdzenia; aktualizacja store po sukcesie.

## Przypadki brzegowe
- Podwójne kliknięcie / równoległy delete → drugi zwraca 404, UI ignoruje (idempotentne z perspektywy użytkownika).

## Poza zakresem
Kosz/przywracanie, usuwanie folderów z zawartością (US-09 blokuje niepuste).

## Definition of Done
AC-1–AC-5 z testami; migracja z FK ON DELETE CASCADE; decyzja o kolejności delete opisana w README.
