# US-05 — Limit plików (quota)

**Epik:** Dokumenty | **Priorytet:** P1 | **Zależności:** US-01 | **Szacunek:** S

## Historia
Jako użytkownik widzę stały licznik „X / 10 plików" i nie mogę przekroczyć limitu, dzięki czemu rozumiem ograniczenia darmowej wersji, a system pozostaje przewidywalny kosztowo.

## Kontekst / decyzje projektowe
- Limity są **konfiguracją, nie stałą w kodzie**: `QuotaOptions { MaxDocuments = 10, MaxFileSizeMb = 10, MaxTotalMb = 50 }` — projekt „quota-ready" pod przyszłe tiery (narracja do README).
- Egzekwowanie wyłącznie server-side w handlerze uploadu; UI jedynie odzwierciedla stan.
- Dokumenty `Failed` **wliczają się** do quoty do momentu usunięcia (świadoma decyzja: user widzi problem i sam sprząta; alternatywa opisana w README).
- Dokumenty demo (US-03) nie wliczają się.

## Kryteria akceptacji
### AC-1: Licznik w UI
- GIVEN użytkownik z 7 dokumentami
- WHEN otwiera widok dokumentów
- THEN widzi pasek/licznik „7 / 10 plików" oraz zużycie „X / 50 MB"

### AC-2: Blokada przy pełnej quocie
- GIVEN użytkownik z 10 dokumentami
- WHEN próbuje wgrać kolejny plik
- THEN handler zwraca `Result.Failure(QuotaExceeded)` przed zapisem czegokolwiek; UI pokazuje komunikat i podpowiada usunięcie plików; przycisk uploadu w stanie disabled z tooltipem

### AC-3: Limit łącznego rozmiaru
- GIVEN użytkownik z 45 MB dokumentów i limitem 50 MB
- WHEN wgrywa plik 8 MB
- THEN `Result.Failure(TotalSizeQuotaExceeded)` z informacją o dostępnym miejscu

### AC-4: Zwolnienie quoty po usunięciu
- GIVEN pełna quota
- WHEN użytkownik usuwa dokument (US-08)
- THEN licznik spada, upload znów możliwy — bez odświeżania strony

### AC-5: Race condition
- GIVEN 9/10 plików
- WHEN dwa uploady trafiają równocześnie
- THEN maksymalnie jeden przechodzi (sprawdzenie quoty i insert w jednej transakcji z odpowiednim poziomem izolacji lub constraint/advisory lock)

## Zakres techniczny
- **Backend:** `QuotaOptions` przez `IOptions<T>`; `IQuotaService.CheckCanUpload(sessionId, fileSize)` wywoływany w slice uploadu; endpoint `GET /api/quota` zwracający stan.
- **Frontend:** komponent paska quoty (odświeżany po upload/delete przez wspólny store/sygnał).

## Przypadki brzegowe
- Zmiana limitu w konfiguracji w dół poniżej aktualnego stanu użytkownika → istniejące pliki zostają, nowe uploady zablokowane do zejścia poniżej limitu.

## Poza zakresem
Płatne tiery, per-user override limitów, panel administracyjny.

## Definition of Done
AC-1–AC-5 z testami (w tym test współbieżności); limity udokumentowane w README z uzasadnieniem „quota-ready".
