<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'
import { useMenu } from '@/composables/useMenu'
import { useSessionStore } from '@/stores/useSessionStore'
import PhoneScreen from '@/components/ui/PhoneScreen.vue'
import Pebble from '@/components/ui/Pebble.vue'
import FoodTile from '@/components/ui/FoodTile.vue'

const emit = defineEmits<{
  (e: 'open-category', id: string): void
  (e: 'back'): void
}>()

const { categories, isLoading, error, load, productsByCategory } = useMenu()
const sessionStore = useSessionStore()
const { tokens: T, theme } = useTheme()
const { t } = useLocale()

const active = ref<string | null>(null)
const warp = ref<{ x: number; y: number } | null>(null)
let navigateTimer: ReturnType<typeof setTimeout> | null = null

onMounted(() => {
  load().catch(() => {
    /* surfaced via the `error` ref */
  })
})

const tableLabel = computed(() => {
  const id = sessionStore.tableId
  if (!id) return t('common.table')
  const tail = id.slice(-2).replace(/^0+/, '') || id.slice(-2)
  return `${t('common.table')} ${tail}`
})

// Pick an Unsplash photo for each category by keyword. Falls back to a
// generic plate photo when no keyword matches.
const KEYWORD_IMAGES: Array<[RegExp, string]> = [
  [/sup|ciorb|borș|bors/i,     'https://images.unsplash.com/photo-1547592180-85f173990554?w=500&q=80'],
  [/salat|salad/i,             'https://images.unsplash.com/photo-1512621776951-a57141f2eefd?w=500&q=80'],
  [/desert|dulce|prajitur|prăjitur|tort|cake/i,
                               'https://images.unsplash.com/photo-1488477181946-6428a0291777?w=500&q=80'],
  [/vin|wine/i,                'https://images.unsplash.com/photo-1510812431401-41d2bd2722f3?w=500&q=80'],
  [/bere|beer/i,               'https://images.unsplash.com/photo-1535958636474-b021ee887b13?w=500&q=80'],
  [/băutur|bautur|cocktail|drink|suc|limonad/i,
                               'https://images.unsplash.com/photo-1551024709-8f23befc6f87?w=500&q=80'],
  [/cafe|cafea|coffee|ceai|tea/i,
                               'https://images.unsplash.com/photo-1495474472287-4d71bcdd2085?w=500&q=80'],
  [/pizza/i,                   'https://images.unsplash.com/photo-1513104890138-7c749659a591?w=500&q=80'],
  [/paste|pasta/i,             'https://images.unsplash.com/photo-1551183053-bf91a1d81141?w=500&q=80'],
  [/peste|pește|fish|somon|salmon|fruct.?de.?mare/i,
                               'https://images.unsplash.com/photo-1467003909585-2f8a72700288?w=500&q=80'],
  [/aperitiv|gustare|starter/i,'https://images.unsplash.com/photo-1551024601-bec78aea704b?w=500&q=80'],
  [/vegetar|vegan|legume/i,    'https://images.unsplash.com/photo-1540420773420-3366772f4999?w=500&q=80'],
  [/burger/i,                  'https://images.unsplash.com/photo-1568901346375-23c9450c58cd?w=500&q=80'],
  [/principal|main|carne|meat|vita|vită|porc|pui|chicken/i,
                               'https://images.unsplash.com/photo-1544025162-d76694265947?w=500&q=80'],
]

const FALLBACK_IMAGE = 'https://images.unsplash.com/photo-1414235077428-338989a2e8c0?w=500&q=80'

function imageFor(name: string): string {
  for (const [pattern, url] of KEYWORD_IMAGES) {
    if (pattern.test(name)) return url
  }
  return FALLBACK_IMAGE
}

const pebbles = computed(() =>
  categories.value.map((cat, i) => ({
    id: cat.id,
    label: cat.name,
    count: productsByCategory(cat.id).length,
    pathIndex: i % 6,
    image: imageFor(cat.name),
  })),
)

type PebbleVm = (typeof pebbles)['value'][number]

// Group pebbles in pairs so we render the design's stacked accent panels.
const pebbleGroups = computed<PebbleVm[][]>(() => {
  const groups: PebbleVm[][] = []
  for (let i = 0; i < pebbles.value.length; i += 2) {
    groups.push(pebbles.value.slice(i, i + 2))
  }
  return groups
})

