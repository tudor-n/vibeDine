/**
 * Format a money amount for display (2 decimals), tolerating null/undefined/NaN.
 * Prevents a render crash when a partial payload omits an amount — mirrors the
 * customer app's `utils/money.ts` rounding discipline (CLAUDE.md invariant #3).
 */
export function formatMoney(value: number | null | undefined): string {
  if (value == null || Number.isNaN(value)) return '0.00'
  return value.toFixed(2)
}
