<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/useAuthStore'

const auth = useAuthStore()
const route = useRoute()

interface Tile {
  name: string
  to: string
  label: string
  description: string
}

const tiles = computed<Tile[]>(() => {
  const base: Tile[] = [
    {
      name: 'settings-profile',
      to: '/settings/profile',
      label: 'Profil',
      description: 'Numele, emailul și avatarul contului tău.',
    },
  ]
  if (!auth.isWaiter) {
    base.push(
      {
        name: 'settings-organizations',
        to: '/settings/organizations',
        label: 'Organizații',
        description: 'Lanțurile la care ești membru și organizația activă.',
      },
      {
        name: 'settings-employees',
        to: '/settings/employees',
        label: 'Angajați',
        description: 'Toți colegii grupați pe restaurant. Aprobă ospătarii în așteptare.',
      },
      {
        name: 'settings-collaborators',
        to: '/settings/collaborators',
        label: 'Colaboratori',
        description: 'Locații și invitații pentru co-proprietari (RestaurantAdmin).',
      },
      {
        name: 'settings-brand',
        to: '/settings/brand',
        label: 'Brand',
        description: 'Personalizează culorile, logo-ul și fotografia',
      },
    )
  }
  return base
})

const activeName = computed(() => String(route.name ?? ''))
</script>

<template>
  <div class="max-w-5xl mx-auto">
    <div class="mb-6">
      <div class="eyebrow mb-4">Setări</div>
      <h1 class="display-title text-[44px] md:text-[56px]">Contul tău</h1>
    </div>

    <!-- Tile selector (org-picker styled) -->
    <div class="tile-grid mb-8">
      <RouterLink
        v-for="t in tiles"
        :key="t.name"
        :to="t.to"
        class="tile"
        :class="{ 'tile-active': activeName === t.name }"
      >
        <div class="flex items-start justify-between gap-3">
          <div class="min-w-0">
            <div class="tile-title">{{ t.label }}</div>
            <div class="tile-desc">{{ t.description }}</div>
          </div>
          <span class="tile-arrow" aria-hidden="true">→</span>
        </div>
      </RouterLink>
    </div>

    <RouterView />
  </div>
</template>

<style scoped>
.tile-grid {
  display: grid;
  gap: 16px;
  grid-template-columns: 1fr;
}
@media (min-width: 768px) {
  .tile-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
.tile {
  background: var(--card, var(--paperHi));
  border: 1px solid var(--ink12);
  border-radius: 18px;
  padding: 22px;
  text-decoration: none;
  color: var(--ink);
  cursor: pointer;
  box-shadow: var(--shadow-soft);
  transition: transform 0.18s ease, background 0.18s ease, border-color 0.18s ease,
    box-shadow 0.18s ease, color 0.18s ease;
}
.tile:hover {
  background: var(--ink06);
  border-color: var(--ink);
  transform: translateY(-2px);
}
.tile:focus-visible {
  outline: 2px solid var(--accent, currentColor);
  outline-offset: 2px;
}
.tile-title {
  font-size: 20px;
  font-weight: 700;
  color: var(--ink);
}
.tile-desc {
  margin-top: 6px;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--ink-70, var(--ink));
  opacity: 0.7;
}
.tile-arrow {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  color: var(--ink40);
}
.tile-active {
  background: var(--ink);
  color: var(--paperHi);
  border-color: var(--ink);
}
.tile-active .tile-title {
  color: var(--paperHi);
}
.tile-active .tile-desc,
.tile-active .tile-arrow {
  color: var(--lime, #b8e986);
}
.tile-active:hover {
  background: var(--ink);
  color: var(--paperHi);
}
</style>
