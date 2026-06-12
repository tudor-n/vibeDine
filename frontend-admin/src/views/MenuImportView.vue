<script setup lang="ts">
import { computed, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'

import ImportStepIndicator from '@/components/menu-import/ImportStepIndicator.vue'
import StepUploadPhotos from '@/components/menu-import/StepUploadPhotos.vue'
import StepMapCategories from '@/components/menu-import/StepMapCategories.vue'
import StepReviewItems from '@/components/menu-import/StepReviewItems.vue'
import StepReviewHierarchy from '@/components/menu-import/StepReviewHierarchy.vue'
import PdfImportProgress from '@/components/menu-import/PdfImportProgress.vue'
import { useMenuImportStore } from '@/stores/useMenuImportStore'
import { useMenuManagementStore } from '@/stores/useMenuManagementStore'
import { useToast } from '@/composables/useToast'

const router = useRouter()
const importStore = useMenuImportStore()
const menuStore = useMenuManagementStore()
const toast = useToast()

watch(
  () => importStore.isImporting,
  async (now, before) => {
    if (before && !now && !importStore.importError) {
      const count = importStore.reviewItems.filter((r) => !r.skipped).length
      toast.success(`${count} produse importate`)
      await menuStore.fetchMenu()
      importStore.$reset()
      router.push('/menu')
    }
  },
)

const canDiscard = computed(
  () =>
    importStore.files.length > 0 ||
    importStore.isScanning ||
    importStore.pdfJob !== null ||
    importStore.extractedItems.length > 0,
)

async function discardImport() {
  if (!window.confirm('Renunți la importul curent? Toate fotografiile și progresul vor fi șterse.')) return
  if (importStore.pdfJob && importStore.pdfJob.id) {
    await importStore.cancelPdfImport()
  }
  importStore.$reset()
}

const acceptAction = computed(() => {
  if (importStore.step !== 3) return null
  if (importStore.hierarchy) {
    const uncategorized = importStore.hierarchy.uncategorizedItemRefs.length
    return {
      label: importStore.isImporting ? 'Salvez…' : 'Acceptă',
      handler: () => importStore.importHierarchy(),
      disabled: importStore.isImporting || uncategorized > 0,
      hint: uncategorized > 0 ? `${uncategorized} produse necategorisite` : null,
    }
  }
  const active = importStore.reviewItems.filter((r) => !r.skipped).length
  return {
    label: importStore.isImporting ? 'Import…' : `Importă ${active} produse →`,
    handler: () => importStore.importAll(),
    disabled: !importStore.canImport || importStore.isImporting,
    hint: null,
  }
})
</script>

<template>
  <div class="import-page">
    <!-- Sticky top bar — bleeds to <main>'s edges so it spans the full viewport
         within the dashboard shell and pins flush with <main>'s padding box. -->
    <header class="sticky-bar">
      <div class="sticky-bar-inner">
        <div class="sticky-bar-title">
          <div class="page-head-eyebrow">meniu · import</div>
          <h1 class="sticky-bar-h1">
            Din hârtie <span class="accent">în vitrină.</span>
          </h1>
        </div>
        <div class="sticky-bar-actions">
          <span v-if="acceptAction?.hint" class="accept-hint">{{ acceptAction.hint }}</span>
          <button
            v-if="canDiscard"
            type="button"
            class="btn-danger"
            @click="discardImport"
            title="Renunță la sesiunea curentă de import"
          >
            Renunță
          </button>
          <button
            v-if="acceptAction"
            type="button"
            class="pill-btn"
            :disabled="acceptAction.disabled"
            @click="acceptAction.handler()"
          >
            {{ acceptAction.label }}
          </button>
        </div>
      </div>
    </header>

    <div class="import-body">
      <RouterLink to="/menu" class="back-link">
        <span aria-hidden="true">←</span>
        <span>Înapoi la meniu</span>
      </RouterLink>

      <ImportStepIndicator :current="importStore.step" />

      <PdfImportProgress
        v-if="importStore.mode === 'pdf' && importStore.pdfJob && importStore.step === 1"
        :job="importStore.pdfJob"
        @cancel="importStore.cancelPdfImport()"
      />
      <StepUploadPhotos v-else-if="importStore.step === 1" />
      <StepMapCategories v-else-if="importStore.step === 2" />
      <StepReviewHierarchy v-else-if="importStore.hierarchy" />
      <StepReviewItems v-else />
    </div>
  </div>
</template>

<style scoped>
/* Bleed the page outward to match <main>'s edges (it has p-4 on mobile, p-10 on
   desktop). This lets the sticky bar span the full viewport width within the
   dashboard shell and pin at top:0 of <main>'s padding box without being inset
   by the page-level padding. */
.import-page {
  margin: -1rem;
  /* Right padding for the floating theme-toggle (.theme-toggle, top-right). */
}
@media (min-width: 768px) {
  .import-page {
    margin: -2.5rem;
  }
}

.sticky-bar {
  position: sticky;
  top: 0;
  z-index: 30;
  background: rgba(var(--paper-rgb), 0.92);
  backdrop-filter: blur(14px);
  -webkit-backdrop-filter: blur(14px);
  border-bottom: 1px solid var(--ink12);
}
.sticky-bar-inner {
  max-width: 64rem;
  margin: 0 auto;
  padding: 14px 1rem 14px 1rem;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
  /* Leave room for the fixed top-right .theme-toggle (44x44 with 18px offset). */
  padding-right: max(1rem, 80px);
}
@media (min-width: 768px) {
  .sticky-bar-inner {
    padding: 16px 2.5rem;
    padding-right: max(2.5rem, 92px);
  }
}
.sticky-bar-title { min-width: 0; }
.sticky-bar-h1 {
  font-family: 'Inter Tight', sans-serif;
  font-weight: 600;
  font-size: clamp(20px, 2.6vw, 26px);
  letter-spacing: -0.025em;
  line-height: 1;
  color: var(--ink);
  margin: 4px 0 0 0;
}
.sticky-bar-h1 .accent { color: var(--accent); }
.sticky-bar-actions {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
  align-items: center;
}

.import-body {
  max-width: 64rem;
  margin: 0 auto;
  padding: 24px 1rem 32px 1rem;
}
@media (min-width: 768px) {
  .import-body {
    padding: 32px 2.5rem 48px 2.5rem;
  }
}

.back-link {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--ink70);
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.14em;
  font-weight: 600;
  text-decoration: none;
  margin-bottom: 14px;
  transition: color 0.15s ease, transform 0.15s ease;
}
.back-link:hover { color: var(--ink); transform: translateX(-2px); }

.accept-hint {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.14em;
  color: var(--ink40);
  margin-right: 4px;
  white-space: nowrap;
}
</style>
