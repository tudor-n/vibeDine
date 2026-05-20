import { ref, watch } from 'vue'

export type Locale = 'ro' | 'en'

const STORAGE_KEY = 'clarifi.locale'

type Dict = Record<string, string>

const ro: Dict = {
  'lang.choose.title': 'Alegeți limba',
  'lang.choose.subtitle': 'Selectați limba pentru meniu',
  'lang.choose.welcome': 'Bun venit',
  'lang.ro': 'Română',
  'lang.en': 'English',
  'lang.continue': 'Continuă',

  'common.back': 'Înapoi',
  'common.close': 'Închide',
  'common.table': 'Masa',
  'common.ron': 'RON',

  'session.invalidTitle': 'Cod QR invalid',
  'session.missingMessage': 'Scanează codul QR de pe masă pentru a deschide meniul.',
  'session.invalidMessage': 'Acest cod QR nu mai este valid. Întreabă personalul pentru ajutor.',

  'theme.toLight': 'Comutați la tema deschisă',
  'theme.toDark': 'Comutați la tema închisă',

  'welcome.line1': 'Bun venit la',
  'welcome.fallbackLocation': 'Restaurant',
  'welcome.today': 'AZI',
  'welcome.featuredName': 'Mușchi de vită Oltenia',
  'welcome.featuredMeta': 'Maturat 28 zile · 128 RON',

  'pebbles.title1': 'Ce doriți',
  'pebbles.title2': 'să mâncați?',
  'pebbles.subtitle': 'Atingeți o categorie pentru a vedea meniul',
  'pebbles.loading': 'Se încarcă meniul...',
  'pebbles.dailyRecommendation': 'Recomandarea zilei',
  'pebbles.dishesCount': '{count} preparate',

  'menu.fallbackTitle': 'Meniu',
  'menu.dishesCount': '{count} preparate',
  'menu.closeSearch': 'Închide căutarea',
  'menu.openSearch': 'Caută',
  'menu.searchPlaceholder': 'Caută în {category}...',
  'menu.clearSearch': 'Șterge căutarea',
  'menu.filter.all': 'Toate',
  'menu.filter.meat': 'Carne',
  'menu.filter.fish': 'Pește',
  'menu.filter.veggie': 'Vegetarian',
  'menu.filter.chef': 'Chef',
  'menu.noResultsFor': 'Niciun rezultat pentru „{query}".',
  'menu.noResults': 'Niciun preparat în acest filtru.',
  'menu.loading': 'Se încarcă...',
  'menu.add': 'Adaugă',
  'menu.outOfStock': 'Epuizat',
  'menu.recommendedForYou': 'Recomandate pentru dumneavoastră',
  'menu.addRecommendation': 'Adaugă recomandarea',
  'menu.cartLabel': 'Coșul meu',
  'menu.cartCountOne': '{count} preparat',
  'menu.cartCountMany': '{count} preparate',
  'menu.cartGo': 'Coș →',

  'cart.title': 'Comanda mea',
  'cart.selectedOne': '{count} preparat selectat',
  'cart.selectedMany': '{count} preparate selectate',
  'cart.prepEstimate': ' · pregătire aproximativ {minutes} de minute',
  'cart.empty': 'Coșul este gol. Adăugați preparate din meniu.',
  'cart.aiSuggested': 'Sugerat de AI',
  'cart.decreaseQty': 'Scade cantitatea',
  'cart.increaseQty': 'Crește cantitatea',
  'cart.subtotal': 'Subtotal',
  'cart.service': 'Serviciu',
  'cart.serviceIncluded': 'inclus',
  'cart.total': 'Total',
  'cart.errorInvalidSession': 'Sesiunea nu este validă.',
  'cart.errorEmpty': 'Coșul este gol.',
  'cart.errorSubmit': 'Eroare la trimitere.',
  'cart.submitting': 'Se trimite...',
  'cart.submit': 'Trimite la bucătărie',

  'sent.confirmed': 'Confirmat ✓',
  'sent.title1': 'Comanda',
  'sent.title2': 'a plecat',
  'sent.title3': 'la bucătărie!',
  'sent.lede': 'Chef Andrei a primit comanda dumneavoastră. Urmăriți statusul mai jos.',
  'sent.stepSubmitted': 'Trimisă la bucătărie',
  'sent.stepPreparing': 'În pregătire acum',
  'sent.stepServed': 'Servire la masă',
  'sent.now': 'ACUM',
  'sent.totalLabel': 'Total comandă',
  'sent.payHint': 'Plata la final · cash sau card',
  'sent.callWaiter': 'Chemați chelnerul',
  'sent.orderAgain': 'Mai comand',

  'flash.badge': 'Ofertă specială',
  'flash.dismiss': 'Închide oferta',
  'flash.title1': 'Ofertă',
  'flash.titleMark': 'limitată',
  'flash.title3': 'de chef.',
  'flash.defaultReason': 'O porție specială la preț redus, doar câteva minute.',
  'flash.discount': '−{percent}% reducere',
  'flash.meta': "Chef's cut · porție limitată",
  'flash.timeLeft': 'Timp rămas',
  'flash.minutes': 'minute',
  'flash.added': 'Adăugat ✓',
  'flash.adding': 'Se adaugă...',
  'flash.addToOrder': 'Adaugă în comandă',
  'flash.noThanks': 'Nu mulțumesc',
  'flash.errorInvalidSession': 'Sesiune invalidă. Reîncărcați pagina.',
  'flash.errorClaim': 'Eroare la revendicare.',
}

