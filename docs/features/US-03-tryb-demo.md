# US-03 — Tryb demo

**Epik:** Fundament | **Priorytet:** P1 (kluczowe dla case study) | **Zależności:** US-01; pełna wartość po US-14–US-17 | **Szacunek:** M

## Historia
Jako odwiedzający (np. rekruter) korzystam z trybu demo na wbudowanych przykładowych dokumentach bez podawania własnego klucza API, aby w ciągu minuty zobaczyć działający czat z cytatami.

## Kontekst / decyzje projektowe
- Tryb demo używa **klucza aplikacji** (konfiguracja serwera, nigdy nie ujawniany) — koszt kontrolowany limitami.
- Dokumenty demo (2–3 pliki: np. dokumentacja techniczna, przykładowa umowa, artykuł) są seedowane przy starcie aplikacji jako zasoby globalne, tylko do odczytu, widoczne w każdej sesji w wydzielonej sekcji „Demo".
- Limity: N pytań na sesję (konfigurowalne, domyślnie 10) + rate limiting po IP (np. 20 pytań/h) — obie wartości w `DemoOptions`.

## Kryteria akceptacji
### AC-1: Dostęp bez klucza
- GIVEN nowa sesja bez klucza API
- WHEN użytkownik wybiera zakres „Dokumenty demo" i zadaje pytanie
- THEN otrzymuje pełnoprawną odpowiedź RAG (streaming + cytaty) na kluczu aplikacji

### AC-2: Limit pytań na sesję
- GIVEN sesja, która zadała N pytań demo
- WHEN zadaje kolejne pytanie w trybie demo
- THEN API zwraca `Result.Failure(DemoLimitReached)`; UI pokazuje licznik „X / N pytań demo" i zachętę do trybu BYOK

### AC-3: Rate limiting po IP
- GIVEN adres IP przekraczający limit godzinowy
- WHEN wysyła pytanie demo
- THEN API zwraca 429 z nagłówkiem `Retry-After`; UI pokazuje czytelny komunikat

### AC-4: Ochrona dokumentów demo
- GIVEN dokumenty demo widoczne w drzewie
- WHEN użytkownik próbuje je usunąć, przenieść lub nadpisać
- THEN operacja jest zablokowana (elementy oznaczone jako tylko do odczytu w UI; API zwraca `Result.Failure(ReadOnlyResource)`)

### AC-5: Demo nie zużywa quoty użytkownika
- GIVEN limit 10 plików użytkownika
- WHEN liczona jest quota
- THEN dokumenty demo nie są wliczane

## Zakres techniczny
- **Backend:** seeder (idempotentny — sprawdza istnienie po stałych ID) uruchamiany przy starcie; `DemoOptions { MaxQuestionsPerSession, MaxQuestionsPerIpPerHour, DemoDocumentIds }`; licznik pytań w session store; rate limiter ASP.NET Core (`AddRateLimiter`, partycja po IP).
- **Frontend:** sekcja „Demo" w drzewie z odznaką; licznik pozostałych pytań; baner trybu demo w czacie.
- **Konfiguracja:** klucz aplikacji w zmiennej środowiskowej / Secret Manager (GCP), nigdy w repo.

## Przypadki brzegowe
- Upload w trakcie scope=demo → upload zawsze idzie do zasobów użytkownika, nie do demo.
- Wyczerpany budżet klucza aplikacji → błąd providera mapowany na czytelny komunikat „Tryb demo chwilowo niedostępny" (nie surowy błąd 500).

## Poza zakresem
Osobny landing page marketingowy, konfigurowalne zestawy dokumentów demo per użytkownik.

## Definition of Done
AC-1–AC-5 z testami; seed działa na czystej bazie w Docker Compose i na Cloud Run; GIF z przejścia demo w README.
