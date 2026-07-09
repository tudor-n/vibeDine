// Money helpers. Prices are RON with 2-decimal (cent) precision, but summing
// floats — e.g. 36.95 * 3, or 73.9 + 36.95 + … — leaks IEEE-754 noise such as
// 226.85000000000002. Round to the nearest cent before storing or displaying so
// a total never shows that long tail.
export function roundMoney(value: number): number {
  return Math.round(value * 100) / 100
}

// Display string for a money amount: rounded to the cent with trailing zeros
// trimmed, so 73.90 → "73.9" and 226.85000000000002 → "226.85" — matching the
// app's existing compact price style.
export function formatMoney(value: number): string {
  return roundMoney(value).toString()
}
