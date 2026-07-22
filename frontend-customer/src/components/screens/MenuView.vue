<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from 'vue'
import { toneFromKey } from '@/design/tokens'
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'
import { useMenu } from '@/composables/useMenu'
import { useCartStore } from '@/stores/useCartStore'
import { useHappyHourStore } from '@/stores/useHappyHourStore'
import { formatMoney } from '@/utils/money'
import type { Product, RecipeLine } from '@shared/types/models'
import PhoneScreen from '@/components/ui/PhoneScreen.vue'
import FoodTile from '@/components/ui/FoodTile.vue'
import HappyHourBanner from '@/components/HappyHourBanner.vue'
import MenuBackdrop from '@/components/ui/MenuBackdrop.vue'

const props = defineProps<{ categoryId: string }>()
const emit = defineEmits<{
  (e: 'back'): void
  (e: 'open-cart'): void
  (e: 'select-category', id: string): void
}>()

const { categories, isLoading, error, load, productsByCategory } = useMenu()
const cartStore = useCartStore()
const happyHour = useHappyHourStore()
const { tokens: T, mode } = useTheme()
const { t } = useLocale()

const searchOpen = ref(false)
const searchQuery = ref('')

function toggleSearch() {
  searchOpen.value = !searchOpen.value
  if (!searchOpen.value) searchQuery.value = ''
}

function normalize(s: string) {
  return s.toLowerCase().normalize('NFD').replace(/\p{Diacritic}/gu, '')
}

// Renders a recipe-tracked dish's ingredients as one compact line, e.g.
// "mozzarella 80 g · roșii 100 g · busuioc 5 g". Quantities use the Romanian number format.
const qtyFmt = new Intl.NumberFormat('ro-RO', { maximumFractionDigits: 3 })
function formatRecipe(recipe: RecipeLine[]): string {
  return recipe
    .map((r) => `${r.ingredientName} ${qtyFmt.format(r.quantityPerPortion)} ${t('menu.unit.' + r.unit)}`)
    .join(' · ')
}

// The chip row is a category switcher: one chip per menu category, in the same
// order as the pebble grid. Tapping one switches the menu in place (handled by
// the parent, which swaps `categoryId` and remounts this screen via its :key).
const categoryChips = computed(() =>
  categories.value.map((c) => ({ id: c.id, label: c.name })),
)

const chipsEl = ref<HTMLElement | null>(null)

function selectCategory(id: string) {
  if (id === props.categoryId) return
  emit('select-category', id)
}

// Bring the active category's chip into view so the diner can always see where
// they are. We adjust ONLY the chip row's own scrollLeft — never el.scrollIntoView(),
// which would also scroll every scrollable ancestor (the dish list, the phone
// frame, the page) and shift the whole screen.
function scrollActiveChipIntoView() {
  const row = chipsEl.value
  const active = row?.querySelector<HTMLElement>('[aria-pressed="true"]')
  if (!row || !active) return
  const rowRect = row.getBoundingClientRect()
  const activeRect = active.getBoundingClientRect()
  const delta = activeRect.left - rowRect.left - (row.clientWidth - active.clientWidth) / 2
  row.scrollLeft += delta // browser clamps to [0, maxScrollLeft]
}

onMounted(async () => {
  load().catch(() => undefined)
  happyHour.startPolling()
  await nextTick()
  scrollActiveChipIntoView()
})

const category = computed(() =>
  categories.value.find((c) => c.id === props.categoryId),
)

const allItems = computed<Product[]>(() => productsByCategory(props.categoryId))

const items = computed<Product[]>(() => {
  let result = allItems.value
  const q = normalize(searchQuery.value.trim())
  if (q) {
    result = result.filter((p) =>
      normalize(`${p.name} ${p.description ?? ''}`).includes(q),
    )
  }
  return result
})

const cartCount = computed(() =>
  cartStore.items.reduce((sum, i) => sum + i.quantity, 0),
)
</script>

