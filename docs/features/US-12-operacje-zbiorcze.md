# US-12 — Operacje zbiorcze na plikach

**Epik:** Foldery | **Priorytet:** P3 (kandydat do „later") | **Zależności:** US-07, US-08, US-10 | **Szacunek:** M

## Historia
Jako użytkownik zaznaczam wiele plików i jedną akcją przenoszę je do folderu lub usuwam, aby porządkować bazę wiedzy bez powtarzania operacji.

## Kontekst / decyzje projektowe
- Semantyka **all-or-nothing**: jedna transakcja; jeśli którykolwiek plik nie przechodzi walidacji (własność, read-only demo), cała operacja jest odrzucana z listą powodów. Decyzja do README (vs częściowy sukces — omówić trade-off).
- Uzasadnienie istnienia przy limicie 10 plików: API projektowane pod zniesienie limitu w przyszłym tierze (narracja „quota-ready" z US-05).
- Tylko dwie operacje: `move`, `delete`. Nic więcej.

## Kryteria akceptacji
### AC-1: Zaznaczanie
- GIVEN lista plików
- WHEN użytkownik zaznacza checkboxy (lub Shift+klik dla zakresu w obrębie folderu)
- THEN pojawia się pasek akcji „N zaznaczonych: Przenieś do… | Usuń | Anuluj"

### AC-2: Zbiorcze przeniesienie
- GIVEN 3 zaznaczone pliki z różnych folderów
- WHEN użytkownik wybiera „Przenieś do…" → folder „Archiwum"
- THEN jeden request `POST /api/documents/bulk-move { ids, targetFolderId }`; wszystkie pliki w „Archiwum"; drzewo zaktualizowane

### AC-3: Zbiorcze usunięcie
- GIVEN 3 zaznaczone pliki
- WHEN „Usuń" + potwierdzenie (dialog pokazuje liczbę i nazwy)
- THEN jedna transakcja usuwa rekordy + chunki (kaskada); quota maleje o 3

### AC-4: All-or-nothing
- GIVEN zaznaczenie zawierające dokument demo (read-only)
- WHEN próba zbiorczego usunięcia
- THEN cała operacja odrzucona: `Result.Failure(BulkValidationFailed)` z listą `{id, powód}`; UI wskazuje problematyczne pozycje; żaden plik nie został usunięty

### AC-5: Walidacja własności każdego ID
- GIVEN lista ID zawierająca cudzy dokument
- WHEN request dociera
- THEN operacja odrzucona (spójnie z AC-4; cudzy ID raportowany jako „nie znaleziono")

## Zakres techniczny
- **Backend:** slices `Documents/BulkMove`, `Documents/BulkDelete`; limit rozmiaru listy (np. 50 ID); walidacja wszystkich pozycji przed jakąkolwiek zmianą; jedna transakcja.
- **Frontend:** selection state w store, pasek akcji, dialogi potwierdzeń, czyszczenie zaznaczenia po sukcesie.

## Przypadki brzegowe
- Pusta lista ID → 400.
- Duplikaty ID w liście → deduplikacja po stronie serwera.
- Plik usunięty w innej karcie przed operacją → raportowany w błędzie walidacji (all-or-nothing).

## Poza zakresem
Bulk drag&drop, bulk zmiana nazw, operacje na folderach zbiorczo.

## Definition of Done
AC-1–AC-5 z testami (obowiązkowo test transakcyjności all-or-nothing); trade-off semantyki opisany w README.
