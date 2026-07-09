<script setup lang="ts">
import { computed } from 'vue'
import { useTheme } from '@/composables/useTheme'
import { useLocale } from '@/composables/useLocale'
import { useGuestStore } from '@/stores/useGuestStore'
import type { PartyMember } from '@/services/api-client'

const emit = defineEmits<{ (e: 'merge'): void }>()

const guestStore = useGuestStore()
const { tokens: T } = useTheme()
const { t } = useLocale()

const members = computed(() => guestStore.partyMembers)
const showPanel = computed(() => members.value.length > 1)

function isYou(m: PartyMember) {
  return m.guestId === guestStore.guestId
}

function summary(m: PartyMember): string {
  if (m.status === 'merged') return t('party.statusMerged')
  if (m.status !== 'draft') return t('party.statusSubmitted')
  if (m.itemCount <= 0) return t('party.empty')
  const label = m.itemCount === 1 ? t('party.itemOne') : t('party.items', { count: m.itemCount })
  return `${label} · ${m.total} ${t('common.ron')}`
}
</script>

<template>
  <div v-if="showPanel" class="party" :style="{ background: T.card, border: `1px solid ${T.ink12}` }">
    <div class="party-head">
      <span class="party-title">{{ t('party.title') }}</span>
      <span class="party-count" :style="{ color: T.ink70 }">{{ members.length }}</span>
    </div>

    <ul class="party-list">
      <li v-for="m in members" :key="m.guestId" class="party-row">
        <span
          class="avatar"
          :style="{
            background: isYou(m) ? T.accent : T.lime,
            color: isYou(m) ? T.onAccent : T.onLime,
          }"
        >{{ (m.displayName || 'G').replace(/[^0-9]/g, '') || 'G' }}</span>
        <span class="party-name">
          {{ m.displayName }}
          <span v-if="isYou(m)" class="you-badge" :style="{ background: T.accent, color: T.onAccent }">{{ t('party.you') }}</span>
        </span>
        <span class="party-summary" :style="{ color: T.ink70 }">{{ summary(m) }}</span>
      </li>
    </ul>

    <button
      v-if="guestStore.canMerge"
      type="button"
      class="merge-btn"
      :style="{ background: T.ink, color: T.paper }"
      :disabled="guestStore.mergeInFlight"
      @click="emit('merge')"
    >
      {{ guestStore.mergeInFlight ? t('party.merging') : t('party.merge') }}
    </button>
    <p v-if="guestStore.canMerge" class="merge-hint" :style="{ color: T.ink70 }">{{ t('party.mergeHint') }}</p>
  </div>
</template>

<style scoped>
.party {
  border-radius: 20px;
  padding: 14px 16px;
  margin: 16px 22px 0;
}
.party-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 10px;
}
.party-title {
  font-size: 15px;
  font-weight: 700;
  letter-spacing: -0.01em;
}
.party-count {
  font-size: 14px;
  font-weight: 600;
}
.party-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.party-row {
  display: flex;
  align-items: center;
  gap: 10px;
}
.avatar {
  width: 30px;
  height: 30px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-size: 13px;
  font-weight: 800;
  flex-shrink: 0;
}
.party-name {
  flex: 1;
  font-size: 15px;
  font-weight: 600;
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
}
.you-badge {
  font-size: 11px;
  font-weight: 700;
  padding: 2px 7px;
  border-radius: 999px;
}
.party-summary {
  font-size: 13px;
  font-weight: 600;
  white-space: nowrap;
}
.merge-btn {
  width: 100%;
  margin-top: 14px;
  border: none;
  border-radius: 14px;
  padding: 13px;
  font-size: 16px;
  font-weight: 700;
  font-family: inherit;
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
}
.merge-btn:disabled {
  opacity: 0.6;
  cursor: default;
}
.merge-hint {
  margin: 8px 2px 0;
  font-size: 13px;
  line-height: 1.4;
  text-align: center;
}
</style>
