# US-20 — Deployment i pakiet case study

**Epik:** Wykończenie | **Priorytet:** P1 (to jest „produkt" projektu) | **Zależności:** wszystkie | **Szacunek:** L

## Historia
Jako osoba oceniająca (rekruter, klient) otwieram publiczny link i w ciągu minuty widzę działający czat z cytatami w trybie demo, a w README znajduję architekturę i decyzje projektowe — bez klonowania repo.

## Kontekst / decyzje projektowe
- Dwa środowiska: **lokalnie** `docker compose up` (API + Angular + Postgres z pgvector + wolumen plików) — jedna komenda, zero konfiguracji poza `.env.example`; **produkcyjnie** GCP: Cloud Run (API + statyki Angular lub oddzielny hosting), Cloud SQL Postgres z pgvector, Cloud Storage na pliki, Secret Manager na klucz demo i klucz embeddingów.
- Migracje bazy uruchamiane automatycznie przy starcie (z blokadą — bezpieczne przy wielu instancjach) lub jako krok deployu — decyzja opisana w README.
- Seed dokumentów demo (US-03) idempotentny — działa na obu środowiskach.
- README jest pierwszoplanowym artefaktem case study.

## Kryteria akceptacji
### AC-1: Uruchomienie lokalne
- GIVEN czysta maszyna z Dockerem
- WHEN `cp .env.example .env` (uzupełnienie 2 kluczy) i `docker compose up`
- THEN aplikacja działa pod localhost; migracje i seed wykonane; tryb demo działa od razu

### AC-2: Deploy produkcyjny
- GIVEN skonfigurowany projekt GCP
- WHEN wykonywany jest udokumentowany proces deployu (skrypt `gcloud`/`cloudbuild.yaml`)
- THEN publiczny URL serwuje aplikację; SSE działa przez Cloud Run (timeout requestu zweryfikowany); sekrety wyłącznie z Secret Manager

### AC-3: Ścieżka oceniającego < 1 min
- GIVEN publiczny URL z README/CV
- WHEN odwiedzający otwiera link
- THEN bez żadnej konfiguracji: widzi dokumenty demo → zadaje przykładowe pytanie (UI podpowiada 2–3 gotowe pytania) → widzi streaming + klikalne cytaty

### AC-4: README jako case study
- GIVEN repozytorium
- WHEN czytane jest README
- THEN zawiera: (1) opis problemu i demo-GIF na górze, (2) diagram architektury (mermaid), (3) sekcję „Decyzje projektowe" — minimum: materialized path vs CTE, pre-filtering w pgvector vs osobne indeksy, BYOK i brak persystencji sekretów, próg groundingu i fraza-sentinel, jeden model embeddingów, all-or-nothing w bulk — każda z uzasadnieniem i odrzuconą alternatywą, (4) pipeline RAG krok po kroku, (5) katalog kodów błędów, (6) known limitations / future work, (7) instrukcje uruchomienia

### AC-5: Higiena repo
- GIVEN publiczne repozytorium
- WHEN przeglądane
- THEN brak sekretów w historii gitów; `.env.example` kompletny; CI (GitHub Actions) buduje i uruchamia testy na PR; licencja i krótki opis w About

## Zakres techniczny
- **Infra:** Dockerfile (multi-stage: build Angular + publish .NET), docker-compose z obrazem `pgvector/pgvector`, skrypt deploy GCP, GitHub Actions (build + test).
- **Dokumentacja:** README + diagram mermaid + GIF-y (upload→pytanie→cytat; drag&drop).

## Przypadki brzegowe
- Zimny start Cloud Run → akceptowalny; min-instances=0 dla kosztów (odnotować, że pierwszy request może trwać kilka sekund).
- Budżet klucza demo wyczerpany → zachowanie z US-03 (czytelny komunikat, apka nie wygląda na zepsutą).

## Poza zakresem
Custom domain (opcjonalnie), monitoring/alerting, IaC (Terraform) — wymienić jako future work.

## Definition of Done
AC-1–AC-5 spełnione; link demo + link repo gotowe do wpisania w CV/LinkedIn; README przeczytane „na świeżo" pod kątem ścieżki rekrutera.
