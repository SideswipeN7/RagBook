# US-07 — Drzewo folderów i lista dokumentów

**Epik:** Dokumenty | **Priorytet:** P1 | **Zależności:** US-04, US-09 | **Szacunek:** M

## Historia
Jako użytkownik widzę swoje dokumenty w drzewie folderów z kluczowymi informacjami (nazwa, rozmiar, status, liczba chunków, data), aby szybko orientować się w swojej bazie wiedzy i nią zarządzać.

## Kontekst / decyzje projektowe
- Jeden widok główny aplikacji: panel drzewa (foldery + pliki) obok panelu czatu.
- Drzewo budowane po stronie klienta z płaskiej listy (foldery mają `path` — sortowanie i grupowanie po prefiksie); Angular CDK `cdk-tree` (nested tree, max 3 poziomy więc bez wirtualizacji).
- Pliki w rootcie (bez folderu) widoczne na najwyższym poziomie.
- Sekcja „Demo" (US-03) renderowana osobno, tylko do odczytu.

## Kryteria akceptacji
### AC-1: Render drzewa
- GIVEN użytkownik z folderami A, A/B i plikami w A, A/B i rootcie
- WHEN otwiera aplikację
- THEN widzi drzewo odzwierciedlające hierarchię; foldery rozwijane/zwijane; stan rozwinięcia zachowany w ramach sesji przeglądarki (sessionStorage stanu UI — nie danych)

### AC-2: Metadane pliku
- GIVEN plik w statusie Ready
- WHEN użytkownik patrzy na wiersz pliku
- THEN widzi: nazwę, rozmiar (czytelny format), badge statusu, liczbę chunków, datę uploadu; dla Processing — spinner; dla Failed — ikonę błędu z tooltipem `FailureReason`

### AC-3: Puste stany
- GIVEN nowa sesja bez plików
- WHEN otwiera widok
- THEN widzi empty state z CTA „Wgraj pierwszy dokument" i wskazaniem trybu demo

### AC-4: Odświeżanie
- GIVEN operacje upload/delete/move wykonane w innych US
- WHEN kończą się sukcesem
- THEN drzewo aktualizuje się bez pełnego przeładowania strony (wspólny store/sygnały Angular)

### AC-5: Wydajność zapytania
- GIVEN maksymalny stan (10 plików, do 3 poziomów folderów)
- WHEN ładowany jest widok
- THEN dane przychodzą z jednego wywołania `GET /api/tree` (foldery + dokumenty w jednej odpowiedzi), bez N+1

## Zakres techniczny
- **Backend:** slice `Tree/GetTree` — dwa zapytania (foldery sesji, dokumenty sesji), złożenie DTO.
- **Frontend:** komponent drzewa (cdk-tree), store stanu (signals), komponent wiersza pliku, empty states.

## Przypadki brzegowe
- Folder pusty → renderowany z możliwością rozwinięcia (pokazuje „pusty folder").
- Bardzo długa nazwa pliku → ellipsis + pełna nazwa w tooltipie.

## Poza zakresem
Wyszukiwarka po nazwach, sortowanie konfigurowalne (stałe: foldery alfabetycznie, pliki po dacie malejąco), wirtualizacja listy.

## Definition of Done
AC-1–AC-5 z testami komponentów (przynajmniej render drzewa i stany pliku); widok spójny wizualnie z resztą aplikacji.
