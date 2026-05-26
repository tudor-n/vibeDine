<script setup lang="ts">
import { computed, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'

import ImportStepIndicator from '@/components/menu-import/ImportStepIndicator.vue'
import StepUploadPhotos from '@/components/menu-import/StepUploadPhotos.vue'
import StepMapCategories from '@/components/menu-import/StepMapCategories.vue'
import StepReviewItems from '@/components/menu-import/StepReviewItems.vue'
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
</script>

<template>
  <div class="max-w-4xl mx-auto">
    <RouterLink to="/menu" class="back-link">
      <span aria-hidden="true">←</span>
      <span>Înapoi la meniu</span>
    </RouterLink>

    <header class="page-head">
      <div>
        <div class="page-head-eyebrow">meniu · import</div>
        <h1 class="page-head-title">
          Din hârtie <span class="accent">în vitrină.</span>
        </h1>
      </div>
      <div class="page-head-actions">
        <button
          v-if="canDiscard"
          type="button"
          class="btn-danger"
          @click="discardImport"
          title="Renunță la sesiunea curentă de import"
        >
          Renunță
        </button>
      </div>
    </header>

    <ImportStepIndicator :current="importStore.step" />

    <PdfImportProgress
      v-if="importStore.mode === 'pdf' && importStore.pdfJob && importStore.step === 1"
      :job="importStore.pdfJob"
      @cancel="importStore.cancelPdfImport()"
    />
    <StepUploadPhotos v-else-if="importStore.step === 1" />
    <StepMapCategories v-else-if="importStore.step === 2" />
    <StepReviewItems v-else />
  </div>
</template>

<style scoped>
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
</style>
