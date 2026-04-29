<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { toneFromKey } from '@/design/tokens'
import { useTheme } from '@/composables/useTheme'
import { useMenu } from '@/composables/useMenu'
import { useCartStore } from '@/stores/useCartStore'
import { useRecommendations } from '@/composables/useRecommendations'
import type { Product, Recommendation } from '@shared/types/models'
import PhoneScreen from '@/components/ui/PhoneScreen.vue'
import FoodTile from '@/components/ui/FoodTile.vue'

const props = defineProps<{ categoryId: string }>()
const emit = defineEmits<{
  (e: 'back'): void
  (e: 'open-cart'): void
}>()

const { categories, isLoading, error, load, productsByCategory } = useMenu()
const cartStore = useCartStore()
const { pairings, upsells } = useRecommendations()
const { tokens: T, theme } = useTheme()

const filter = ref<'all'>('all')

onMounted(() => {
  load().catch(() => undefined)
})

const category = computed(() =>
  categories.value.find((c) => c.id === props.categoryId),
)

const items = computed<Product[]>(() => productsByCategory(props.categoryId))

const cartCount = computed(() =>
  cartStore.items.reduce((sum, i) => sum + i.quantity, 0),
)

const recommendations = computed<Recommendation[]>(() => [
  ...pairings.value,
  ...upsells.value,
])

function addRecommendation(rec: Recommendation) {
  const product: Product = {
    id: rec.menuItem.id,
    categoryId: '',
    name: rec.menuItem.name,
    description: rec.menuItem.description ?? '',
    price: rec.menuItem.price,
    imageUrl: rec.menuItem.imageUrl ?? null,
    isAvailable: true,
  }
  cartStore.addRecommendedItem(product)
}
</script>

<template>
  <PhoneScreen :theme="theme">
    <div class="header">
      <button
        class="circle-btn"
        :style="{ background: T.card, border: `1px solid ${T.ink12}`, color: T.ink }"
        aria-label="Înapoi"
        @click="emit('back')"
      >←</button>
      <div class="title">
        <div class="title-name">{{ category?.name ?? 'Meniu' }}</div>
        <div class="title-meta" :style="{ color: T.ink70 }">{{ items.length }} preparate</div>
      </div>
      <button
        class="circle-btn"
        :style="{ background: T.card, border: `1px solid ${T.ink12}`, color: T.ink70 }"
        aria-label="Caută"
      >⌕</button>
    </div>

    <div class="scroll">
    <div class="chips">
      <span
        class="chip chip-active"
        :style="{ background: T.ink, color: T.paperHi }"
      >Toate</span>
    </div>

    <div v-if="isLoading" class="state" :style="{ color: T.ink70 }">Se încarcă...</div>
    <div v-else-if="error" class="state" :style="{ color: T.accent }">{{ error }}</div>

    <div v-else class="dishes">
      <article
        v-for="m in items"
        :key="m.id"
        class="dish"
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
            <div class="dish-desc" :style="{ color: T.ink70 }">{{ m.description }}</div>
          </div>
        </div>
        <div class="dish-foot" :style="{ borderTop: `1px solid ${T.ink06}` }">
          <div class="price">
            {{ m.price }}
            <span class="price-unit" :style="{ color: T.ink70 }">RON</span>
          </div>
          <button
            class="add-btn"
            :style="{ background: T.accent, color: T.onAccent }"
            :disabled="!m.isAvailable"
            @click="cartStore.addItem(m)"
          >
            <span class="plus">+</span> {{ m.isAvailable ? 'Adaugă' : 'Epuizat' }}
          </button>
        </div>
      </article>
    </div>

    <div
      v-if="recommendations.length > 0"
      class="recs"
      :style="{ background: T.paperHi, borderTop: `1px solid ${T.ink12}` }"
    >
      <div class="recs-pill" :style="{ background: T.lime, color: T.ink }">
        Recomandate pentru dumneavoastră
      </div>
      <div class="recs-strip">
        <div
          v-for="r in recommendations"
          :key="r.id"
          class="rec-card"
          :style="{ background: T.card, border: `1px solid ${T.ink12}` }"
        >
          <FoodTile
            :tone="toneFromKey(r.menuItem.id)"
            :image="r.menuItem.imageUrl"
            :radius="14"
            class="rec-tile"
          />
          <div class="rec-name">{{ r.menuItem.name }}</div>
          <div class="rec-sub" :style="{ color: T.ink70 }">{{ r.reason }}</div>
          <div class="rec-row">
            <span class="rec-price">
              {{ r.menuItem.price }}
              <span class="rec-price-unit" :style="{ color: T.ink70 }">RON</span>
            </span>
            <button
              class="rec-add"
              :style="{ background: T.accent, color: T.onAccent }"
              aria-label="Adaugă recomandarea"
              @click="addRecommendation(r)"
            >+</button>
          </div>
        </div>
      </div>
    </div>
    </div>

    <div v-if="cartCount > 0" class="cart-bar" :style="{ background: T.ink, color: T.paperHi }">
      <div>
        <div
          class="cart-eyebrow"
          :style="{ color: theme === 'dark' ? 'rgba(34,31,26,0.6)' : 'rgba(251,247,236,0.55)' }"
        >Coșul meu</div>
        <div class="cart-summary">
          {{ cartStore.total }} RON · {{ cartCount }} {{ cartCount === 1 ? 'preparat' : 'preparate' }}
        </div>
      </div>
      <button
        class="cart-cta"
        :style="{ background: T.accent, color: T.onAccent }"
        @click="emit('open-cart')"
      >Coș →</button>
    </div>
  </PhoneScreen>
