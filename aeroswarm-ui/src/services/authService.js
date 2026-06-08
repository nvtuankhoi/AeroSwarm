const BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:5501/api'
const TOKEN_KEY = 'aeroswarm_token'

export async function login(username, password) {
  const res = await fetch(`${BASE_URL}/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username, password }),
  })
  if (!res.ok) {
    const err = await res.json().catch(() => ({}))
    throw new Error(err.message || 'Login failed')
  }
  const data = await res.json()
  localStorage.setItem(TOKEN_KEY, data.token)
  return data
}

export function getToken() {
  return localStorage.getItem(TOKEN_KEY)
}

export function logout() {
  localStorage.removeItem(TOKEN_KEY)
}

export function isAuthenticated() {
  return !!getToken()
}
