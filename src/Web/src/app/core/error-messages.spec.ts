import { ERROR_MESSAGES, GENERIC_ERROR_MESSAGE, messageForCode, withReportId } from './error-messages';

/**
 * The full set of stable backend error codes (the wire contract, `module.snake_case`). This list is the
 * enforcement point for US-19 AC-1/FR-003: every code here MUST have a dedicated message, and a code the backend
 * adds without a message here will fail the completeness test below. Keep in sync with `docs/features/README.md`.
 */
const ALL_BACKEND_CODES = [
  'session.resource_not_found', 'session.name_required', 'session.resource_already_exists', 'session.concurrency_conflict',
  'document.unsupported_file_type', 'document.empty_file', 'document.not_found', 'document.read_only',
  'document.bulk_validation_failed', 'document.bulk_empty', 'document.bulk_too_large',
  'quota.exceeded', 'quota.conflict', 'quota.invalid_size', 'quota.total_size_exceeded', 'quota.file_too_large',
  'folder.invalid_name', 'folder.max_depth_exceeded', 'folder.duplicate_name', 'folder.not_empty', 'folder.not_found',
  'folder.conflict', 'folder.circular_move',
  'chat.scope_not_found', 'chat.invalid_question', 'chat.provider_rate_limited', 'chat.provider_unavailable',
  'chat.conversation_not_found',
  'chat.demo_limit_reached', 'chat.demo_rate_limited', 'chat.demo_unavailable',
  'settings.invalid_api_key', 'settings.validation_unavailable', 'settings.api_key_missing', 'settings.too_many_attempts',
  'validation.failed', 'error.unexpected',
];

describe('error-messages', () => {
  it('has a dedicated message entry for every stable backend code (AC-1)', () => {
    const missing = ALL_BACKEND_CODES.filter((code) => !Object.prototype.hasOwnProperty.call(ERROR_MESSAGES, code));
    expect(missing).withContext(`codes without a message: ${missing.join(', ')}`).toEqual([]);

    // A known code resolves to its own dedicated entry (never falls through to the fallback branch).
    for (const code of ALL_BACKEND_CODES) {
      expect(messageForCode(code, 'FALLBACK_SENTINEL')).withContext(code).toBe(ERROR_MESSAGES[code]);
    }
  });

  it('has no message keys that are not in the known code list (no stale/typo entries)', () => {
    const known = new Set(ALL_BACKEND_CODES);
    const unexpected = Object.keys(ERROR_MESSAGES).filter((code) => !known.has(code));
    expect(unexpected).withContext(`unexpected dictionary keys: ${unexpected.join(', ')}`).toEqual([]);
  });

  it('returns the generic fallback for an unknown code', () => {
    expect(messageForCode('totally.unknown_code')).toBe(GENERIC_ERROR_MESSAGE);
    expect(messageForCode(undefined)).toBe(GENERIC_ERROR_MESSAGE);
  });

  it('prefers a caller-provided fallback over the generic one for an unknown code', () => {
    expect(messageForCode('nope.unknown', 'Nie udało się wgrać pliku.')).toBe('Nie udało się wgrać pliku.');
    // A known code ignores the fallback and returns its dedicated message.
    expect(messageForCode('document.empty_file', 'Nie udało się wgrać pliku.')).toBe(ERROR_MESSAGES['document.empty_file']);
  });

  it('appends a short report id from a W3C trace id, and is a no-op without one (AC-4)', () => {
    // 00-<trace>-<span>-01 → the short id is the first 12 chars of the trace segment.
    expect(withReportId('Błąd.', '00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01'))
      .toBe('Błąd. (zgłoszenie: 0af7651916cd)');
    expect(withReportId('Błąd.', null)).toBe('Błąd.');
    expect(withReportId('Błąd.', undefined)).toBe('Błąd.');
  });
});
