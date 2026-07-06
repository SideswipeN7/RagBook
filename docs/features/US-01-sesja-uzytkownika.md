# US-01 — Sesja użytkownika (izolacja danych)

**Epik:** Fundament | **Priorytet:** P1 (blokujące wszystko) | **Zależności:** brak | **Szacunek:** S

## Historia
Jako użytkownik aplikacji otrzymuję anonimową sesję identyfikującą mnie bez logowania, dzięki czemu moje dokumenty, foldery i rozmowy są w pełni odizolowane od innych użytkowników.

## Kontekst / decyzje projektowe
- Brak logowania i multi-tenant w MVP (poza zakresem całego projektu) — izolacja opiera się na `UserSessionId` (GUID) generowanym przy pierwszej wizycie.
- Identyfikator sesji trzymany w cookie `HttpOnly`, `Secure`, `SameSite=Strict`, ważność 30 dni (odświeżana przy każdej wizycie).
- Każda encja domenowa (Document, Folder, Conversation) posiada kolumnę `UserSessionId` i każde zapytanie jest po niej filtrowane — brak globalnych zapytań bez filtra sesji.
- Próba dostępu do cudzego zasobu zwraca **404** (nie 403), aby nie ujawniać istnienia zasobu.

## Kryteria akceptacji
### AC-1: Utworzenie sesji
- GIVEN nowy użytkownik bez cookie sesji
- WHEN otwiera aplikację (dowolny endpoint SPA lub API)
- THEN backend generuje `UserSessionId` (GUID v4), ustawia cookie HttpOnly i zwraca stan aplikacji dla pustej sesji

### AC-2: Trwałość sesji
- GIVEN użytkownik z ważnym cookie sesji
- WHEN wraca do aplikacji po zamknięciu przeglądarki
- THEN widzi swoje dokumenty i foldery; data ważności cookie zostaje odświeżona

### AC-3: Izolacja zasobów
- GIVEN dwie różne sesje A i B
- WHEN sesja B odpytuje o zasób (dokument/folder/rozmowę) należący do A po jego ID
- THEN API zwraca 404; zasób A nie pojawia się na żadnej liście sesji B

### AC-4: Filtr sesji wymuszony architektonicznie
- GIVEN dowolny handler czytający dane domenowe
- WHEN wykonuje zapytanie do bazy
- THEN zapytanie zawiera warunek `UserSessionId = @session` (wymuszone przez wspólny mechanizm, np. rozszerzenie repozytorium/query filter EF Core), a test integracyjny to weryfikuje

## Zakres techniczny
- **Backend:** middleware ustawiający/odczytujący cookie i wstrzykujący `ISessionContext { Guid UserSessionId }` do handlerów; global query filter EF Core na `UserSessionId`.
- **DB:** kolumna `user_session_id uuid NOT NULL` + indeks na każdej tabeli głównej.
- **Frontend:** brak logiki — cookie zarządzane przez backend; interceptor Angular obsługuje 404 jako „zasób nie istnieje".

## Przypadki brzegowe
- Wygasłe/skasowane cookie → nowa, pusta sesja (stare dane pozostają osierocone; czyszczenie poza zakresem MVP).
- Ręcznie sfałszowany GUID w cookie → traktowany jak pusta sesja (brak danych), nie błąd.

## Poza zakresem
Logowanie, rejestracja, odzyskiwanie sesji, GDPR-owe czyszczenie osieroconych danych (odnotować w README jako known limitation).

## Definition of Done
Testy integracyjne AC-1–AC-4 zielone; global query filter udokumentowany w README (sekcja „Izolacja danych").
