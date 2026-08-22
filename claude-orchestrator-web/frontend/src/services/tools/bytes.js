// btoa/atob are latin1-only, so every module moving text across a base64 boundary
// needs UTF-8 byte conversion. One copy is easier to keep correct than three.

export const encoder = new TextEncoder()
export const decoder = new TextDecoder()

export function bytesToBase64(bytes) {
  let binary = ''
  for (const byte of bytes) binary += String.fromCharCode(byte)
  return btoa(binary)
}

export function base64ToBytes(base64) {
  const binary = atob(base64)
  const bytes = new Uint8Array(binary.length)
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i)
  return bytes
}
