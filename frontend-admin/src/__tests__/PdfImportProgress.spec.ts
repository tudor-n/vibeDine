import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import PdfImportProgress from '@/components/menu-import/PdfImportProgress.vue'

function makeJob(overrides: Partial<InstanceType<typeof PdfImportProgress>['$props']['job']> = {}) {
  return {
    id: 'job-1',
    status: 'extracting' as const,
    phase: 'extracting' as const,
    fileName: 'meniu_pranz.pdf',
    fileSizeBytes: 91226112, // ~87 MB
    processedPages: 62,
    totalPages: 100,
    failedPages: 0,
    etaSeconds: 45,
    ...overrides,
  }
}

describe('PdfImportProgress', () => {
  it('renders the filename', () => {
    const wrapper = mount(PdfImportProgress, { props: { job: makeJob() } })
    expect(wrapper.text()).toContain('meniu_pranz.pdf')
  })

  it('renders file size and page count in the meta line', () => {
    const wrapper = mount(PdfImportProgress, { props: { job: makeJob() } })
    // 91226112 bytes = 87.0 MB  (91226112 / 1024 / 1024 = 87.0)
    expect(wrapper.text()).toContain('87')
    expect(wrapper.text()).toContain('100 pagini')
  })

  it('renders progress counter as "62 / 100"', () => {
    const wrapper = mount(PdfImportProgress, { props: { job: makeJob() } })
    expect(wrapper.text()).toContain('62 / 100')
  })

  it('renders an uploading status before page totals are known', () => {
    const wrapper = mount(PdfImportProgress, {
      props: {
        job: makeJob({
          status: 'uploading' as const,
          processedPages: 0,
          totalPages: 0,
        }),
      },
    })
    expect(wrapper.text()).toContain('Se încarcă PDF-ul')
  })

  it('renders exactly totalPages dots', () => {
    const wrapper = mount(PdfImportProgress, { props: { job: makeJob() } })
    expect(wrapper.findAll('[data-testid="page-dot"]')).toHaveLength(100)
  })

  it('emits "cancel" when the cancel button is clicked', async () => {
    const wrapper = mount(PdfImportProgress, { props: { job: makeJob() } })
    await wrapper.find('[data-testid="cancel-btn"]').trigger('click')
    expect(wrapper.emitted('cancel')).toHaveLength(1)
  })

  it('hides the cancel button when status is "complete"', () => {
    const wrapper = mount(PdfImportProgress, {
      props: { job: makeJob({ status: 'complete' }) },
    })
    expect(wrapper.find('[data-testid="cancel-btn"]').exists()).toBe(false)
  })

  it('hides the cancel button when status is "failed"', () => {
    const wrapper = mount(PdfImportProgress, {
      props: {
        job: makeJob({
          status: 'failed',
          errorMessage: 'Nu am putut procesa fișierul.',
        }),
      },
    })
    expect(wrapper.find('[data-testid="cancel-btn"]').exists()).toBe(false)
  })

  it('displays errorMessage when status is "failed"', () => {
    const wrapper = mount(PdfImportProgress, {
      props: {
        job: makeJob({
          status: 'failed',
          errorMessage: 'Nu am putut procesa fișierul.',
        }),
      },
    })
    expect(wrapper.text()).toContain('Nu am putut procesa fișierul.')
  })

  it('hides the cancel button when status is "cancelled"', () => {
    const wrapper = mount(PdfImportProgress, {
      props: { job: makeJob({ status: 'cancelled' }) },
    })
    expect(wrapper.find('[data-testid="cancel-btn"]').exists()).toBe(false)
  })
})
