import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { apiClient, type JoinTableResponse, type MergePartyResponse, type PartyMember } from '@/services/api-client'

// Guest identity is stored per-table (keyed by qrToken) in *sessionStorage*.
// sessionStorage is per-tab yet still survives reloads, which is exactly what we
// want: every phone (and every browser tab when testing) is its OWN guest with
// its OWN draft. localStorage would be shared across tabs of the same browser,
// collapsing two testers into a single guest ("one person controls everything").
// This also matches useSessionStore, which already persists orderId in
// sessionStorage. A phone that later scans a different table starts a fresh slot.
function storageKey(qrToken: string) {
  return `party.guest.${qrToken}`
}

function readStoredGuestId(qrToken: string): string | null {
  try {
    return sessionStorage.getItem(storageKey(qrToken))
  } catch {
    return null
  }
}

// Leaf store: depends only on the API client (never on session/cart stores) so
// there is no circular import. Callers that hold the qrToken pass it in.
export const useGuestStore = defineStore('guest', () => {
  const guestId = ref<string | null>(null)
  const displayName = ref<string | null>(null)
  const partyMembers = ref<PartyMember[]>([])
  const mergeInFlight = ref(false)

  // Set on the phones whose drafts were folded into someone else's order. The
  // OrderingView watches this to switch to the "merged" screen.
  const mergedInto = ref<{ initiatorDisplayName: string } | null>(null)

  const otherMembersWithItems = computed(() =>
    partyMembers.value.filter(
      (m) => m.guestId !== guestId.value && m.status === 'draft' && m.itemCount > 0,
    ),
  )
  // Merge is meaningful only once someone *else* at the table also has a cart.
  const canMerge = computed(() => otherMembersWithItems.value.length > 0)

  async function join(qrToken: string): Promise<JoinTableResponse> {
    const res = await apiClient.joinTable(qrToken, readStoredGuestId(qrToken))
    guestId.value = res.guestId
    displayName.value = res.displayName
    partyMembers.value = res.partyMembers
    mergedInto.value = null
    try {
      sessionStorage.setItem(storageKey(qrToken), res.guestId)
    } catch {
      // private mode / disabled storage — in-memory identity still works
    }
    return res
  }

  async function refreshParty(qrToken: string | null) {
    if (!qrToken) return
    try {
      const res = await apiClient.getParty(qrToken)
      partyMembers.value = res.partyMembers
    } catch {
      // transient — the panel just keeps its previous snapshot
    }
  }

  async function merge(qrToken: string | null): Promise<MergePartyResponse | null> {
    if (!qrToken || !guestId.value || mergeInFlight.value) return null
    mergeInFlight.value = true
    try {
      const res = await apiClient.mergeParty(qrToken, guestId.value)
      partyMembers.value = res.partyMembers
      return res
    } finally {
      mergeInFlight.value = false
    }
  }

  function markMerged(initiatorDisplayName: string) {
    mergedInto.value = { initiatorDisplayName }
  }

  function clearMerged() {
    mergedInto.value = null
  }

  return {
    guestId,
    displayName,
    partyMembers,
    mergeInFlight,
    mergedInto,
    otherMembersWithItems,
    canMerge,
    join,
    refreshParty,
    merge,
    markMerged,
    clearMerged,
  }
})
