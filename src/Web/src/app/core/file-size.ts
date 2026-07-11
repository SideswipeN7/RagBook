/**
 * Formats a byte count as a human-readable, **decimal** size (1 MB = 1,000,000 bytes — matching the
 * quota convention, US-05/US-07): `B` below 1 KB, otherwise `KB`/`MB` with one decimal place.
 */
export function formatFileSize(bytes: number): string {
  if (bytes < 1_000) {
    return `${bytes} B`;
  }
  if (bytes < 1_000_000) {
    return `${(bytes / 1_000).toFixed(1)} KB`;
  }

  return `${(bytes / 1_000_000).toFixed(1)} MB`;
}
