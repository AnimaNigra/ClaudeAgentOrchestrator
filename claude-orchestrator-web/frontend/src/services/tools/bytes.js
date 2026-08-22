// btoa/atob umí jen latin1, takže každý modul, který posílá text přes base64
// hranici, potřebuje převod na UTF-8 bajty. Jedna kopie se udržuje snáz než tři.

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