const panelStyle = computed(() => {
  // Slightly translucent accent wash matching the handoff: rgba(accent, 0.13/0.16) bg,
  // rgba(accent, 0.22/0.28) border. Tinted per theme.
  const rgb = theme.value === 'dark' ? '232,37,90' : '255,74,28'
  const bgAlpha = theme.value === 'dark' ? 0.16 : 0.13
  const borderAlpha = theme.value === 'dark' ? 0.28 : 0.22
  return {
    background: `rgba(${rgb},${bgAlpha})`,
    border: `1px solid rgba(${rgb},${borderAlpha})`,
  }
})

// First main category is the "Recomandarea zilei" anchor; sticky bar links to it.
const dailyTarget = computed(() => pebbles.value[0] ?? null)

function handlePebbleClick(id: string, ev: MouseEvent) {
  if (navigateTimer) return
  active.value = id

  // Capture the pebble's center for the warp clip-path origin.
  const target = ev.currentTarget as HTMLElement | null
  if (target) {
    const rect = target.getBoundingClientRect()
    const phone = target.closest('.phone-screen') as HTMLElement | null
    const phoneRect = phone?.getBoundingClientRect()
    if (phoneRect) {
      warp.value = {
        x: rect.left + rect.width / 2 - phoneRect.left,
        y: rect.top + rect.height / 2 - phoneRect.top,
      }
    } else {
      warp.value = { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 }
    }
  }

  navigateTimer = setTimeout(() => {
    emit('open-category', id)
  }, 360)
}

function openDaily() {
  if (!dailyTarget.value) return
  // Reuse the warp by faking an event from the sticky bar centre.
  const phone = document.querySelector('.phone-screen') as HTMLElement | null
  const phoneRect = phone?.getBoundingClientRect()
  if (phoneRect) {
    warp.value = { x: phoneRect.width / 2, y: phoneRect.height - 100 }
  }
  active.value = dailyTarget.value.id
  if (navigateTimer) clearTimeout(navigateTimer)
  navigateTimer = setTimeout(() => {
    emit('open-category', dailyTarget.value!.id)
  }, 360)
}

onBeforeUnmount(() => {
  if (navigateTimer) clearTimeout(navigateTimer)
})
</script>

<template>
  <PhoneScreen :theme="theme">
    <div class="header">
      <button
        class="circle-btn"
        type="button"
        :style="{ background: T.card, border: `1px solid ${T.ink12}`, color: T.ink }"
        :aria-label="t('common.back')"
        @click="emit('back')"
      >←</button>
      <div class="status-table" :style="{ color: T.ink70 }">{{ tableLabel }}</div>
    </div>

    <div class="heading">
      <h2 class="title">
        {{ t('pebbles.title1') }} <span :style="{ color: T.accent }">{{ t('pebbles.title2') }}</span>
      </h2>
      <p class="lede" :style="{ color: T.ink70 }">
        {{ t('pebbles.subtitle') }}
      </p>
    </div>

    <div v-if="isLoading" class="state" :style="{ color: T.ink70 }">
      {{ t('pebbles.loading') }}
    </div>
    <div v-else-if="error" class="state" :style="{ color: T.accent }">
      {{ error }}
    </div>

    <div v-else class="grid-wrap">
      <!-- Background blooms (behind cards, never over interactive elements) -->
      <div
        class="bloom bloom-lime"
        :style="{ background: `radial-gradient(circle, ${T.lime} 0%, transparent 70%)` }"
      />
      <div
        class="bloom bloom-accent"
        :style="{ background: `radial-gradient(circle, ${T.accent} 0%, transparent 70%)` }"
      />

      <div class="groups">
        <div
          v-for="(group, gIdx) in pebbleGroups"
          :key="gIdx"
          class="panel"
          :style="panelStyle"
        >
          <div class="grid">
            <Pebble
              v-for="pb in group"
              :key="pb.id"
              :label="pb.label"
              :sublabel="t('pebbles.dishesCount', { count: pb.count })"
              :path-index="pb.pathIndex"
              :image="pb.image"
              :active="pb.id === active"
              :theme="theme"
              @click="(ev) => handlePebbleClick(pb.id, ev)"
            />
          </div>
        </div>
      </div>
    </div>

    <!-- Sticky "Recomandarea zilei" bar — fades in on the bottom edge. -->
    <div
      v-if="dailyTarget"
      class="sticky-wrap"
      :style="{
        background: `linear-gradient(to bottom, transparent, ${T.paper} 28%)`,
      }"
    >
      <button
        class="daily-card"
        type="button"
        :style="{ background: T.ink, color: T.paperHi }"
        @click="openDaily"
      >
        <FoodTile
          tone="terra"
          :label="t('welcome.today')"
          :radius="12"
          class="daily-tile"
        />
        <div class="daily-body">
          <div class="daily-eyebrow" :style="{ color: T.lime }">
            {{ t('pebbles.dailyRecommendation') }}
          </div>
          <div class="daily-name">{{ t('welcome.featuredName') }}</div>
          <div
            class="daily-meta"
            :style="{ color: theme === 'dark' ? T.ink70 : 'rgba(251,247,236,0.55)' }"
          >{{ t('welcome.featuredMeta') }}</div>
        </div>
        <span
          class="daily-arrow"
          :style="{ background: T.accent, color: T.onAccent }"
          aria-hidden="true"
        >→</span>
      </button>
    </div>

    <!-- Warp transition overlay: lime circle expanding from the tapped pebble. -->
    <div
      v-if="warp"
      class="warp"
      :style="{
        background: T.lime,
        clipPath: `circle(150% at ${warp.x}px ${warp.y}px)`,
        '--warp-x': `${warp.x}px`,
        '--warp-y': `${warp.y}px`,
      } as Record<string, string>"
    />
  </PhoneScreen>