<template>
  <PhoneScreen :theme="mode">
    <MenuBackdrop />
    <div
      class="bloom bloom-accent"
      :style="{ background: `radial-gradient(circle, ${T.accent} 0%, transparent 70%)` }"
    />
    <div
      class="bloom bloom-lime"
      :style="{ background: `radial-gradient(circle, ${T.lime} 0%, transparent 70%)` }"
    />

    <!-- Return-to-happy-hour banner — pinned at the very top of the screen once
         the diner has dismissed the offer sheet; tapping it reopens the offer. -->
    <HappyHourBanner />

    <div class="header">
      <button
        class="circle-btn"
        :style="{ background: T.card, border: `1px solid ${T.ink12}`, color: T.ink }"
        :aria-label="t('common.back')"
        @click="emit('back')"
      >←</button>
      <div class="title">
        <div class="title-name">{{ category?.name ?? t('menu.fallbackTitle') }}</div>
        <div class="title-meta" :style="{ color: T.ink70 }">{{ t('menu.dishesCount', { count: items.length }) }}</div>
      </div>
      <button
        class="circle-btn"
        type="button"
        :style="searchOpen
          ? { background: T.ink, border: 'none', color: T.paperHi }
          : { background: T.card, border: `1px solid ${T.ink12}`, color: T.ink70 }"
        :aria-label="searchOpen ? t('menu.closeSearch') : t('menu.openSearch')"
        :aria-pressed="searchOpen"
        @click="toggleSearch"
      >{{ searchOpen ? '×' : '⌕' }}</button>
    </div>

    <div v-if="searchOpen" class="search-row">
      <div
        class="search-box"
        :style="{ background: T.card, border: `1px solid ${T.ink12}` }"
      >
        <span class="search-icon" :style="{ color: T.ink70 }">⌕</span>
        <input
          v-model="searchQuery"
          class="search-input"
          type="search"
          :placeholder="t('menu.searchPlaceholder', { category: category?.name ?? t('menu.fallbackTitle').toLowerCase() })"
          :style="{ color: T.ink }"
          autofocus
        />
        <button
          v-if="searchQuery"
          type="button"
          class="search-clear"
          :style="{ color: T.ink70 }"
          :aria-label="t('menu.clearSearch')"
          @click="searchQuery = ''"
        >×</button>
      </div>
    </div>

    <div class="scroll">
    <div ref="chipsEl" class="chips">
      <button
        v-for="c in categoryChips"
        :key="c.id"
        class="chip"
        type="button"
        :style="c.id === props.categoryId
          ? { background: T.ink, color: T.paperHi, border: 'none' }
          : { background: T.card, color: T.ink, border: `1px solid ${T.ink12}` }"
        :aria-pressed="c.id === props.categoryId"
        @click="selectCategory(c.id)"
      >{{ c.label }}</button>
    </div>

    <div
      v-if="!isLoading && !error && items.length === 0"
      class="state"
      :style="{ color: T.ink70 }"
    >{{ searchQuery.trim() ? t('menu.noResultsFor', { query: searchQuery }) : t('menu.noResults') }}</div>

    <div v-if="isLoading" class="state" :style="{ color: T.ink70 }">{{ t('menu.loading') }}</div>
    <div v-else-if="error" class="state" :style="{ color: T.accent }">{{ error }}</div>

    <div v-else class="dishes">
      <article
        v-for="m in items"
        :key="m.id"
        class="dish"
        :class="{ 'dish-out': m.outOfStock }"
        :style="{ background: T.card, border: `1px solid ${T.ink12}` }"
      >
        <div class="dish-row">
          <FoodTile
            :tone="toneFromKey(m.id)"
            :image="m.imageUrl"
            :radius="16"
            class="dish-tile"
          />
          <div class="dish-body">
            <div class="dish-name">{{ m.name }}</div>
            <div
              v-if="m.outOfStock"
              class="oos-chip"
              :style="{ background: T.ink06, color: T.ink70, border: `1px solid ${T.ink12}` }"
            >{{ t('menu.unavailableNow') }}</div>
            <div class="dish-desc" :style="{ color: T.ink70 }">{{ m.description }}</div>
            <div
              v-if="m.recipe && m.recipe.length"
              class="dish-recipe"
              :style="{ color: T.ink40 }"
            >{{ formatRecipe(m.recipe) }}</div>
          </div>
        </div>
        <div class="dish-foot" :style="{ borderTop: `1px solid ${T.ink06}` }">
          <div class="price">
            <template v-if="happyHour.discountedPriceFor(m.id) != null">
              {{ formatMoney(happyHour.discountedPriceFor(m.id)!) }}
              <span :style="{ color: T.ink40, textDecoration: 'line-through', fontSize: '0.78em', marginLeft: '5px', fontWeight: 500 }">{{ formatMoney(m.price) }}</span>
            </template>
            <template v-else>{{ formatMoney(m.price) }}</template>
            <span class="price-unit" :style="{ color: T.ink70 }">{{ t('common.ron') }}</span>
          </div>
          <button
            class="add-btn"
            :style="{ background: T.accent, color: T.onAccent }"
            :disabled="!m.isAvailable"
            @click="cartStore.addItem(m)"
          >
            <span class="plus">+</span> {{ m.isAvailable ? t('menu.add') : t('menu.unavailableNow') }}
          </button>
        </div>
      </article>
    </div>
    </div>

    <div v-if="cartCount > 0" class="cart-bar" :style="{ background: T.ink, color: T.paperHi }">
      <div>
        <div
          class="cart-eyebrow"
          :style="{ color: mode === 'dark' ? 'rgba(34,31,26,0.6)' : 'rgba(251,247,236,0.55)' }"
        >{{ t('menu.cartLabel') }}</div>
        <div class="cart-summary">
          {{ cartStore.total }} {{ t('common.ron') }} ·
          {{ cartCount === 1 ? t('menu.cartCountOne', { count: cartCount }) : t('menu.cartCountMany', { count: cartCount }) }}
        </div>
      </div>
      <button
        class="cart-cta"
        type="button"
        :style="{ background: T.accent, color: T.onAccent }"
        @click="emit('open-cart')"
      >{{ t('menu.cartGo') }}</button>
    </div>
  </PhoneScreen>
