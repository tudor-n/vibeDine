// Deterministic Vibey copy — no Math.random, no network. The teaser is picked
// once per cart identity (handoff §4.1); the post-add follow-up interpolates
// the added item name (handoff §4.2). Keeping this pure/side-effect-free lets
// the cost-invariant tests assert stability without mocking a store.
import type { Locale } from '@/composables/useLocale'

// Five Romanian teasers, verbatim from the design handoff §4.1, plus a
// parallel English set. Deliberately generic — nothing about the picks is
// known yet, so these must never reference a dish or a recommendation count.
export const VIBEY_TEASERS_RO: readonly string[] = [
  'Am aruncat un ochi peste ce ai ales — cred că îți pot completa frumos masa. Vrei să vezi ce zic?',
  'Masa ta arată bine deja. Am câteva idei care s-ar potrivi de minune — ți le arăt?',
  'Pot să-ți rotunjesc comanda cu câteva alegeri gândite pentru ce ai în coș. Curios?',
  'Câteva completări bine alese ar duce masa asta la nivelul următor. Le deschidem?',
  'Mi-a atras atenția ce-ai comandat — cred că merge grozav cu încă ceva. Vrei o sugestie?',
]

export const VIBEY_TEASERS_EN: readonly string[] = [
  "I took a look at what you picked — I think I can round out your meal nicely. Want to see what I've got?",
  "Your table already looks great. I've got a few ideas that would fit perfectly — want me to show you?",
  'I could round out your order with a few thoughtful picks for what you have so far. Curious?',
  'A few well-chosen extras could take this meal to the next level. Shall we open them up?',
  "What you ordered caught my eye — I think it'd go great with one more thing. Want a suggestion?",
]

export const VIBEY_TEASERS: Record<Locale, readonly string[]> = {
  ro: VIBEY_TEASERS_RO,
  en: VIBEY_TEASERS_EN,
}

// Deterministic string hash — mirrors the rotation style of `toneFromKey` in
// design/tokens.ts — so the teaser is stable per cart identity (`seed`, the
// draft orderId) for as long as the guest sits, without touching Math.random.
function hashSeed(seed: string): number {
  let h = 0
  for (let i = 0; i < seed.length; i++) h = (h * 31 + seed.charCodeAt(i)) >>> 0
  return h
}

/** Deterministic teaser pick — same `seed` + `locale` always returns the same line. */
export function pickTeaser(seed: string, locale: Locale): string {
  const list = VIBEY_TEASERS[locale] ?? VIBEY_TEASERS_RO
  const index = hashSeed(seed) % list.length
  // list is always one of the two fixed 5-element arrays above, so index is
  // always in-bounds; the assertion just satisfies noUncheckedIndexedAccess.
  return (list[index] ?? list[0]) as string
}

// Calm empty-state line for when the AI genuinely returns zero picks (an
// exhausted candidate pool, or the graceful `{summary:"",recommendations:[]}`
// failure path). The design requires Vibey to simply not push anything rather
// than falsely claim everything was added. Deterministic — no model call.
export function nothingToSuggest(locale: Locale): string {
  return locale === 'en'
    ? 'Nothing to add right now — your table already looks complete!'
    : 'Deocamdată n-am ce să-ți sugerez — dar masa ta arată completă!'
}

// Post-add follow-up (handoff §4.2). Deterministic — no model call. Branches
// on whether more recommendations remain in the sheet.
export function followUpLine(name: string, remaining: number, locale: Locale): string {
  if (locale === 'en') {
    return remaining > 0
      ? `Good choice — ${name} fits perfectly. I've got one more idea and you're all set:`
      : `Perfect. ${name} completes the meal — it looks balanced now. Enjoy!`
  }
  return remaining > 0
    ? `Bună alegere — ${name} se potrivește perfect. Mai am o idee și ești gata:`
    : `Perfect. ${name} completează masa — arată echilibrat acum. Poftă bună!`
}
