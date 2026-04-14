import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import OpenFileDialog from './OpenFileDialog.vue'

describe('OpenFileDialog', () => {
  it('open button disabled when input empty', () => {
    const w = mount(OpenFileDialog, { props: { open: true } })
    const btn = w.get('[data-testid="open-submit"]')
    expect(btn.attributes('disabled')).toBeDefined()
  })

  it('open button enabled after non-empty path', async () => {
    const w = mount(OpenFileDialog, { props: { open: true } })
    await w.get('[data-testid="open-path-input"]').setValue('/a.md')
    const btn = w.get('[data-testid="open-submit"]')
    expect(btn.attributes('disabled')).toBeUndefined()
  })

  it('emits submit with trimmed path on click', async () => {
    const w = mount(OpenFileDialog, { props: { open: true } })
    await w.get('[data-testid="open-path-input"]').setValue('   /a.md  ')
    await w.get('[data-testid="open-submit"]').trigger('click')
    expect(w.emitted('submit')?.[0]?.[0]).toBe('/a.md')
  })

  it('emits close on cancel', async () => {
    const w = mount(OpenFileDialog, { props: { open: true } })
    await w.get('[data-testid="open-cancel"]').trigger('click')
    expect(w.emitted('close')).toBeTruthy()
  })

  it('renders nothing when open=false', () => {
    const w = mount(OpenFileDialog, { props: { open: false } })
    expect(w.find('[data-testid="open-submit"]').exists()).toBe(false)
  })

  it('emits submit-file with the File when Browse picks one', async () => {
    const w = mount(OpenFileDialog, { props: { open: true } })
    const file = new File(['# hi'], 'note.md', { type: 'text/markdown' })
    const input = w.find('input[type="file"]').element
    Object.defineProperty(input, 'files', { value: [file], configurable: true })
    await w.find('input[type="file"]').trigger('change')
    expect(w.emitted('submit-file')?.[0]?.[0]).toBe(file)
    // Path input should NOT be filled (Browse now opens directly in lite mode)
    expect(w.get('[data-testid="open-path-input"]').element.value).toBe('')
  })
})