</template>

<style scoped>
.bloom {
  position: absolute;
  border-radius: 50%;
  pointer-events: none;
  z-index: 0;
}
.bloom-accent {
  top: -40px;
  left: -60px;
  width: 260px;
  height: 260px;
  filter: blur(45px);
  opacity: 0.25;
}
.bloom-lime {
  bottom: 120px;
  right: -60px;
  width: 200px;
  height: 200px;
  filter: blur(40px);
  opacity: 0.20;
}
.header {
  position: relative;
  z-index: 1;
  padding: 10px 20px 14px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.circle-btn {
  width: 48px;
  height: 48px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-size: 20px;
  cursor: pointer;
  font-family: inherit;
  flex-shrink: 0;
}
.title {
  text-align: center;
  flex: 1;
  min-width: 0;
}
.title-name {
  font-size: 18px;
  font-weight: 700;
  letter-spacing: -0.01em;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.title-meta {
  font-size: 14px;
  margin-top: 2px;
}
.scroll {
  position: relative;
  z-index: 1;
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
  padding-bottom: calc(110px + var(--safe-bottom, 0px));
}
.search-row {
  position: relative;
  z-index: 1;
  padding: 0 20px 14px;
}
.search-box {
  display: flex;
  align-items: center;
  gap: 10px;
  height: 48px;
  border-radius: 999px;
  padding: 0 16px;
}
.search-icon {
  font-size: 18px;
  flex-shrink: 0;
}
.search-input {
  flex: 1;
  min-width: 0;
  background: transparent;
  border: none;
  outline: none;
  font-family: inherit;
  font-size: 16px;
  font-weight: 500;
  padding: 0;
}
.search-input::-webkit-search-cancel-button { display: none; }
.search-clear {
  border: none;
  background: transparent;
  font-size: 24px;
  line-height: 1;
  cursor: pointer;
  font-family: inherit;
  padding: 4px 6px;
  flex-shrink: 0;
}
.chips {
  display: flex;
  gap: 8px;
  padding: 0 20px 16px;
  overflow-x: auto;
  white-space: nowrap;
  scrollbar-width: none;
}
.chips::-webkit-scrollbar { display: none; }
.chip {
  font-size: 15px;
  font-weight: 600;
  padding: 10px 18px;
  border-radius: 999px;
  flex-shrink: 0;
  cursor: pointer;
  font-family: inherit;
  min-height: 44px;
  transition: background 0.18s ease, color 0.18s ease, border-color 0.18s ease;
}
.state {
  padding: 24px;
  font-size: 18px;
  text-align: center;
}
.dishes {
  padding: 0 clamp(12px, 4vw, 16px) 18px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.dish {
  border-radius: 22px;
  padding: clamp(12px, 3.5vw, 14px);
  box-shadow: 0 2px 10px -6px rgba(21, 20, 15, 0.08);
  --tile: clamp(76px, 24vw, 100px);
}
.dish-out {
  opacity: 0.55;
  filter: grayscale(0.45);
}
.oos-chip {
  display: inline-block;
  margin: 2px 0 6px;
  padding: 3px 10px;
  border-radius: 999px;
  font-size: 12px;
  font-weight: 600;
}
.dish-row {
  display: grid;
  grid-template-columns: var(--tile) 1fr;
  gap: clamp(12px, 4vw, 16px);
  margin-bottom: 12px;
}
.dish-tile {
  width: var(--tile);
  height: var(--tile);
}
.dish-body {
  padding: 2px 0;
}
.dish-name {
  font-size: clamp(16px, 4.6vw, 18px);
  font-weight: 700;
  letter-spacing: -0.01em;
  line-height: 1.2;
  margin-bottom: 5px;
}
.dish-desc {
  font-size: 14px;
  line-height: 1.5;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.dish-recipe {
  margin-top: 4px;
  font-size: 12px;
  line-height: 1.4;
}
.dish-foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding-top: 10px;
}
.price {
  font-size: clamp(20px, 6.1vw, 24px);
  font-weight: 700;
  letter-spacing: -0.02em;
}
.price-unit {
  font-size: 15px;
  font-weight: 600;
}
.add-btn {
  border: none;
  height: 48px;
  padding: 0 clamp(14px, 4.5vw, 20px);
  border-radius: 999px;
  font-size: clamp(14px, 4.1vw, 16px);
  font-weight: 700;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 8px;
  font-family: inherit;
}
.add-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.plus {
  font-size: 22px;
}
.cart-bar {
  position: absolute;
  bottom: calc(16px + var(--safe-bottom, 0px));
  left: clamp(12px, 3.6vw, 14px);
  right: clamp(12px, 3.6vw, 14px);
  padding: 14px 16px 14px clamp(16px, 5.6vw, 22px);
  border-radius: 999px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  box-shadow: 0 20px 40px -16px rgba(21, 20, 15, 0.4);
  z-index: 5;
}
.cart-eyebrow {
  font-size: 13px;
  font-weight: 500;
  margin-bottom: 2px;
}
.cart-summary {
  font-size: clamp(17px, 5.1vw, 20px);
  font-weight: 700;
}
.cart-cta {
  border: none;
  border-radius: 999px;
  padding: 13px clamp(15px, 5.1vw, 20px);
  font-size: clamp(14px, 3.8vw, 15px);
  font-weight: 700;
  cursor: pointer;
  font-family: inherit;
}
</style>
