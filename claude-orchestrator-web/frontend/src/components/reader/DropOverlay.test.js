import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import DropOverlay from './DropOverlay.vue'

function makeFile(name='a.md', type='text/markdown') {
  return new File(['x'], name, { type })
}

describe('DropOverlay', () => {
  it('is hidden by default', () => {
    const w = mount(DropOverlay)
    expect(w.get('[data-testid="drop-overlay"]').classes()).toContain('invisible')
  })

  it('shows on dragenter and hides on dragleave', async () => {
    const w = mount(DropOverlay)
    await w.trigger('dragenter', { dataTransfer: { types: ['Files'] } })
    expect(w.get('[data-testid="drop-overlay"]').classes()).not.toContain('invisible')
    await w.trigger('dragleave')
    expect(w.get('[data-testid="drop-overlay"]').classes()).toContain('invisible')
  })

  it('emits file-dropped with the dropped File and hides', async () => {
    const w = mount(DropOverlay)
    const file = makeFile('a.md')
    await w.trigger('dragenter', { dataTransfer: { types: ['Files'] } })
    await w.trigger('drop', { dataTransfer: { files: [file] } })
    expect(w.emitted('file-dropped')?.[0]?.[0]).toBe(file)
    expect(w.get('[data-testid="drop-overlay"]').classes()).toContain('invisible')
  })

  it('does not emit if no file in drop', async () => {
    const w = mount(DropOverlay)
    await w.trigger('drop', { dataTransfer: { files: [] } })
    expect(w.emitted('file-dropped')).toBeFalsy()
  })
})
