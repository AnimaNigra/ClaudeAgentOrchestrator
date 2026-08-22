const MIN_COUNT = 1
const MAX_COUNT = 100

export function generateUuids(count) {
  const parsed = Math.floor(Number(count))
  const safe = Number.isFinite(parsed) ? parsed : MIN_COUNT
  const clamped = Math.min(MAX_COUNT, Math.max(MIN_COUNT, safe))
  return { ok: true, value: Array.from({ length: clamped }, () => crypto.randomUUID()) }
}
