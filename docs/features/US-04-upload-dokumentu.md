# US-04 — Upload dokumentu

**Epik:** Dokumenty | **Priorytet:** P1 | **Zależności:** US-01, US-05 (walidacja quoty), US-09 (wybór folderu — opcjonalnie na start root) | **Szacunek:** M

## Historia
Jako użytkownik wgrywam plik PDF, TXT lub Markdown do wybranego folderu (lub do roota), aby móc zadawać pytania o jego treść.

## Kontekst / decyzje projektowe
- Obsługiwane typy: `application/pdf`, `text/plain`, `text/markdown` — walidacja po sygnaturze pliku (magic bytes), nie tylko rozszerzeniu.
- Upload jest szybki i synchroniczny tylko do momentu zapisu pliku + rekordu `Document(Status=Processing)`; chunking i embeddingi dzieją się w tle (US-06).
- Pliki binarne trzymane poza bazą relacyjną: lokalnie wolumen Dockera, na GCP — Cloud Storage (abstrakcja `IFileStorage`).

## Kryteria akceptacji
### AC-1: Poprawny upload
- GIVEN użytkownik z wolną quotą i wybranym folderem docelowym
- WHEN wgrywa poprawny plik PDF ≤ 10 MB
- THEN API zwraca 201 z obiektem dokumentu (`Status=Processing`); plik pojawia się natychmiast w drzewie ze statusem przetwarzania

### AC-2: Walidacja typu
- GIVEN plik .exe przemianowany na .pdf
- WHEN użytkownik próbuje go wgrać
- THEN walidacja magic bytes odrzuca plik; `Result.Failure(UnsupportedFileType)` z komunikatem wskazującym dozwolone formaty

### AC-3: Walidacja rozmiaru
- GIVEN plik > `MaxFileSizeMb`
- WHEN użytkownik próbuje go wgrać
- THEN odrzucenie po stronie serwera (`Result.Failure(FileTooLarge)`); UI dodatkowo waliduje przed wysyłką, ale źródłem prawdy jest serwer

### AC-4: Upload do folderu
- GIVEN wybrany folder w drzewie
- WHEN użytkownik wgrywa plik
- THEN dokument otrzymuje `FolderId` wybranego folderu; bez wyboru — `FolderId = NULL` (root)

### AC-5: Duplikat nazwy
- GIVEN w folderze istnieje plik `umowa.pdf`
- WHEN użytkownik wgrywa kolejny `umowa.pdf` do tego samego folderu
- THEN plik zostaje przyjęty z nazwą `umowa (2).pdf` (auto-sufiks) — brak blokady, brak nadpisania

## Zakres techniczny
- **Backend:** slice `Documents/Upload` (multipart/form-data); walidacja: quota (US-05) → typ → rozmiar; zapis przez `IFileStorage`; rekord `Document`; publikacja komunikatu Wolverine `DocumentUploaded(DocumentId)`.
- **DB:** `documents(id, user_session_id, folder_id NULL FK, file_name, content_type, size_bytes, status, chunk_count, storage_path, uploaded_at)`.
- **Frontend:** przycisk uploadu + drag&drop pliku z dysku na drzewo; progress bar; walidacja wstępna (typ/rozmiar) przed wysyłką.

## Przypadki brzegowe
- PDF zaszyfrowany hasłem lub uszkodzony → upload przechodzi, przetwarzanie kończy się `Status=Failed` z powodem (US-06).
- Przerwanie uploadu w połowie → brak rekordu i pliku (transakcyjność: rekord tworzony po udanym zapisie pliku; sprzątanie osieroconych plików przy błędzie).
- Plik 0 bajtów → `Result.Failure(EmptyFile)`.

## Poza zakresem
OCR skanów, DOCX, obrazy, upload wielu plików naraz (pojedynczo w MVP; bulk dotyczy operacji na istniejących — US-12).

## Definition of Done
AC-1–AC-5 z testami integracyjnymi (w tym magic bytes); upload działa lokalnie (wolumen) i na GCP (Cloud Storage) przez tę samą abstrakcję.
