# US-02 — Konfiguracja klucza AI (BYOK)

**Epik:** Fundament | **Priorytet:** P1 | **Zależności:** US-01 | **Szacunek:** M

## Historia
Jako użytkownik podaję własny klucz API Anthropic, który jest trzymany wyłącznie w pamięci sesji serwera (nigdy w bazie danych), dzięki czemu generowanie odpowiedzi odbywa się na moim koncie i moim koszcie.

## Kontekst / decyzje projektowe
- Świadoma decyzja architektoniczna: **brak persystencji sekretów**. Klucz żyje w server-side session store (IMemoryCache/IDistributedCache z TTL) powiązanym z `UserSessionId` — restart aplikacji = konieczność ponownego podania klucza. Trade-off opisany w README.
- Klucz dotyczy wyłącznie **generacji** (Claude). Embeddingi są scentralizowane po stronie aplikacji (osobny provider, klucz aplikacji w konfiguracji) — patrz US-06.
- Klucz nigdy nie trafia do logów, odpowiedzi API ani frontendu w pełnej formie.

## Kryteria akceptacji
### AC-1: Zapis i walidacja klucza
- GIVEN użytkownik na ekranie ustawień
- WHEN wkleja klucz `sk-ant-api03-...` i klika „Zapisz"
- THEN backend wykonuje testowe wywołanie API Anthropic (minimalny request); przy sukcesie klucz zapisany w session store i zwrócony status „aktywny"; przy błędzie — `Result.Failure(InvalidApiKey)` z czytelnym komunikatem

### AC-2: Maskowanie
- GIVEN zapisany klucz
- WHEN użytkownik otwiera ustawienia
- THEN widzi wyłącznie maskę `sk-ant-api03-…XXXX` (4 ostatnie znaki); pełny klucz nigdy nie jest zwracany przez API

### AC-3: Brak klucza blokuje czat
- GIVEN sesja bez klucza (i poza trybem demo)
- WHEN użytkownik otwiera czat
- THEN pole pytania jest zablokowane z komunikatem i linkiem do ustawień; endpoint czatu zwraca `Result.Failure(ApiKeyMissing)`

### AC-4: Usunięcie klucza
- GIVEN zapisany klucz
- WHEN użytkownik klika „Usuń klucz"
- THEN klucz jest usuwany z session store; czat wraca do stanu zablokowanego

### AC-5: Brak wycieków
- GIVEN dowolna operacja z użyciem klucza
- WHEN przeglądane są logi aplikacji i odpowiedzi HTTP
- THEN klucz nie występuje w żadnej formie (test: middleware skanujący logi w testach integracyjnych lub reguła w loggerze)

## Zakres techniczny
- **Backend:** slice `Settings/SetApiKey`, `Settings/DeleteApiKey`, `Settings/GetApiKeyStatus`; `IApiKeyStore` nad IMemoryCache (TTL = TTL sesji); fabryka klienta Anthropic per request na bazie klucza z sesji.
- **Frontend:** komponent ustawień (input typu password, maska po zapisie, stany: brak/aktywny/błąd).
- **Bezpieczeństwo:** klucz przesyłany wyłącznie POST body po HTTPS; nagłówek `Cache-Control: no-store` na endpointach ustawień.

## Przypadki brzegowe
- Klucz poprawny składniowo, ale bez środków/uprawnień → walidacja wychwyci (błąd z API Anthropic przekazany czytelnie).
- Wygaśnięcie wpisu w cache w trakcie rozmowy → czat zwraca `ApiKeyMissing`, UI prowadzi do ustawień bez utraty historii rozmowy.

## Poza zakresem
Szyfrowanie at-rest (nie ma persystencji), obsługa wielu providerów, klucze OpenAI/innych.

## Definition of Done
AC-1–AC-5 pokryte testami; sekcja README „Obsługa sekretów (BYOK)" z uzasadnieniem braku persystencji.