</template>

<style scoped>
.header {
  position: relative;
  padding: 12px 20px 0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-shrink: 0;
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
}
.status-table {
  font-size: 16px;
  font-weight: 600;
}
.heading {
  position: relative;
  padding: 22px 24px 6px;
}
.title {
  font-weight: 600;
  font-size: 36px;
  line-height: 1.05;
  letter-spacing: -0.03em;
  margin: 0;
}
.lede {
  font-size: 16px;
  margin: 8px 0 0;
}
.state {
  padding: 32px 24px;
  font-size: 18px;
  text-align: center;
}
.grid-wrap {
  position: relative;
  flex: 1;
  overflow: hidden auto;
  padding: 12px 16px 110px;
}
.bloom {
  position: absolute;
  border-radius: 50%;
  pointer-events: none;
  z-index: 0;
}
.bloom-lime {
  top: 10%;
  left: -20%;
  width: 260px;
  height: 260px;
  filter: blur(60px);
  opacity: 0.32;
}
.bloom-accent {
  bottom: 20%;
  right: -15%;
  width: 220px;
  height: 220px;
  filter: blur(55px);
  opacity: 0.28;
}
.groups {
  position: relative;
  z-index: 1;
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.panel {
  border-radius: 24px;
  padding: 18px 14px 22px;
}
.grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0 10px;
  align-items: start;
}

/* Sticky bottom bar */
.sticky-wrap {
  position: absolute;
  bottom: 0;
  left: 0;
  right: 0;
  padding: 10px 16px 28px;
  z-index: 20;
  pointer-events: none;
}
.daily-card {
  width: 100%;
  pointer-events: auto;
  border: none;
  border-radius: 22px;
  padding: 14px 16px;
  display: flex;
  gap: 14px;
  align-items: center;
  cursor: pointer;
  text-align: left;
  font-family: inherit;
  box-shadow: 0 16px 40px -12px rgba(21, 20, 15, 0.5);
}
.daily-tile {
  width: 58px;
  height: 68px;
  flex-shrink: 0;
}
.daily-body {
  flex: 1;
  min-width: 0;
}
.daily-eyebrow {
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  margin-bottom: 4px;
}
.daily-name {
  font-size: 16px;
  font-weight: 700;
  line-height: 1.2;
  margin-bottom: 2px;
}
.daily-meta {
  font-size: 13px;
  font-weight: 500;
}
.daily-arrow {
  width: 38px;
  height: 38px;
  border-radius: 50%;
  flex-shrink: 0;
  display: grid;
  place-items: center;
  font-size: 18px;
  font-weight: 700;
}

/* Warp transition: circle clip expanding from the tapped pebble centre. */
.warp {
  position: absolute;
  inset: 0;
  z-index: 60;
  pointer-events: none;
  animation: warp-expand 360ms cubic-bezier(0.4, 0, 0.2, 1) forwards;
}
@keyframes warp-expand {
  0% {
    clip-path: circle(0px at var(--warp-x) var(--warp-y));
    opacity: 1;
  }
  60% {
    opacity: 1;
  }
  100% {
    clip-path: circle(150% at var(--warp-x) var(--warp-y));
    opacity: 1;
  }
}
</style>