const en: Dict = {
  'lang.choose.title': 'Choose your language',
  'lang.choose.subtitle': 'Select the menu language',
  'lang.choose.welcome': 'Welcome',
  'lang.ro': 'Română',
  'lang.en': 'English',
  'lang.continue': 'Continue',

  'common.back': 'Back',
  'common.close': 'Close',
  'common.table': 'Table',
  'common.ron': 'RON',

  'session.invalidTitle': 'Invalid QR code',
  'session.missingMessage': 'Scan the QR code on your table to open the menu.',
  'session.invalidMessage': 'This QR code is no longer valid. Please ask the staff for help.',

  'theme.toLight': 'Switch to light theme',
  'theme.toDark': 'Switch to dark theme',

  'welcome.line1': 'Welcome to',
  'welcome.fallbackLocation': 'Restaurant',
  'welcome.today': 'TODAY',
  'welcome.featuredName': 'Oltenia beef tenderloin',
  'welcome.featuredMeta': 'Aged 28 days · 128 RON',

  'pebbles.title1': 'What would you like',
  'pebbles.title2': 'to eat?',
  'pebbles.subtitle': 'Tap a category to see the menu',
  'pebbles.loading': 'Loading menu...',
  'pebbles.dailyRecommendation': "Chef's pick of the day",
  'pebbles.dishesCount': '{count} dishes',

  'menu.fallbackTitle': 'Menu',
  'menu.dishesCount': '{count} dishes',
  'menu.closeSearch': 'Close search',
  'menu.openSearch': 'Search',
  'menu.searchPlaceholder': 'Search in {category}...',
  'menu.clearSearch': 'Clear search',
  'menu.filter.all': 'All',
  'menu.filter.meat': 'Meat',
  'menu.filter.fish': 'Fish',
  'menu.filter.veggie': 'Vegetarian',
  'menu.filter.chef': 'Chef',
  'menu.noResultsFor': 'No results for „{query}".',
  'menu.noResults': 'No dishes in this filter.',
  'menu.loading': 'Loading...',
  'menu.add': 'Add',
  'menu.outOfStock': 'Sold out',
  'menu.recommendedForYou': 'Recommended for you',
  'menu.addRecommendation': 'Add recommendation',
  'menu.cartLabel': 'My cart',
  'menu.cartCountOne': '{count} dish',
  'menu.cartCountMany': '{count} dishes',
  'menu.cartGo': 'Cart →',

  'cart.title': 'My order',
  'cart.selectedOne': '{count} item selected',
  'cart.selectedMany': '{count} items selected',
  'cart.prepEstimate': ' · preparation approximately {minutes} minutes',
  'cart.empty': 'Your cart is empty. Add dishes from the menu.',
  'cart.aiSuggested': 'AI suggested',
  'cart.decreaseQty': 'Decrease quantity',
  'cart.increaseQty': 'Increase quantity',
  'cart.subtotal': 'Subtotal',
  'cart.service': 'Service',
  'cart.serviceIncluded': 'included',
  'cart.total': 'Total',
  'cart.errorInvalidSession': 'Session is invalid.',
  'cart.errorEmpty': 'Cart is empty.',
  'cart.errorSubmit': 'Error sending order.',
  'cart.submitting': 'Sending...',
  'cart.submit': 'Send to kitchen',

  'sent.confirmed': 'Confirmed ✓',
  'sent.title1': 'Your order',
  'sent.title2': 'is on its way',
  'sent.title3': 'to the kitchen!',
  'sent.lede': 'Chef Andrei received your order. Follow the status below.',
  'sent.stepSubmitted': 'Sent to kitchen',
  'sent.stepPreparing': 'Preparing now',
  'sent.stepServed': 'Served at the table',
  'sent.now': 'NOW',
  'sent.totalLabel': 'Order total',
  'sent.payHint': 'Pay at the end · cash or card',
  'sent.callWaiter': 'Call the waiter',
  'sent.orderAgain': 'Order again',

  'flash.badge': 'Special offer',
  'flash.dismiss': 'Close offer',
  'flash.title1': "Chef's",
  'flash.titleMark': 'limited',
  'flash.title3': 'offer.',
  'flash.defaultReason': 'A special portion at a reduced price, only a few minutes.',
  'flash.discount': '−{percent}% off',
  'flash.meta': "Chef's cut · limited portion",
  'flash.timeLeft': 'Time left',
  'flash.minutes': 'minutes',
  'flash.added': 'Added ✓',
  'flash.adding': 'Adding...',
  'flash.addToOrder': 'Add to order',
  'flash.noThanks': 'No thanks',
  'flash.errorInvalidSession': 'Invalid session. Reload the page.',
  'flash.errorClaim': 'Error claiming offer.',
}

const dict: Record<Locale, Dict> = { ro, en }

function readInitial(): Locale {
  if (typeof localStorage === 'undefined') return 'ro'
  const stored = localStorage.getItem(STORAGE_KEY)
  if (stored === 'ro' || stored === 'en') return stored
  return 'ro'
}

const locale = ref<Locale>(readInitial())

watch(locale, (v) => {
  try {
    localStorage.setItem(STORAGE_KEY, v)
  } catch {
    // ignore — private mode / disabled storage
  }
})

function setLocale(l: Locale) {
  locale.value = l
}

function format(s: string, params?: Record<string, string | number>): string {
  if (!params) return s
  return s.replace(/\{(\w+)\}/g, (_, k) => {
    const v = params[k]
    return v === undefined || v === null ? `{${k}}` : String(v)
  })
}

export function useLocale() {
  const t = (key: string, params?: Record<string, string | number>): string => {
    const table = dict[locale.value] ?? dict.ro
    const str = table[key] ?? dict.ro[key] ?? key
    return format(str, params)
  }
  return { locale, setLocale, t }
}
