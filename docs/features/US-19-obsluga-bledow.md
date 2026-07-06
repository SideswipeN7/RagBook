# US-19 — Obsługa błędów i stany brzegowe

**Epik:** Wykończenie | **Priorytet:** P1 | **Zależności:** przekrojowo US-02–US-18 | **Szacunek:** M

## Historia
Jako użytkownik w każdej sytuacji błędu dostaję czytelny, konkretny komunikat z możliwą akcją naprawczą — nigdy surowy wyjątek, kod 500 bez treści ani urwany interfejs.

## Kontekst / decyzje projektowe
- Jednolity kontrakt błędów API oparty o `Result<T>`: każdy failure mapowany na `ProblemDetails` z polami `{ code, title, detail, errors? }`; `code` to stabilny identyfikator (np. `QuotaExceeded`, `InvalidApiKey`) — frontend tłumaczy kody na komunikaty PL (słownik w jednym miejscu).
- Wyjątki nieobsłużone → globalny handler → `ProblemDetails(code=InternalError)` + correlation id w odpowiedzi i logach; szczegóły wyjątku nigdy w odpowiedzi.
- Taksonomia komunikatów UI: **błąd walidacji** (inline przy polu/akcji), **błąd operacji** (toast z akcją), **stan pusty/informacyjny** (w treści widoku — np. NoAnswerFound to NIE błąd), **błąd krytyczny widoku** (panel z „Spróbuj ponownie").

## Kryteria akceptacji
### AC-1: Katalog kodów pokryty
- GIVEN kody: `InvalidApiKey`, `ApiKeyMissing`, `QuotaExceeded`, `TotalSizeQuotaExceeded`, `UnsupportedFileType`, `FileTooLarge`, `EmptyFile`, `MaxDepthExceeded`, `DuplicateFolderName`, `FolderNotEmpty`, `CircularMove`, `ReadOnlyResource`, `DemoLimitReached`, `BulkValidationFailed`, `ScopeNotFound`, `RateLimited`, `ProviderUnavailable`, `InternalError`
- WHEN dowolny z nich wraca z API
- THEN frontend pokazuje dedykowany polski komunikat z sensowną akcją (test: słownik kompletny — brak fallbacku „Unknown error" dla znanych kodów)

### AC-2: Zły klucz w trakcie pracy
- GIVEN klucz unieważniony po stronie Anthropic w trakcie sesji
- WHEN użytkownik zadaje pytanie
- THEN komunikat „Klucz API został odrzucony przez Anthropic" + link do ustawień; historia rozmowy nietknięta

### AC-3: Timeout / niedostępność providera
- GIVEN timeout wywołania (limit w konfiguracji, np. 60 s) lub 5xx
- WHEN operacja czatu zawodzi
- THEN toast/panel „Usługa AI chwilowo niedostępna — spróbuj ponownie" z przyciskiem retry ponawiającym ostatnie pytanie

### AC-4: Correlation id
- GIVEN błąd `InternalError`
- WHEN użytkownik widzi komunikat
- THEN komunikat zawiera krótki identyfikator zgłoszenia; ten sam id jest w logach serwera (test integracyjny)

### AC-5: Brak surowych wyjątków
- GIVEN dowolny endpoint i wymuszony wyjątek w handlerze (test)
- WHEN odpowiedź wraca do klienta
- THEN status 500 + `ProblemDetails` bez stack trace; format identyczny jak dla błędów domenowych

## Zakres techniczny
- **Backend:** middleware wyjątków; mapper `Result.Failure → ProblemDetails` (jedno miejsce); polityki timeout/retry na kliencie Anthropic (Polly lub wbudowane SDK); correlation id (middleware + logger scope).
- **Frontend:** interceptor HTTP mapujący `ProblemDetails` na warstwę komunikatów; słownik kodów PL; komponenty toast/panel błędu.

## Przypadki brzegowe
- 429 od Anthropic z `retry-after` → komunikat z czasem oczekiwania.
- Utrata sieci w SPA → globalny banner offline (navigator.onLine + nieudane requesty).

## Poza zakresem
Telemetria/APM, alerting, i18n poza polskim.

## Definition of Done
AC-1–AC-5 z testami; katalog kodów błędów jako tabela w README (kod → znaczenie → zachowanie UI).
