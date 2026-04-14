// IndexedDB wrapper for FileSystemFileHandle persistence.
// Enables reopening lite-mode files across sessions (Chromium browsers).

const DB_NAME = 'claude-orchestrator-reader-fsa'
const STORE = 'handles'

let _dbPromise = null
function openDb() {
  if (_dbPromise) return _dbPromise
  _dbPromise = new Promise((resolve, reject) => {
    if (!('indexedDB' in window)) return reject(new Error('IndexedDB unavailable'))
    const req = indexedDB.open(DB_NAME, 1)
    req.onupgradeneeded = () => req.result.createObjectStore(STORE)
    req.onsuccess = () => resolve(req.result)
    req.onerror = () => reject(req.error)
  })
  return _dbPromise
}

export function isSupported() {
  return typeof window !== 'undefined'
    && typeof window.showOpenFilePicker === 'function'
    && 'indexedDB' in window
}

export async function saveHandle(key, handle) {
  const db = await openDb()
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE, 'readwrite')
    tx.objectStore(STORE).put(handle, key)
    tx.oncomplete = () => resolve()
    tx.onerror = () => reject(tx.error)
  })
}

export async function getHandle(key) {
  const db = await openDb()
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE, 'readonly')
    const req = tx.objectStore(STORE).get(key)
    req.onsuccess = () => resolve(req.result || null)
    req.onerror = () => reject(req.error)
  })
}

export async function deleteHandle(key) {
  const db = await openDb()
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE, 'readwrite')
    tx.objectStore(STORE).delete(key)
    tx.oncomplete = () => resolve()
    tx.onerror = () => reject(tx.error)
  })
}

export function generateKey() {
  return 'fsa-' + Math.random().toString(36).slice(2) + '-' + Date.now().toString(36)
}

// Calls showOpenFilePicker with our Markdown accept filter.
// Returns { file, handle } on success, or null if user cancelled / API unavailable.
export async function pickFileWithHandle() {
  if (!isSupported()) return null
  try {
    const [handle] = await window.showOpenFilePicker({
      multiple: false,
      types: [{
        description: 'Markdown / text',
        accept: {
          'text/markdown': ['.md', '.markdown', '.mdx'],
          'text/plain': ['.txt'],
        },
      }],
    })
    const file = await handle.getFile()
    return { file, handle }
  } catch (err) {
    if (err.name === 'AbortError') return null
    throw err
  }
}

// Retrieves a handle by key, checks/requests read permission, and returns
// { file, handle } or null if denied / not found.
export async function reopenFromHandle(key) {
  const handle = await getHandle(key)
  if (!handle) return null
  // Query current permission
  let perm = await handle.queryPermission({ mode: 'read' })
  if (perm !== 'granted') {
    perm = await handle.requestPermission({ mode: 'read' })
  }
  if (perm !== 'granted') return null
  const file = await handle.getFile()
  return { file, handle }
}
