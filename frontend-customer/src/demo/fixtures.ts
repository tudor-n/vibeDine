// Every piece of hardcoded data the demo session serves, typed against the
// REAL wire contracts so drift breaks `npm run type-check`. Content rules:
// dish names/descriptions are Romanian-only (menus are single-language tenant
// data); Vibey copy is locale-aware because the request carries `locale`.
import type { JoinTableResponse, PartyMember } from '@/services/api-client'
import type { MenuResponse } from '@shared/types/api-responses'
import type { VibeyRecommendationRequest } from '@shared/types/api-requests'
import type { VibeyRecommendationResponse, VibeyRec } from '@shared/types/api-responses'
import type {
  ActiveHappyHour,
  DeviceSessionOrder,
  PendingFlashDealPayload,
} from '@shared/types/models'
import { roundMoney } from '@/utils/money'

export const DEMO_RESTAURANT_ID = '00000000-0000-4000-8000-00000000d001'
export const DEMO_TABLE_ID = '00000000-0000-4000-8000-00000000d002'
export const DEMO_GUEST_ID = '00000000-0000-4000-8000-00000000d003'

// Stable product ids so Vibey/happy-hour/flash-deal fixtures can reference them.
const P = {
  bruschete: 'demo-p-01', hummus: 'demo-p-02', zacusca: 'demo-p-03',
  burger: 'demo-p-11', paste: 'demo-p-12', somon: 'demo-p-13', risotto: 'demo-p-14',
  limonada: 'demo-p-21', ipa: 'demo-p-22', cafea: 'demo-p-23', vin: 'demo-p-24',
  lava: 'demo-p-31', papanasi: 'demo-p-32', tiramisu: 'demo-p-33',
} as const

export const DEMO_MENU: MenuResponse = {
  categories: [
    {
      id: 'demo-c-1', name: 'Aperitive', sortOrder: 1, imageUrl: null,
      products: [
        { id: P.bruschete, categoryId: 'demo-c-1', name: 'Bruschete cu roșii', description: 'Pâine prăjită, roșii coapte, busuioc, ulei de măsline', price: 24.5, imageUrl: null, isAvailable: true },
        { id: P.hummus, categoryId: 'demo-c-1', name: 'Hummus cu lipie caldă', description: 'Năut cremos, tahini, boia afumată', price: 26, imageUrl: null, isAvailable: true },
        { id: P.zacusca, categoryId: 'demo-c-1', name: 'Zacuscă de casă', description: 'Rețeta bunicii, cu pâine de casă', price: 22, imageUrl: null, isAvailable: true },
      ],
    },
    {
      id: 'demo-c-2', name: 'Feluri principale', sortOrder: 2, imageUrl: null,
      products: [
        { id: P.burger, categoryId: 'demo-c-2', name: 'Burger Angus', description: 'Vită Angus, cheddar maturat, bacon crocant, cartofi wedges', price: 52, imageUrl: null, isAvailable: true },
        { id: P.paste, categoryId: 'demo-c-2', name: 'Paste carbonara', description: 'Guanciale, pecorino, gălbenuș de ou', price: 42.5, imageUrl: null, isAvailable: true },
        { id: P.somon, categoryId: 'demo-c-2', name: 'Somon la grătar', description: 'File de somon, legume de sezon, sos de lămâie', price: 64, imageUrl: null, isAvailable: true },
        { id: P.risotto, categoryId: 'demo-c-2', name: 'Risotto cu ciuperci', description: 'Orez arborio, ciuperci de pădure, parmezan', price: 46, imageUrl: null, isAvailable: true },
      ],
    },
    {
      id: 'demo-c-3', name: 'Băuturi', sortOrder: 3, imageUrl: null,
      products: [
        { id: P.limonada, categoryId: 'demo-c-3', name: 'Limonadă cu mentă', description: 'Lămâie proaspătă, mentă, miere', price: 18, imageUrl: null, isAvailable: true },
        { id: P.ipa, categoryId: 'demo-c-3', name: 'Bere artizanală IPA', description: 'IPA locală, note citrice, 5.9%', price: 22, imageUrl: null, isAvailable: true },
        { id: P.cafea, categoryId: 'demo-c-3', name: 'Cafea specialitate', description: 'Espresso dublu, origine Etiopia', price: 14, imageUrl: null, isAvailable: true },
        { id: P.vin, categoryId: 'demo-c-3', name: 'Vin alb (pahar)', description: 'Fetească regală, sec', price: 24, imageUrl: null, isAvailable: true },
      ],
    },
    {
      id: 'demo-c-4', name: 'Deserturi', sortOrder: 4, imageUrl: null,
      products: [
        { id: P.lava, categoryId: 'demo-c-4', name: 'Lava cake', description: 'Ciocolată belgiană, interior curgător, înghețată de vanilie', price: 28, imageUrl: null, isAvailable: true },
        { id: P.papanasi, categoryId: 'demo-c-4', name: 'Papanași cu smântână', description: 'Cu dulceață de afine și smântână', price: 26, imageUrl: null, isAvailable: true },
        { id: P.tiramisu, categoryId: 'demo-c-4', name: 'Tiramisu', description: 'Mascarpone, espresso, cacao', price: 25, imageUrl: null, isAvailable: true },
      ],
    },
  ],
}

