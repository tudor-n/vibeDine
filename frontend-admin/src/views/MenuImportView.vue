<script setup lang="ts">
import { onBeforeUnmount, watch } from 'vue'
import { useRouter } from 'vue-router'

import ImportStepIndicator from '@/components/menu-import/ImportStepIndicator.vue'
import StepUploadPhotos from '@/components/menu-import/StepUploadPhotos.vue'
import StepMapCategories from '@/components/menu-import/StepMapCategories.vue'
import StepReviewItems from '@/components/menu-import/StepReviewItems.vue'
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

onBeforeUnmount(() => {
  importStore.$reset()
})
</script>

<template>
  <div class="max-w-5xl mx-auto">
    <header class="mb-8">
      <div class="eyebrow mb-3">meniu · import</div>
      <h1 class="display-title text-[44px] md:text-[56px]">
        Din hârtie<br />
        <span class="text-accent">în vitrină.</span>
      </h1>
      <p class="text-ink-70 text-lg mt-4 max-w-2xl">
        Trei pași: încarci fotografiile, mapezi secțiunile și revizuiești produsele. La final,
        importul se face într-o singură tranzacție.
      </p>
    </header>

    <ImportStepIndicator :current="importStore.step" />

    <StepUploadPhotos v-if="importStore.step === 1" />
    <StepMapCategories v-else-if="importStore.step === 2" />
    <StepReviewItems v-else />
  </div>
</template>
