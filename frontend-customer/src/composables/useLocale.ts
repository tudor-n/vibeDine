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

  'splash.loading': 'Se încarcă meniul',

  'session.invalidTitle': 'Cod QR invalid',
  'session.missingMessage': 'Scanează codul QR de pe masă pentru a deschide meniul.',
  'session.invalidMessage': 'Acest cod QR nu mai este valid. Întreabă personalul pentru ajutor.',

  'theme.toLight': 'Comutați la tema deschisă',
  'theme.toDark': 'Comutați la tema închisă',

  'welcome.line1': 'Bun venit la',
  'welcome.cta': 'Vezi meniul',
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
  'menu.noResultsFor': 'Niciun rezultat pentru „{query}".',
  'menu.noResults': 'Niciun preparat în această categorie.',
  'menu.loading': 'Se încarcă...',
  'menu.add': 'Adaugă',
  'menu.outOfStock': 'Epuizat',
  'menu.unavailableNow': 'Indisponibil momentan',
  'menu.unit.piece': 'buc',
  'menu.unit.gram': 'g',
  'menu.unit.kilogram': 'kg',
  'menu.unit.milliliter': 'ml',
  'menu.unit.liter': 'l',
  'menu.cartLabel': 'Coșul meu',
  'menu.cartCountOne': '{count} preparat',
  'menu.cartCountMany': '{count} preparate',
  'menu.cartGo': 'Coș →',

  'hh.badge': 'HAPPY HOUR',
  'hh.add': 'Adaugă',
  'hh.added': 'Adăugat ✓',
  'hh.addAria': 'Adaugă oferta happy hour în coș',
  'hh.popupSubtitle': 'Preț special, doar în acest interval.',
  'hh.endsIn': 'Se termină în',
  'hh.addFor': 'Adaugă pentru',
  'hh.maybeLater': 'Poate mai târziu',
  'hh.dismissAria': 'Închide oferta happy hour',
  'hh.toastAdded': '{product} adăugat la preț de happy hour',
  'hh.reopenTitle': 'Oferta ta Happy Hour',
  'hh.reopenMeta': '−{percent}% · până la {time} · atinge pentru detalii',
  'hh.reopenAria': 'Redeschide oferta happy hour',

  'cart.title': 'Comanda mea',
  'cart.selectedOne': '{count} preparat selectat',
  'cart.selectedMany': '{count} preparate selectate',
  'cart.prepEstimate': ' · pregătire aproximativ {minutes} de minute',
  'cart.empty': 'Coșul este gol. Adăugați preparate din meniu.',
  'cart.aiSuggested': 'Sugerat de Vibey',
  'vibey.subtitle': 'Asistentul tău de la masă',
  'vibey.thinking': 'analizează coșul…',
  'vibey.revealShow': 'Da, arată-mi',
  'vibey.revealHide': 'Ascunde ideile',
  'vibey.allAdded': 'Toate ideile lui Vibey sunt în coș',
  'vibey.sheetTitle': 'Ideile lui Vibey',
  'vibey.sheetSubtitle': 'Alese pentru ce ai în coș',
  'vibey.add': '+ Adaugă',
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
  'sent.viewProgress': 'Vezi statusul comenzii',
  'sent.cancelledTitle': 'Comanda a fost anulată',
  'sent.cancelledBody': 'Restaurantul a anulat comanda ta. Te rugăm să întrebi personalul pentru detalii.',

  'flash.badge': 'Ofertă specială',
  'flash.dismiss': 'Închide oferta',
  'flash.title1': 'Ofertă',
  'flash.titleMark': 'limitată',
  'flash.title3': 'de chef.',
  'flash.defaultReason': 'O porție specială la preț redus, doar câteva minute.',
  'flash.discount': '−{percent}% reducere',
  'flash.meta': "Chef's cut · porție limitată",
  'flash.forGuests': 'Pentru {count} persoane · câte o porție de fiecare',
  'flash.timeLeft': 'Timp rămas',
  'flash.minutes': 'minute',
  'flash.added': 'Adăugat ✓',
  'flash.adding': 'Se adaugă...',
  'flash.addToOrder': 'Adaugă în comandă',
  'flash.noThanks': 'Nu mulțumesc',
  'flash.errorInvalidSession': 'Sesiune invalidă. Reîncărcați pagina.',
  'flash.errorClaim': 'Eroare la revendicare.',
  'flash.iconLabel': 'Vezi oferta specială',

  'waiter.iconLabel': 'Chemați chelnerul',
  'waiter.calling': 'Se trimite…',
  'waiter.onTheWay': 'Chelnerul vine spre dvs.',
  'waiter.cooldownHint': 'Mai puteți chema în',
  'waiter.errorGeneric': 'Nu s-a putut trimite. Încercați din nou.',

  'party.title': 'La masa ta',
  'party.you': 'Tu',
  'party.justYou': 'Deocamdată ești singur la masă.',
  'party.items': '{count} produse',
  'party.itemOne': '1 produs',
  'party.empty': 'coș gol',
  'party.statusMerged': 'notă unită',
  'party.statusSubmitted': 'comandă trimisă',
  'party.merge': 'Unește notele',
  'party.merging': 'Se unește…',
  'party.mergeHint': 'Adună toate coșurile într-o singură notă.',
  'party.joinedToast': '{name} s-a alăturat mesei tale',
  'party.joinedToastHint': 'La final puteți uni comenzile într-o singură notă.',

  'merged.title': 'Notele au fost unite',
  'merged.body': 'Produsele tale sunt acum în comanda lui {name}. {name} se ocupă de notă pentru toată masa.',
  'merged.newOrder': 'Comandă separată nouă',
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

  'splash.loading': 'Loading the menu',

  'session.invalidTitle': 'Invalid QR code',
  'session.missingMessage': 'Scan the QR code on your table to open the menu.',
  'session.invalidMessage': 'This QR code is no longer valid. Please ask the staff for help.',

  'theme.toLight': 'Switch to light theme',
  'theme.toDark': 'Switch to dark theme',

  'welcome.line1': 'Welcome to',
  'welcome.cta': 'View the menu',
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
  'menu.noResultsFor': 'No results for „{query}".',
  'menu.noResults': 'No dishes in this category.',
  'menu.loading': 'Loading...',
  'menu.add': 'Add',
  'menu.outOfStock': 'Sold out',
  'menu.unavailableNow': 'Unavailable right now',
  'menu.unit.piece': 'pcs',
  'menu.unit.gram': 'g',
  'menu.unit.kilogram': 'kg',
  'menu.unit.milliliter': 'ml',
  'menu.unit.liter': 'l',
  'menu.cartLabel': 'My cart',
  'menu.cartCountOne': '{count} dish',
  'menu.cartCountMany': '{count} dishes',
  'menu.cartGo': 'Cart →',

  'hh.badge': 'HAPPY HOUR',
  'hh.add': 'Add',
  'hh.added': 'Added ✓',
  'hh.addAria': 'Add the happy hour deal to cart',
  'hh.popupSubtitle': 'Special price, only during this window.',
  'hh.endsIn': 'Ends in',
  'hh.addFor': 'Add for',
  'hh.maybeLater': 'Maybe later',
  'hh.dismissAria': 'Dismiss the happy hour offer',
  'hh.toastAdded': '{product} added at happy-hour price',
  'hh.reopenTitle': 'Your Happy Hour offer',
  'hh.reopenMeta': '−{percent}% · ends {time} · tap to view',
  'hh.reopenAria': 'Reopen the happy hour offer',

  'cart.title': 'My order',
  'cart.selectedOne': '{count} item selected',
  'cart.selectedMany': '{count} items selected',
  'cart.prepEstimate': ' · preparation approximately {minutes} minutes',
  'cart.empty': 'Your cart is empty. Add dishes from the menu.',
  'cart.aiSuggested': 'Suggested by Vibey',
  'vibey.subtitle': 'Your assistant at the table',
  'vibey.thinking': 'reading your cart…',
  'vibey.revealShow': 'Show me',
  'vibey.revealHide': 'Hide ideas',
  'vibey.allAdded': "All of Vibey's ideas are in your cart",
  'vibey.sheetTitle': "Vibey's ideas",
  'vibey.sheetSubtitle': "Chosen for what's in your cart",
  'vibey.add': '+ Add',
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
  'sent.viewProgress': 'View order status',
  'sent.cancelledTitle': 'Order cancelled',
  'sent.cancelledBody': 'The restaurant cancelled your order. Please ask the staff for details.',

  'flash.badge': 'Special offer',
  'flash.dismiss': 'Close offer',
  'flash.title1': "Chef's",
  'flash.titleMark': 'limited',
  'flash.title3': 'offer.',
  'flash.defaultReason': 'A special portion at a reduced price, only a few minutes.',
  'flash.discount': '−{percent}% off',
  'flash.meta': "Chef's cut · limited portion",
  'flash.forGuests': 'For {count} guests · one portion each',
  'flash.timeLeft': 'Time left',
  'flash.minutes': 'minutes',
  'flash.added': 'Added ✓',
  'flash.adding': 'Adding...',
  'flash.addToOrder': 'Add to order',
  'flash.noThanks': 'No thanks',
  'flash.errorInvalidSession': 'Invalid session. Reload the page.',
  'flash.errorClaim': 'Error claiming offer.',
  'flash.iconLabel': 'View special offer',

  'waiter.iconLabel': 'Call the waiter',
  'waiter.calling': 'Calling…',
  'waiter.onTheWay': 'A waiter is on the way',
  'waiter.cooldownHint': 'You can call again in',
  'waiter.errorGeneric': 'Could not send. Try again.',

  'party.title': 'At your table',
  'party.you': 'You',
  'party.justYou': "It's just you at the table so far.",
  'party.items': '{count} items',
  'party.itemOne': '1 item',
  'party.empty': 'empty cart',
  'party.statusMerged': 'merged',
  'party.statusSubmitted': 'order sent',
  'party.merge': 'Merge our bills',
  'party.merging': 'Merging…',
  'party.mergeHint': 'Pull everyone’s carts into one single bill.',
  'party.joinedToast': '{name} just joined your table',
  'party.joinedToastHint': 'You can merge your orders into one bill at the end.',

  'merged.title': 'Bills merged',
  'merged.body': "Your items are now in {name}'s order. {name} will handle the bill for the table.",
  'merged.newOrder': 'Start a separate order',
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