function findProduct(id: string) {
  const p = DEMO_MENU.categories.flatMap((c) => c.products).find((x) => x.id === id)
  if (!p) throw new Error(`Demo fixture references unknown product ${id}`)
  return p
}

export function demoPartyMembers(): PartyMember[] {
  return [{ guestId: DEMO_GUEST_ID, displayName: 'Oaspete 1', itemCount: 0, total: 0, status: 'draft' }]
}

function readStoredOrders(): DeviceSessionOrder[] {
  try {
    const raw = sessionStorage.getItem('sessionOrders')
    return raw ? (JSON.parse(raw) as DeviceSessionOrder[]) : []
  } catch {
    return []
  }
}

// The draft orderId must survive a reload (the real backend restores the same
// draft) but must NOT be reused once that order was submitted — orderAgain()
// re-joins expecting a fresh draft. sessionOrders (persisted by
// registerSubmittedRound / the session store) records which ids are spent.
function resolveDraftOrderId(): string {
  const stored = sessionStorage.getItem('orderId')
  const spent = new Set(readStoredOrders().map((o) => o.id))
  if (stored && !spent.has(stored)) return stored
  return crypto.randomUUID()
}

export function buildDemoJoinResponse(): JoinTableResponse {
  const orders = readStoredOrders()
  const cumulativeTotal = Number(sessionStorage.getItem('cumulativeTotal')) || 0
  const origin = window.location.origin
  return {
    guestId: DEMO_GUEST_ID,
    displayName: 'Oaspete 1',
    restaurantId: DEMO_RESTAURANT_ID,
    restaurantName: 'Aroma Bistro',
    restaurantAddress: 'Str. Demo 1, București',
    organizationName: 'Aroma Group',
    tableId: DEMO_TABLE_ID,
    tableNumber: '7',
    activeOrder: { id: resolveDraftOrderId(), status: 'draft', totalAmount: 0, items: [] },
    partyMembers: demoPartyMembers(),
    branding: {
      enabled: true,
      primary: '#B4532A',
      secondary: '#3E7C59',
      background: '#F6F1E7',
      logoUrl: `${origin}/demo/logo.svg`,
      photoUrl: `${origin}/demo/welcome.svg`,
      menuBackgroundUrl: null,
      menuBackgroundStyle: null,
      presetKey: null,
      logoBackground: null,
      logoIsDark: false,
    },
    session: {
      id: '00000000-0000-4000-8000-00000000d004',
      // 24h — effectively unlimited for a demo. MUST stay < ~24.8 days:
      // useLinkExpiry uses setTimeout(seconds*1000) and a 32-bit overflow
      // fires the expiry overlay IMMEDIATELY.
      linkExpirySeconds: 86_400,
      cumulativeTotal,
      orders,
      pendingDeal: null,
    },
  }
}

