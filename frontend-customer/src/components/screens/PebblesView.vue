<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useTheme } from '@/composables/useTheme'
import { useMenu } from '@/composables/useMenu'
import { useSessionStore } from '@/stores/useSessionStore'
import PhoneScreen from '@/components/ui/PhoneScreen.vue'
import Pebble from '@/components/ui/Pebble.vue'

const emit = defineEmits<{
  (e: 'open-category', id: string): void
  (e: 'back'): void
}>()

const { categories, isLoading, error, load, productsByCategory } = useMenu()
const sessionStore = useSessionStore()
const { tokens: T, theme } = useTheme()

const active = ref<string | null>(null)

onMounted(() => {
  load().catch(() => {
    /* surfaced via the `error` ref */
  })
})

const tableLabel = computed(() => {
  const id = sessionStore.tableId
  if (!id) return 'Masa'
  const tail = id.slice(-2).replace(/^0+/, '') || id.slice(-2)
  return `Masa ${tail}`
})

// Pick an Unsplash photo for each category by keyword. Falls back to a
// generic plate photo when no keyword matches.
const KEYWORD_IMAGES: Array<[RegExp, string]> = [
  [/sup|ciorb|borș|bors/i,    'https://images.unsplash.com/photo-1547592180-85f173990554?w=500&q=80'],
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

type Pebble = (typeof pebbles)['value'][number]

const pebbleGroups = computed<Pebble[][]>(() => {
  const groups: Pebble[][] = []
  for (let i = 0; i < pebbles.value.length; i += 2) {
    groups.push(pebbles.value.slice(i, i + 2))
  }
  return groups
})

const panelStyle = computed(() => {
  const rgb = theme.value === 'dark' ? '232,37,90' : '255,74,28'
  const bgAlpha = theme.value === 'dark' ? 0.16 : 0.13
  const borderAlpha = theme.value === 'dark' ? 0.28 : 0.22
  return {
    background: `rgba(${rgb},${bgAlpha})`,
    border: `1px solid rgba(${rgb},${borderAlpha})`,
  }
})

let navigateTimer: ReturnType<typeof setTimeout> | null = null

function handlePebbleClick(id: string) {
  if (navigateTimer) return
  active.value = id
  navigateTimer = setTimeout(() => {
    emit('open-category', id)
  }, 320)
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
        :style="{ background: T.card, border: `1px solid ${T.ink12}`, color: T.ink }"
        aria-label="Înapoi"
        @click="emit('back')"
      >←</button>
      <div class="status-table" :style="{ color: T.ink70 }">{{ tableLabel }}</div>
    </div>

    <div class="heading">
      <h2 class="title">
        Ce doriți <span :style="{ color: T.accent }">să mâncați?</span>
      </h2>
      <p class="lede" :style="{ color: T.ink70 }">
        Atingeți o categorie pentru a vedea meniul
      </p>
    </div>

    <div v-if="isLoading" class="state" :style="{ color: T.ink70 }">
      Se încarcă meniul...
    </div>
    <div v-else-if="error" class="state" :style="{ color: T.accent }">
      {{ error }}
    </div>

    <div v-else class="grid-wrap">
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
              :sublabel="`${pb.count} preparate`"
              :path-index="pb.pathIndex"
              :image="pb.image"
              :active="pb.id === active"
              :theme="theme"
              @click="handlePebbleClick(pb.id)"
            />
          </div>
        </div>
      </div>
    </div>
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
  padding: 12px 16px 32px;
}
.groups {
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
</style>
