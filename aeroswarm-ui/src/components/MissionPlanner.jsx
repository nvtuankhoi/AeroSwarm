import { useState } from 'react'
import { uploadSwarmMission } from '../services/swarmService'

export default function MissionPlanner({
  open,
  waypoints,
  onRemoveWaypoint,
  onClear,
  onClose,
  onUploaded,
  onLog,
}) {
  const [formation, setFormation] = useState('V')
  const [spacingM, setSpacingM] = useState(15)
  const [altitude, setAltitude] = useState(10)
  const [uploading, setUploading] = useState(false)

  if (!open) return null

  const handleUpload = async () => {
    if (waypoints.length === 0) {
      onLog?.('WARN', 'No leader waypoints — click map to add at least one.')
      return
    }
    setUploading(true)
    try {
      const payload = {
        formation,
        spacingM: Number(spacingM),
        leaderWaypoints: waypoints.map((w) => ({
          lat: w[0],
          lon: w[1],
          alt: Number(altitude),
        })),
      }
      const res = await uploadSwarmMission(payload)
      onLog?.(
        'SYS',
        `Mission #${res.flightId} uploaded to ${res.uploadedDrones} drone(s) (${formation}, spacing ${spacingM}m).`,
      )
      onUploaded?.(res)
      onClose()
    } catch (err) {
      onLog?.('WARN', `Mission upload failed: ${err.message}`)
    } finally {
      setUploading(false)
    }
  }

  return (
    <div className="fixed inset-0 z-[2000] bg-black/60 backdrop-blur-sm flex items-center justify-center">
      <div className="glass-panel bg-surface border border-outline-variant/40 rounded-lg p-6 w-[420px] max-w-[90vw]">
        <div className="flex justify-between items-start mb-4">
          <div>
            <h2 className="text-lg font-bold text-primary tracking-tight uppercase">
              Plan Swarm Mission
            </h2>
            <p className="text-xs text-outline mt-0.5 uppercase tracking-widest">
              Click map to add leader waypoints
            </p>
          </div>
          <button
            onClick={onClose}
            className="text-on-surface-variant hover:text-error p-1 rounded"
            title="Close"
          >
            <span className="material-symbols-outlined text-[20px]">close</span>
          </button>
        </div>

        <div className="flex flex-col gap-3">
          {/* Formation */}
          <div>
            <label className="text-[11px] font-bold uppercase tracking-widest text-outline block mb-1">
              Formation
            </label>
            <div className="flex gap-2">
              {['V', 'LINE'].map((f) => (
                <button
                  key={f}
                  type="button"
                  onClick={() => setFormation(f)}
                  className={`flex-1 py-1.5 rounded text-[11px] font-bold uppercase tracking-widest border transition-colors ${
                    formation === f
                      ? 'bg-primary text-on-primary border-primary'
                      : 'bg-surface-container-high border-outline-variant/30 text-on-surface hover:border-primary/40'
                  }`}
                >
                  {f}
                </button>
              ))}
            </div>
          </div>

          {/* Spacing + Altitude */}
          <div className="grid grid-cols-2 gap-2">
            <div>
              <label className="text-[11px] font-bold uppercase tracking-widest text-outline block mb-1">
                Spacing (m)
              </label>
              <input
                type="number"
                min="1"
                value={spacingM}
                onChange={(e) => setSpacingM(e.target.value)}
                className="w-full bg-surface-container-high border border-outline-variant/30 rounded px-2 py-1 text-sm font-mono text-on-surface focus:outline-none focus:border-primary/60"
              />
            </div>
            <div>
              <label className="text-[11px] font-bold uppercase tracking-widest text-outline block mb-1">
                Altitude (m)
              </label>
              <input
                type="number"
                min="1"
                value={altitude}
                onChange={(e) => setAltitude(e.target.value)}
                className="w-full bg-surface-container-high border border-outline-variant/30 rounded px-2 py-1 text-sm font-mono text-on-surface focus:outline-none focus:border-primary/60"
              />
            </div>
          </div>

          {/* Waypoints list */}
          <div>
            <div className="flex justify-between items-center mb-1">
              <span className="text-[11px] font-bold uppercase tracking-widest text-outline">
                Leader Waypoints ({waypoints.length})
              </span>
              {waypoints.length > 0 && (
                <button
                  type="button"
                  onClick={onClear}
                  className="text-[10px] uppercase text-error hover:text-error/80"
                >
                  Clear
                </button>
              )}
            </div>
            <div className="bg-surface-container-low border border-outline-variant/20 rounded max-h-32 overflow-y-auto">
              {waypoints.length === 0 ? (
                <div className="text-center text-outline text-xs py-4 italic">
                  Click on the map to add a waypoint
                </div>
              ) : (
                <ul className="divide-y divide-outline-variant/10">
                  {waypoints.map((w, i) => (
                    <li
                      key={i}
                      className="flex justify-between items-center px-2 py-1.5 hover:bg-surface-container-high/50"
                    >
                      <span className="font-mono text-xs text-on-surface">
                        #{i + 1} — {w[0].toFixed(5)}, {w[1].toFixed(5)}
                      </span>
                      <button
                        type="button"
                        onClick={() => onRemoveWaypoint(i)}
                        className="text-outline hover:text-error p-1"
                      >
                        <span className="material-symbols-outlined text-[14px]">delete</span>
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </div>

          {/* Actions */}
          <div className="flex gap-2 mt-2">
            <button
              onClick={onClose}
              className="flex-1 py-2 rounded text-xs font-bold uppercase tracking-widest border border-outline-variant/30 text-on-surface hover:bg-surface-container-high"
            >
              Cancel
            </button>
            <button
              onClick={handleUpload}
              disabled={uploading || waypoints.length === 0}
              className="flex-1 py-2 rounded text-xs font-bold uppercase tracking-widest bg-secondary text-on-secondary disabled:opacity-50 hover:bg-secondary-fixed transition-colors"
            >
              {uploading ? 'UPLOADING…' : 'UPLOAD'}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