// Always-on ("24/7") happy hour: −20% on the IPA. endTime is recomputed on
// every poll to ~2h from now (capped at 23:59 — secondsUntil() evaluates
// "HH:mm" against TODAY, so a wrapped past-midnight time would read as
// already over; near midnight the countdown just runs to 23:59 and the next
// poll after midnight restores the 2h window).
export function buildDemoHappyHour(): ActiveHappyHour {
  const product = findProduct(P.ipa)
  const end = new Date(Date.now() + 2 * 60 * 60 * 1000)
  const sameDay = end.getDate() === new Date().getDate()
  const hh = sameDay ? String(end.getHours()).padStart(2, '0') : '23'
  const mm = sameDay ? String(end.getMinutes()).padStart(2, '0') : '59'
  return {
    productId: product.id,
    productName: product.name,
    discountPercent: 20,
    originalPrice: product.price,
    discountedPrice: roundMoney(product.price * 0.8),
    startTime: '00:00',
    endTime: `${hh}:${mm}`,
  }
}

// Hardcoded Vibey picks with per-locale reasons. Served minus whatever is
// already in the cart, so Vibey never suggests something the guest has.
const VIBEY_POOL: Array<{ id: string; reason: { ro: string; en: string } }> = [
  { id: P.ipa, reason: { ro: 'Amăreala citrică taie perfect grăsimea burgerului.', en: 'Its citrus bitterness cuts right through a rich main.' } },
  { id: P.limonada, reason: { ro: 'Proaspătă și dulce-acrișoară — echilibrează felurile sărate.', en: 'Fresh and tangy — balances the savoury dishes.' } },
  { id: P.bruschete, reason: { ro: 'Un start ușor cât aștepți felul principal.', en: 'A light start while your main is on the way.' } },
  { id: P.lava, reason: { ro: 'Finalul clasic: ciocolată caldă după un fel consistent.', en: 'The classic finish: warm chocolate after a hearty main.' } },
]

export function buildDemoVibeyResponse(
  req: VibeyRecommendationRequest,
): VibeyRecommendationResponse {
  const inCart = new Set(req.items.map((i) => i.id))
  const recommendations: VibeyRec[] = VIBEY_POOL.filter((r) => !inCart.has(r.id))
    .slice(0, 3)
    .map((r) => {
      const p = findProduct(r.id)
      return {
        id: p.id,
        name: p.name,
        category: DEMO_MENU.categories.find((c) => c.id === p.categoryId)?.name ?? null,
        priceRon: p.price,
        reason: req.locale === 'en' ? r.reason.en : r.reason.ro,
      }
    })
  const summary =
    req.locale === 'en'
      ? 'I looked at your picks — here is what would round them off nicely.'
      : 'M-am uitat peste alegerile tale — uite ce le-ar completa de minune.'
  return { summary, recommendations }
}

// Post-order upsell: Lava cake at −25%, claimable for 2 minutes. Shape mirrors
// the FlashDealAvailable WS payload so buildFlashDealFromPayload maps it as-is.
export function buildDemoFlashDealPayload(orderId: string): PendingFlashDealPayload {
  const product = findProduct(P.lava)
  return {
    dealId: crypto.randomUUID(),
    orderId,
    productId: product.id,
    productName: product.name,
    imageUrl: null,
    originalPrice: product.price,
    discountPrice: roundMoney(product.price * 0.75),
    discountPercent: 25,
    expiresAt: new Date(Date.now() + 120_000).toISOString(),
    quantity: 1,
  }
}
