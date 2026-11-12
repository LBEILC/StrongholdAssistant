export const PASSWORD_ITERATIONS = 20000
const encode = (bytes: Uint8Array) => btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
const decode = (value: string) => Uint8Array.from(atob(value.replace(/-/g, '+').replace(/_/g, '/')), c => c.charCodeAt(0))
async function derive(password: string, salt: Uint8Array<ArrayBuffer>, iterations: number) {
 const key = await crypto.subtle.importKey('raw', new TextEncoder().encode(password), 'PBKDF2', false, ['deriveBits'])
 return new Uint8Array(await crypto.subtle.deriveBits({ name: 'PBKDF2', hash: 'SHA-256', salt, iterations }, key, 256))
}
export async function hashPassword(password: string) {
 const salt = crypto.getRandomValues(new Uint8Array(16))
 return `pbkdf2-sha256$${PASSWORD_ITERATIONS}$${encode(salt)}$${encode(await derive(password, salt, PASSWORD_ITERATIONS))}`
}
export async function checkPassword(password: string, stored: string) {
 const [, iterations, salt, encoded] = stored.split('$')
 const actual = await derive(password, decode(salt), Number(iterations)), expected = decode(encoded)
 // 哈希固定为 32 字节，逐字节累积差异，不提前返回。
 let difference = 0
 for (let i = 0; i < 32; i++) difference |= actual[i] ^ expected[i]
 return difference === 0
}
