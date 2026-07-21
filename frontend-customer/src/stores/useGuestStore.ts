import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { apiClient, type JoinTableResponse, type MergePartyResponse, type PartyMember } from '@/services/api-client'
import { getDeviceId } from '@/services/deviceId'

// Guest identity is stored per-table (keyed by qrToken) in *localStorage*.
// This is what lets a phone keep its order across more than a same-tab reload:
// sessionStorage is wiped whenever the QR scanner opens the link in a fresh
// browser context (very common on phones — a re-scan, or the in-app browser
// spawning a new tab), so the guestId would be lost and the backend would mint a
// brand-new empty guest, stranding the draft the diner already built.
// localStorage survives that, so the same device re-attaches to the same guest +
// draft. Stale ids degrade gracefully: the backend purges idle guests, and an id
// that no longer matches this table just starts a fresh slot (see PartyController).
// Trade-off: two TABS of the *same* desktop browser now share one guest (only
// relevant when manually testing the multi-person party flow on one machine — on
// real phones each diner is a separate device with its own localStorage).
function storageKey(qrToken: string) {
  return `party.guest.${qrToken}`
}

function readStoredGuestId(qrToken: string): string | null {
  try {
    return localStorage.getItem(storageKey(qrToken))
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

  // Transient "X joined your table" banner for the phones already seated.
  // `key` forces the toast to re-animate when two joins land back-to-back.
  const joinNotice = ref<{ name: string; key: number } | null>(null)
  let joinInFlight = false
  let noticeTimer: ReturnType<typeof setTimeout> | null = null
  let noticeKey = 0

  const otherMembersWithItems = computed(() =>
    partyMembers.value.filter(
      (m) => m.guestId !== guestId.value && m.status === 'draft' && m.itemCount > 0,
    ),
  )
  // Merge is meaningful only once someone *else* at the table also has a cart.
  const canMerge = computed(() => otherMembersWithItems.value.length > 0)

  async function join(qrToken: string): Promise<JoinTableResponse> {
    // Our own join also broadcasts PartyMemberJoined, and that push can outrun
    // the HTTP response (guestId still holds the old value) — suppress toasts
    // until the response has landed so a phone never announces itself.
    joinInFlight = true
    try {
      const res = await apiClient.joinTable(qrToken, readStoredGuestId(qrToken), getDeviceId())
      guestId.value = res.guestId
      displayName.value = res.displayName
      partyMembers.value = res.partyMembers
      mergedInto.value = null
      try {
        localStorage.setItem(storageKey(qrToken), res.guestId)
      } catch {
        // private mode / disabled storage — in-memory identity still works
      }
      return res
    } finally {
      joinInFlight = false
    }
  }

  // Pop the "X joined" toast; it dismisses itself after a few seconds.
  function announceJoin(name: string) {
    if (joinInFlight) return
    joinNotice.value = { name, key: ++noticeKey }
    if (noticeTimer) clearTimeout(noticeTimer)
    noticeTimer = setTimeout(() => {
      joinNotice.value = null
      noticeTimer = null
    }, 3000)
  }

  async function refreshParty(qrToken: string | null) {
    if (!qrToken) return
    try {
      // Passing our guestId makes every refresh double as a presence heartbeat.
      const res = await apiClient.getParty(qrToken, guestId.value)
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
    joinNotice,
    otherMembersWithItems,
    canMerge,
    join,
    refreshParty,
    merge,
    markMerged,
    clearMerged,
    announceJoin,
  }
})
