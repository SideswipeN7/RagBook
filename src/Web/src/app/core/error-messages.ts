/**
 * The single source of truth mapping stable backend error codes (`module.snake_case`, the wire contract) to their
 * Polish user-facing messages (US-19). Every code the API can return has a dedicated entry here — the six former
 * per-feature maps (chat / tree / selection / upload / api-key / folder) were consolidated into this one dictionary,
 * so there is exactly one wording per code. {@link messageForCode} resolves a code to its message, using a neutral
 * fallback **only** for a code absent from the catalog (a completeness spec forbids a known code falling through).
 */
export const ERROR_MESSAGES: Record<string, string> = {
  // Session (US-01)
  'session.resource_not_found': 'Zasób nie istnieje lub został usunięty.',
  'session.name_required': 'Nazwa jest wymagana.',
  'session.resource_already_exists': 'Zasób o tej nazwie już istnieje.',
  'session.concurrency_conflict': 'Zasób został zmieniony w innej karcie — odśwież i spróbuj ponownie.',

  // Documents (US-04/06/08/10/12)
  'document.unsupported_file_type': 'Nieobsługiwany typ pliku. Dozwolone: PDF, TXT, Markdown.',
  'document.empty_file': 'Plik jest pusty.',
  'document.not_found': 'Plik już nie istnieje.',
  'document.read_only': 'Ten plik jest tylko do odczytu i nie można go zmienić.',
  'document.bulk_validation_failed':
    'Niektórych zaznaczonych pozycji nie można przetworzyć — oznaczono je na liście. Popraw zaznaczenie i spróbuj ponownie.',
  'document.bulk_empty': 'Nie zaznaczono żadnych dokumentów.',
  'document.bulk_too_large': 'Zaznaczono zbyt wiele dokumentów naraz.',

  // Quota (US-05)
  'quota.exceeded': 'Limit plików osiągnięty — usuń pliki, aby wgrać nowe.',
  'quota.conflict': 'Nie udało się zaktualizować limitu — spróbuj ponownie.',
  'quota.invalid_size': 'Nieprawidłowy rozmiar pliku.',
  'quota.total_size_exceeded': 'Brak miejsca w limicie — usuń pliki, aby wgrać nowe.',
  'quota.file_too_large': 'Plik przekracza dozwolony rozmiar.',

  // Folders (US-09/11)
  'folder.invalid_name': 'Nieprawidłowa nazwa folderu.',
  'folder.max_depth_exceeded': 'Osiągnięto maksymalną głębokość zagnieżdżenia.',
  'folder.duplicate_name': 'Folder o tej nazwie już istnieje w tym miejscu.',
  'folder.not_empty': 'Usuń lub przenieś zawartość przed usunięciem folderu.',
  'folder.not_found': 'Folder nie istnieje.',
  'folder.conflict': 'Nie udało się wykonać operacji na folderze — spróbuj ponownie.',
  'folder.circular_move': 'Nie można przenieść folderu do niego samego ani do jego podfolderu.',

  // Chat / RAG (US-13/14/15/17/18)
  'chat.scope_not_found': 'Wybrany zakres już nie istnieje — przełącz na „Wszystkie".',
  'chat.invalid_question': 'Pytanie jest puste lub zbyt długie.',
  'chat.provider_rate_limited': 'Zbyt wiele zapytań — spróbuj ponownie za chwilę.',
  'chat.provider_unavailable': 'Usługa AI jest chwilowo niedostępna — spróbuj ponownie.',
  'chat.conversation_not_found': 'Ta rozmowa już nie istnieje.',

  // Demo (US-03)
  'chat.demo_limit_reached': 'Wykorzystano limit pytań demo. Dodaj własny klucz API, aby pytać dalej.',
  'chat.demo_rate_limited': 'Zbyt wiele pytań demo z Twojej sieci — spróbuj ponownie później.',
  'chat.demo_unavailable': 'Tryb demo jest chwilowo niedostępny. Spróbuj ponownie później.',

  // Settings / BYOK (US-02)
  'settings.invalid_api_key': 'Klucz API został odrzucony przez Anthropic — sprawdź go w ustawieniach.',
  'settings.validation_unavailable': 'Nie można teraz zweryfikować klucza. Spróbuj ponownie za chwilę.',
  'settings.api_key_missing': 'Skonfiguruj klucz API w ustawieniach, aby zadać pytanie.',
  'settings.too_many_attempts': 'Zbyt wiele prób. Odczekaj chwilę i spróbuj ponownie.',

  // Global (US-19)
  'validation.failed': 'Dane są nieprawidłowe. Popraw je i spróbuj ponownie.',
  'error.unexpected': 'Wystąpił nieoczekiwany błąd. Spróbuj ponownie.',
};

/** The neutral fallback for a code not present in {@link ERROR_MESSAGES} (an unknown/newer server code). */
export const GENERIC_ERROR_MESSAGE = 'Wystąpił nieoczekiwany błąd. Spróbuj ponownie.';

/**
 * Resolves an error <paramref name="code"/> to its Polish message. A known code always returns its dedicated entry;
 * an absent/undefined code returns <paramref name="fallback"/> when given, else {@link GENERIC_ERROR_MESSAGE}. Pass
 * a surface-specific fallback (e.g. "Nie udało się wgrać pliku.") for a friendlier default on unknown codes.
 */
export function messageForCode(code: string | undefined | null, fallback?: string): string {
  return (code && ERROR_MESSAGES[code]) || fallback || GENERIC_ERROR_MESSAGE;
}

/**
 * Appends a short report id (the correlation id from the error response's `X-Trace-Id` / `traceId`) to a message,
 * so a user can quote it to support (US-19 AC-4). The full W3C trace id is long, so a compact suffix is shown.
 */
export function withReportId(message: string, traceId: string | undefined | null): string {
  if (!traceId) {
    return message;
  }
  const parts = traceId.split('-');
  const shortId = (parts.length >= 2 ? parts[1] : traceId).slice(0, 12);

  return `${message} (zgłoszenie: ${shortId})`;
}
