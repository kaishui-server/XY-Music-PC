/**
 * Normalize legacy brand names at user-facing boundaries.
 *
 * Server-managed copy can outlive a desktop release, so remote agreement and
 * about-page text must follow the current product name even when the server
 * still returns the former brand.
 */
export function normalizeBrandText(value: string): string {
  return value
    .replace(/弦予音乐/g, 'XY Music')
    .replace(/弦予/g, 'XY')
    .replace(/XianYu\s+Music/gi, 'XY Music')
    .replace(/XianYu/gi, 'XY');
}
