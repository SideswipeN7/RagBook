# Feature Specification: Konfiguracja klucza AI (BYOK)

**Feature Branch**: `008-us02-byok`

**Created**: 2026-07-11

**Status**: Draft

**Input**: User description: "US-02 — Konfiguracja klucza AI (BYOK). Użytkownik podaje własny klucz API Anthropic trzymany wyłącznie w pamięci sesji serwera (nigdy w bazie), dzięki czemu generacja odpowiedzi odbywa się na jego koncie i koszcie."

## Clarifications

### Session 2026-07-11

- Q: Gdy testowe wywołanie walidujące nie może się zakończyć z powodu problemu po stronie dostawcy (timeout / 5xx / brak sieci), a nie odrzucenia klucza — co robimy? → A: Osobny błąd przejściowy `settings.validation_unavailable` („nie można teraz zweryfikować, spróbuj ponownie"); klucz NIE jest zapisywany; nie mylimy tego z błędnym kluczem.
- Q: Endpoint zapisu wyzwala płatne wywołanie zewnętrzne przy każdej próbie — czy w MVP dodać ochronę przed nadużyciem? → A: Tak — prosty throttle per sesja na próby zapisu/walidacji (ograniczenie liczby prób w oknie czasu).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Zapis i walidacja klucza (Priority: P1)

Użytkownik otwiera ustawienia, wkleja swój klucz API Anthropic (`sk-ant-api03-…`) i zapisuje go. System weryfikuje klucz minimalnym wywołaniem testowym u dostawcy; przy sukcesie klucz jest zapamiętany dla bieżącej sesji i oznaczony jako „aktywny", przy niepowodzeniu użytkownik dostaje czytelny komunikat i klucz nie jest zapamiętywany.

**Why this priority**: To fundament całego czatu RAG (M2). Bez ważnego klucza żadna generacja odpowiedzi nie jest możliwa; walidacja przy zapisie eliminuje najczęstszą klasę błędów (literówka, wygasły/nieaktywny klucz) zanim użytkownik zada pytanie.

**Independent Test**: Podaj poprawny klucz → status „aktywny"; podaj błędny/pusty klucz → czytelny błąd i status „brak/aktywny bez zmian". Testowalne w pełni bez czatu.

**Acceptance Scenarios**:

1. **Given** sesja bez zapisanego klucza, **When** użytkownik wkleja poprawny, aktywny klucz i zapisuje, **Then** system potwierdza status „aktywny", a klucz jest dostępny dla operacji generacji w tej sesji.
2. **Given** sesja bez zapisanego klucza, **When** użytkownik zapisuje klucz odrzucony przez dostawcę (nieprawidłowy, bez środków lub bez uprawnień), **Then** system zwraca błąd `settings.invalid_api_key` z czytelnym komunikatem, a żaden klucz nie zostaje zapamiętany.
3. **Given** sesja bez zapisanego klucza, **When** użytkownik zapisuje pusty lub oczywiście niepoprawny składniowo klucz, **Then** system odrzuca go jako `settings.invalid_api_key` bez wykonywania wywołania do dostawcy.
4. **Given** poprawny składniowo klucz, **When** testowe wywołanie walidujące nie może się zakończyć z powodu problemu po stronie dostawcy (timeout / 5xx / brak sieci), **Then** system zwraca przejściowy błąd `settings.validation_unavailable` z komunikatem zachęcającym do ponowienia, a klucz NIE zostaje zapamiętany.
5. **Given** wielokrotne próby zapisu z jednej sesji w krótkim czasie, **When** liczba prób przekracza dozwolony limit w oknie czasu, **Then** kolejne próby są odrzucane kodem `settings.too_many_attempts` do czasu wygaśnięcia okna, bez wykonywania kolejnych wywołań do dostawcy.

---

### User Story 2 - Brak klucza blokuje czat (Priority: P1)

Gdy sesja nie ma aktywnego klucza (i nie jest w trybie demo), interfejs czatu jest zablokowany z jasnym komunikatem i odnośnikiem do ustawień, a każda próba wywołania generacji jest odrzucana po stronie serwera stabilnym kodem błędu.

**Why this priority**: Zapewnia spójny, bezpieczny stan „przed konfiguracją" i chroni endpoint generacji przed wywołaniem bez klucza. Warunek brzegowy każdej przyszłej funkcji czatu (US-14+).

**Independent Test**: Bez zapisanego klucza sprawdź status → „brak"; wywołaj chroniony endpoint generacji → odmowa `settings.api_key_missing`. Nie wymaga działającego czatu — wystarczy strażnik na endpointcie.

**Acceptance Scenarios**:

1. **Given** sesja bez aktywnego klucza poza trybem demo, **When** użytkownik otwiera czat, **Then** pole pytania jest zablokowane z komunikatem i odnośnikiem do ustawień.
2. **Given** sesja bez aktywnego klucza, **When** następuje próba wywołania generacji, **Then** operacja jest odrzucana kodem `settings.api_key_missing`, bez próby kontaktu z dostawcą.
3. **Given** aktywny klucz, którego wpis w pamięci sesji wygasł w trakcie korzystania, **When** użytkownik próbuje zadać pytanie, **Then** operacja jest odrzucana `settings.api_key_missing`, a UI kieruje do ustawień bez utraty dotychczasowej historii rozmowy.

---

### User Story 3 - Maskowanie zapisanego klucza (Priority: P2)

Po zapisaniu klucza ekran ustawień pokazuje wyłącznie maskę z czterema ostatnimi znakami; pełna wartość klucza nigdy nie jest zwracana przez API ani prezentowana w interfejsie.

**Why this priority**: Potwierdza użytkownikowi, że klucz jest zapisany, bez ujawniania sekretu (współdzielony ekran, zrzut, log przeglądarki). Ważne, ale zależne od US1 (musi istnieć zapisany klucz).

**Independent Test**: Zapisz klucz → odczytaj status/ustawienia → widoczna wyłącznie maska `sk-ant-api03-…XXXX`; żadna odpowiedź API nie zawiera pełnej wartości.

**Acceptance Scenarios**:

1. **Given** zapisany aktywny klucz, **When** użytkownik otwiera ustawienia, **Then** widzi maskę z prefiksem i czterema ostatnimi znakami, a nie pełny klucz.
2. **Given** zapisany aktywny klucz, **When** klient odpytuje status klucza, **Then** odpowiedź zawiera status i maskę, ale nigdy pełnej wartości klucza.

---

### User Story 4 - Usunięcie klucza (Priority: P2)

Użytkownik może w każdej chwili usunąć zapisany klucz; po usunięciu sesja wraca do stanu „brak klucza", a czat ponownie się blokuje.

**Why this priority**: Kontrola użytkownika nad własnym sekretem (zakończenie pracy na współdzielonym urządzeniu). Zależne od US1.

**Independent Test**: Zapisz klucz (status „aktywny") → usuń → status „brak"; kolejne wywołanie generacji odrzucone `settings.api_key_missing`.

**Acceptance Scenarios**:

1. **Given** zapisany aktywny klucz, **When** użytkownik usuwa klucz, **Then** status zmienia się na „brak", a klucz przestaje być dostępny dla generacji.
2. **Given** brak zapisanego klucza, **When** użytkownik wywołuje usunięcie, **Then** operacja kończy się bez błędu i stan pozostaje „brak" (idempotentne).

---

### User Story 5 - Brak wycieków sekretu (Priority: P1)

Przy każdej operacji korzystającej z klucza pełna wartość nie pojawia się w logach aplikacji ani w treści odpowiedzi HTTP w żadnej formie.

**Why this priority**: Bezpieczeństwo sekretu użytkownika to twarde wymaganie case-study (BYOK). Naruszenie podważa cały model zaufania funkcji.

**Independent Test**: Wykonaj zapis/status/usunięcie i wywołanie generacji, przeskanuj zebrane logi i treści odpowiedzi — pełna wartość klucza nie występuje.

**Acceptance Scenarios**:

1. **Given** operacja zapisu klucza, **When** przeglądane są logi aplikacji z tej operacji, **Then** pełna wartość klucza nie występuje w żadnym wpisie.
2. **Given** dowolna odpowiedź endpointów ustawień lub generacji, **When** analizowana jest treść odpowiedzi, **Then** nie zawiera pełnej wartości klucza (co najwyżej maskę).

---

### Edge Cases

- **Klucz poprawny składniowo, ale odrzucony przez dostawcę** (brak środków, cofnięte uprawnienia) → walidacja przy zapisie wychwytuje i przekazuje czytelny komunikat (`settings.invalid_api_key`); klucz nie jest zapamiętywany.
- **Dostawca nieosiągalny podczas walidacji** (timeout / 5xx / brak sieci) → osobny błąd przejściowy `settings.validation_unavailable`; klucz nie jest zapamiętywany; użytkownik może ponowić — nie jest to mylone z błędnym kluczem.
- **Zbyt wiele prób zapisu z jednej sesji** (np. spamowanie losowymi kluczami) → throttle per sesja odrzuca nadmiarowe próby (`settings.too_many_attempts`) zanim wykona kolejne płatne wywołanie do dostawcy.
- **Wygaśnięcie wpisu w pamięci sesji w trakcie rozmowy** → następna próba generacji zwraca `settings.api_key_missing`; UI prowadzi do ustawień, historia rozmowy zostaje zachowana.
- **Restart aplikacji** → wszystkie klucze znikają z pamięci (brak persystencji); użytkownik musi podać klucz ponownie — świadomy trade-off.
- **Ponowny zapis (nadpisanie) klucza** → nowa wartość zastępuje poprzednią po ponownej walidacji.
- **Podanie klucza przez sesję innego użytkownika** → klucz jest widoczny wyłącznie w obrębie własnej sesji (izolacja jak w US-01); status i maska nie przeciekają między sesjami.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST umożliwić użytkownikowi zapisanie klucza API dostawcy generacji, podanego wyłącznie w treści żądania po połączeniu szyfrowanym.
- **FR-002**: System MUST zwalidować klucz przy zapisie poprzez minimalne wywołanie testowe u dostawcy i zapamiętać go tylko wtedy, gdy walidacja się powiodła.
- **FR-003**: System MUST odrzucić klucz pusty lub oczywiście niepoprawny składniowo bez wywołania do dostawcy, zwracając `settings.invalid_api_key`.
- **FR-004**: System MUST przy odrzuceniu klucza przez dostawcę zwrócić stabilny kod błędu `settings.invalid_api_key` z czytelnym komunikatem i nie zapamiętać klucza.
- **FR-004a**: System MUST rozróżnić odrzucenie klucza od niemożności zweryfikowania: gdy walidacja nie może się zakończyć z powodu problemu po stronie dostawcy (timeout / błąd serwera / brak łączności), MUST zwrócić przejściowy kod `settings.validation_unavailable`, nie zapamiętać klucza i zasugerować ponowienie.
- **FR-004b**: System MUST ograniczać liczbę prób zapisu/walidacji klucza w obrębie pojedynczej sesji w oknie czasu (throttle per sesja) i po przekroczeniu limitu odrzucać kolejne próby kodem `settings.too_many_attempts` bez wykonywania wywołania do dostawcy.
- **FR-005**: System MUST przechowywać klucz wyłącznie w pamięci sesji serwera powiązanej z identyfikatorem sesji użytkownika i NIGDY nie zapisywać go w trwałym magazynie danych.
- **FR-006**: System MUST wiązać czas życia zapamiętanego klucza z czasem życia sesji, tak że wygaśnięcie sesji lub restart aplikacji usuwa klucz.
- **FR-007**: System MUST udostępniać status klucza dla sesji w jednej z postaci: „brak" lub „aktywny".
- **FR-008**: System MUST przy statusie „aktywny" zwracać wyłącznie maskę klucza (rozpoznawalny prefiks + cztery ostatnie znaki) i NIGDY pełnej wartości.
- **FR-009**: System MUST zablokować możliwość wywołania generacji, gdy sesja nie ma aktywnego klucza (poza trybem demo), zwracając stabilny kod `settings.api_key_missing`.
- **FR-010**: Users MUST być w stanie usunąć zapamiętany klucz, po czym status wraca do „brak"; ponowne usunięcie przy braku klucza jest bezbłędne (idempotentne).
- **FR-011**: System MUST zapewnić, że pełna wartość klucza nie pojawia się w logach aplikacji ani w treści odpowiedzi HTTP.
- **FR-012**: System MUST zapewnić izolację klucza między sesjami — klucz, status i maska są dostępne wyłącznie w obrębie sesji, która zapisała klucz (spójnie z izolacją danych US-01).
- **FR-013**: System MUST oznaczać odpowiedzi endpointów ustawień jako niebuforowalne, aby maska ani status nie były przechowywane przez pośredniki/przeglądarkę.
- **FR-014**: System MUST udostępniać interfejs ustawień z polem na klucz ukrywającym wpisywane znaki oraz rozróżnialnymi stanami: brak klucza, klucz aktywny (maska), błąd walidacji.
- **FR-015**: Interfejs czatu MUST w stanie „brak aktywnego klucza" prezentować zablokowane pole pytania z komunikatem i odnośnikiem do ustawień.

### Key Entities *(include if feature involves data)*

- **Klucz API sesji (session API key)**: sekret dostawcy generacji powiązany z identyfikatorem sesji użytkownika; atrybuty obserwowalne z zewnątrz: status (brak/aktywny), maska (prefiks + 4 ostatnie znaki), czas życia zależny od sesji. Pełna wartość istnieje wyłącznie w pamięci serwera na czas trwania sesji.
- **Status klucza (key status)**: pochodna reprezentacja stanu sesji względem klucza, używana przez ustawienia (co pokazać) i czat (czy odblokować pytania).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Użytkownik z ważnym kluczem przechodzi od otwarcia ustawień do statusu „aktywny" w jednej próbie, poniżej 30 sekund.
- **SC-002**: 100% prób zapisania nieprawidłowego lub odrzuconego klucza kończy się czytelnym komunikatem błędu i pozostawia sesję bez zapamiętanego klucza.
- **SC-002a**: Awaria dostawcy podczas walidacji nigdy nie skutkuje zapamiętaniem klucza ani komunikatem „klucz nieprawidłowy" — użytkownik zawsze otrzymuje odróżnialny komunikat o przejściowym problemie z możliwością ponowienia.
- **SC-003**: W żadnym momencie po zapisaniu pełna wartość klucza nie jest widoczna w interfejsie ani zwracana przez API — ujawniana jest wyłącznie maska (0 przypadków pełnej wartości w odpowiedziach).
- **SC-004**: 100% wywołań generacji bez aktywnego klucza (poza demo) jest odrzucanych stabilnym kodem błędu, bez próby kontaktu z dostawcą.
- **SC-005**: Skan logów i treści odpowiedzi z pełnego przebiegu (zapis → status → generacja → usunięcie) nie zawiera ani jednego wystąpienia pełnej wartości klucza.
- **SC-006**: Po usunięciu klucza kolejne wywołanie generacji jest zablokowane, a status wraca do „brak" natychmiast (w tej samej sesji, bez odświeżenia po stronie serwera).

## Assumptions

- Sesja użytkownika i jej identyfikator (US-01) istnieją i są nośnikiem powiązania klucza; izolacja danych działa jak w US-01 (cudzy zasób → 404).
- Dotyczy wyłącznie klucza **generacji** (Claude). Embeddingi są scentralizowane po stronie aplikacji z osobnym kluczem aplikacyjnym (US-06) i są poza zakresem tej funkcji.
- Tryb demo (US-03) jako alternatywna ścieżka „bez własnego klucza" istnieje pojęciowo; tutaj istotne jest jedynie, że blokada czatu obowiązuje „poza trybem demo". Pełna implementacja demo jest poza zakresem.
- Walidacja klucza opiera się na pojedynczym, minimalnym wywołaniu testowym u dostawcy; koszt takiego wywołania jest pomijalny.
- Komunikacja klient–serwer odbywa się po połączeniu szyfrowanym (HTTPS) w środowiskach docelowych.
- Sam czat/RAG (US-14+) jest poza zakresem; ta funkcja dostarcza jedynie strażnika „jest/brak klucza" i wytwórcę klienta generacji do wykorzystania później.

## Out of Scope

- Szyfrowanie klucza „at-rest" (brak jakiejkolwiek persystencji, więc nie dotyczy).
- Obsługa wielu dostawców lub kluczy innych niż dostawca generacji (np. OpenAI).
- Pełna funkcjonalność czatu, retrieval i generacja odpowiedzi (US-14+).
- Pełny tryb demo (US-03) — tu wyłącznie warunek „poza trybem demo" dla blokady czatu.
- Rotacja/wygaszanie kluczy inne niż naturalne wygaśnięcie sesji.
