// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

import StepUploadPhotos from '@/components/menu-import/StepUploadPhotos.vue'

const mockStore = vi.hoisted(() => ({
  files: [],
  canAdvanceFromStep1: false,
  isScanning: false,
  scanError: null,
  importError: null,
  addFiles: vi.fn(() => ({ rejected: [] })),
  removeFile: vi.fn(),
  scan: vi.fn(),
  startPdfImport: vi.fn(),
}))

vi.mock('@/stores/useMenuImportStore', () => ({
  useMenuImportStore: () => mockStore,
}))

describe('StepUploadPhotos', () => {
  it('describes the 5 MB photo limit separately from the 110 MB PDF limit', () => {
    const wrapper = mount(StepUploadPhotos)

    expect(wrapper.text()).toContain('5 MB pe fotografie')
    expect(wrapper.text()).toContain('110 MB')
    expect(wrapper.text()).toContain('PDF')
  })
})
