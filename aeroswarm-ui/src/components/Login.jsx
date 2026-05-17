import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { login } from '../services/authService'

export default function Login() {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const navigate = useNavigate()

  const handleSubmit = async () => {
    if (!username || !password) {
      setError('Please enter username and password')
      return
    }
    setLoading(true)
    setError('')
    try {
      await login(username, password)
      navigate('/dashboard')
    } catch (err) {
      setError(err.message || 'Authentication failed')
    } finally {
      setLoading(false)
    }
  }

  const handleKeyDown = (e) => {
    if (e.key === 'Enter') handleSubmit()
  }

  return (
    <div className="min-h-screen bg-background flex items-center justify-center grid-overlay">
      <div className="w-full max-w-md">
        {/* Logo */}
        <div className="text-center mb-10">
          <div className="flex items-center justify-center gap-3 mb-3">
            <span className="material-symbols-outlined text-primary text-4xl">hub</span>
            <h1 className="text-4xl font-bold tracking-widest text-primary uppercase" style={{ fontFamily: 'Inter' }}>
              AeroSwarm
            </h1>
          </div>
          <p className="text-on-surface-variant text-sm tracking-widest uppercase">Multi-UAV Command Center</p>
          <div className="mt-3 h-px bg-gradient-to-r from-transparent via-primary/30 to-transparent" />
        </div>

        {/* Card */}
        <div className="glass-panel rounded-xl p-8">
          <h2 className="text-on-surface text-lg font-semibold mb-6 text-center tracking-wide uppercase" style={{ fontFamily: 'Inter' }}>
            Operator Authentication
          </h2>

          {error && (
            <div className="mb-4 p-3 rounded bg-error-container border border-error/30 text-on-error-container text-sm flex items-center gap-2">
              <span className="material-symbols-outlined text-[16px]">error</span>
              {error}
            </div>
          )}

          <div className="flex flex-col gap-4">
            <div className="flex flex-col gap-1">
              <label className="text-outline text-[11px] uppercase tracking-widest font-bold">Username</label>
              <div className="flex items-center gap-2 bg-surface-container-high/60 border border-outline-variant/40 rounded px-3 py-2.5 focus-within:border-primary/50 transition-colors">
                <span className="material-symbols-outlined text-outline text-[18px]">person</span>
                <input
                  type="text"
                  value={username}
                  onChange={e => setUsername(e.target.value)}
                  onKeyDown={handleKeyDown}
                  placeholder="admin"
                  className="bg-transparent flex-1 text-on-surface text-sm outline-none placeholder:text-outline"
                />
              </div>
            </div>

            <div className="flex flex-col gap-1">
              <label className="text-outline text-[11px] uppercase tracking-widest font-bold">Password</label>
              <div className="flex items-center gap-2 bg-surface-container-high/60 border border-outline-variant/40 rounded px-3 py-2.5 focus-within:border-primary/50 transition-colors">
                <span className="material-symbols-outlined text-outline text-[18px]">lock</span>
                <input
                  type="password"
                  value={password}
                  onChange={e => setPassword(e.target.value)}
                  onKeyDown={handleKeyDown}
                  placeholder="••••••••"
                  className="bg-transparent flex-1 text-on-surface text-sm outline-none placeholder:text-outline"
                />
              </div>
            </div>

            <div
              onClick={!loading ? handleSubmit : undefined}
              className={`mt-2 py-3 px-6 rounded flex items-center justify-center gap-2 font-bold text-sm uppercase tracking-widest cursor-pointer transition-all select-none
                ${loading
                  ? 'bg-secondary/40 text-on-secondary/50 cursor-not-allowed'
                  : 'bg-secondary text-on-secondary hover:bg-secondary-fixed active:scale-95'
                }`}
            >
              {loading ? (
                <>
                  <span className="material-symbols-outlined text-[18px] animate-spin">progress_activity</span>
                  Authenticating...
                </>
              ) : (
                <>
                  <span className="material-symbols-outlined text-[18px]">login</span>
                  Access System
                </>
              )}
            </div>
          </div>

          <p className="mt-6 text-center text-outline text-xs">
            Authorized personnel only — All activity is logged
          </p>
        </div>

        <p className="text-center text-outline/50 text-xs mt-6">AeroSwarm v0.1 · ESP32-C3 MAVLink v2</p>
      </div>
    </div>
  )
}