</template>

<style scoped>
.header {
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
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
  padding-bottom: 110px;
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
}
.state {
  padding: 24px;
  font-size: 18px;
  text-align: center;
}
.dishes {
  padding: 0 16px 18px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.dish {
  border-radius: 22px;
  padding: 14px;
  box-shadow: 0 2px 10px -6px rgba(21, 20, 15, 0.08);
}
.dish-row {
  display: grid;
  grid-template-columns: 100px 1fr;
  gap: 16px;
  margin-bottom: 12px;
}
.dish-tile {
  width: 100px;
  height: 100px;
}
.dish-body {
  padding: 2px 0;
}
.dish-name {
  font-size: 18px;
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
.dish-foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding-top: 10px;
}
.price {
  font-size: 24px;
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
  padding: 0 20px;
  border-radius: 999px;
  font-size: 16px;
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
.recs {
  padding: 16px 16px 18px;
}
.recs-pill {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  border-radius: 999px;
  padding: 8px 16px;
  font-size: 14px;
  font-weight: 700;
  margin-bottom: 12px;
}
.recs-strip {
  display: flex;
  gap: 12px;
  overflow-x: auto;
  padding-bottom: 4px;
  scrollbar-width: none;
}
.recs-strip::-webkit-scrollbar { display: none; }
.rec-card {
  min-width: 170px;
  border-radius: 20px;
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.rec-tile {
  height: 80px;
}
.rec-name {
  font-size: 16px;
  font-weight: 700;
  line-height: 1.2;
}
.rec-sub {
  font-size: 13px;
}
.rec-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 4px;
}
.rec-price {
  font-size: 18px;
  font-weight: 700;
}
.rec-price-unit {
  font-size: 12px;
  font-weight: 500;
}
.rec-add {
  border: none;
  border-radius: 999px;
  width: 40px;
  height: 40px;
  font-size: 22px;
  font-weight: 700;
  cursor: pointer;
  display: grid;
  place-items: center;
  font-family: inherit;
}
.cart-bar {
  position: absolute;
  bottom: 24px;
  left: 14px;
  right: 14px;
  padding: 14px 16px 14px 22px;
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
  font-size: 20px;
  font-weight: 700;
}
.cart-cta {
  border: none;
  border-radius: 999px;
  padding: 13px 20px;
  font-size: 15px;
  font-weight: 700;
  cursor: pointer;
  font-family: inherit;
}
</style>
