import { useEffect, useState, useRef, useCallback, Fragment } from 'react'
import { useNavigate } from 'react-router-dom'
import { MapContainer, TileLayer, Marker, Polyline, Popup, useMap } from 'react-leaflet'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import { HubConnectionBuilder } from '@microsoft/signalr'
import axios from 'axios'
import { getToken, logout } from '../services/authService'

const API = 'http://localhost:5501/api'
const MAX_TRAIL = 20
const MAX_LOGS = 100

// ─── helpers ────────────────────────────────────────────────────────────────

function headingToDir(h) {
  const d = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW']
  return d[Math.round((h % 360) / 45) % 8]
}

function droneColor(drone) {
  if (!drone || !drone.isArmed) return '#ffb4ab' // error/red
  if (drone.mode === 'GUIDED') return '#4ae176'   // secondary/green
  if (drone.mode === 'LOITER') return '#ffb95f'   // tertiary/orange
  return '#adc6ff'                                 // primary/blue
}

function batteryIcon(pct) {
  if (pct > 75) return 'battery_full'
  if (pct > 50) return 'battery_5_bar'
  if (pct > 25) return 'battery_3_bar'
  return 'battery_1_bar'
}

function createDroneIcon(color, heading) {
  const svg = `
    <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
      <g transform="rotate(${heading}, 16, 16)">
        <polygon points="16,4 26,28 16,22 6,28"
          fill="${color}"
          stroke="rgba(0,0,0,0.5)"
          stroke-width="1"
          filter="url(#glow)"
        />
      </g>
      <defs>
        <filter id="glow">
          <feGaussianBlur stdDeviation="2" result="coloredBlur"/>
          <feMerge>
            <feMergeNode in="coloredBlur"/>
            <feMergeNode in="SourceGraphic"/>
          </feMerge>
        </filter>
      </defs>
    </svg>`
  return L.divIcon({
    html: svg,
    className: '',
    iconSize: [32, 32],
    iconAnchor: [16, 16],
    popupAnchor: [0, -16],
  })
}

// ─── sub-components ──────────────────────────────────────────────────────────

function MapCenterUpdater({ drones }) {
  const map = useMap()
  const centered = useRef(false)
  useEffect(() => {
    if (centered.current) return
    const active = Object.values(drones).find(d => d && d.latitude !== 0)
    if (active) {
      map.setView([active.latitude, active.longitude], 15)
      centered.current = true
    }
  }, [drones, map])
  return null
}

function DroneTelemetryCard({ droneId, drone, onCommand }) {
  const color = droneColor(drone)
  const colorClass = !drone || !drone.isArmed
    ? 'border-t-error text-error'
    : drone?.mode === 'GUIDED'
    ? 'border-t-secondary text-secondary'
    : drone?.mode === 'LOITER'
    ? 'border-t-tertiary text-tertiary'
    : 'border-t-primary text-primary'

  const statusTextClass = !drone || !drone.isArmed
    ? 'text-error'
    : drone?.mode === 'GUIDED'
    ? 'text-secondary'
    : drone?.mode === 'LOITER'
    ? 'text-tertiary'
    : 'text-primary'

  const dotClass = !drone || !drone.isArmed
    ? 'bg-error shadow-[0_0_5px_#ffb4ab]'
    : drone?.mode === 'GUIDED'
    ? 'bg-secondary shadow-[0_0_5px_#4ae176]'
    : drone?.mode === 'LOITER'
    ? 'bg-tertiary shadow-[0_0_5px_#ffb95f]'
    : 'bg-primary shadow-[0_0_5px_#adc6ff]'

  const cmd = (action) => onCommand(droneId, action)

  if (!drone) {
    return (
      <div className={`glass-panel rounded-lg p-4 flex flex-col gap-3 border-t-2 border-t-outline`}>
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded bg-surface-container flex items-center justify-center border border-outline-variant/30">
            <span className="material-symbols-outlined text-outline">flight</span>
          </div>
          <div>
            <h3 className="font-mono text-lg font-semibold text-on-surface">DRONE #{droneId}</h3>
            <p className="text-xs text-outline uppercase tracking-widest mt-0.5 animate-pulse">
              WAITING FOR SIGNAL...
            </p>
          </div>
        </div>
      </div>
    )
  }

  return (
    <div className={`glass-panel rounded-lg p-4 flex flex-col gap-3 border-t-2 ${colorClass.split(' ')[0]}`}>
      {/* Header */}
      <div className="flex justify-between items-start">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded bg-surface-container flex items-center justify-center border border-outline-variant/30">
            <span className="material-symbols-outlined text-on-surface">flight</span>
          </div>
          <div>
            <h3 className="font-mono text-lg font-semibold text-on-surface">DRONE #{droneId}</h3>
            <div className="flex items-center gap-2 mt-0.5">
              <span className={`w-2 h-2 rounded-full ${dotClass}`} />
              <span className={`text-[11px] font-bold uppercase tracking-widest ${statusTextClass}`}>
                {drone.mode} | {drone.isArmed ? 'ARMED' : 'DISARMED'}
              </span>
            </div>
          </div>
        </div>
        <div className="text-right flex flex-col items-end">
          <div className={`flex items-center gap-1 ${statusTextClass}`}>
            <span className="material-symbols-outlined text-[16px]">{batteryIcon(drone.batteryPercent)}</span>
            <span className="font-mono text-sm font-medium">{drone.batteryPercent}%</span>
          </div>
          <span className="font-mono text-xs text-outline mt-0.5">{drone.batteryVoltage?.toFixed(1)}V</span>
        </div>
      </div>

      {/* Telemetry Grid */}
      <div className="grid grid-cols-3 gap-2 bg-surface-container/50 p-3 rounded border border-outline-variant/10">
        <TelCell label="ALTITUDE" value={`${drone.altitude?.toFixed(1)}m`} sub="Rel" valueClass="text-primary" />
        <TelCell label="SPEED" value={`${drone.speed?.toFixed(1)} m/s`} sub="Ground" />
        <TelCell label="HEADING" value={`${Math.round(drone.heading)}° ${headingToDir(drone.heading)}`} sub="Yaw" />
        <div className="col-span-3 mt-2 pt-2 border-t border-outline-variant/20 flex justify-between">
          <div className="flex gap-4">
            <TelCell label="LATITUDE" value={`${drone.latitude?.toFixed(4)}°`} small />
            <TelCell label="LONGITUDE" value={`${drone.longitude?.toFixed(4)}°`} small />
          </div>
          <div className="flex flex-col items-end">
            <span className="text-[11px] font-bold uppercase tracking-widest text-outline">LINK</span>
            <span className={`font-mono text-xs ${statusTextClass}`}>{drone.linkQuality}%</span>
          </div>
        </div>
      </div>

      {/* Command Buttons */}
      <div className="flex gap-1.5">
        {[
          { action: 'arm', label: 'ARM', icon: 'verified', cls: 'hover:bg-secondary/20 hover:text-secondary hover:border-secondary/40' },
          { action: 'disarm', label: 'DISARM', icon: 'power_settings_new', cls: 'hover:bg-error/20 hover:text-error hover:border-error/40' },
          { action: 'rtl', label: 'RTL', icon: 'home', cls: 'hover:bg-tertiary/20 hover:text-tertiary hover:border-tertiary/40' },
          { action: 'land', label: 'LAND', icon: 'flight_land', cls: 'hover:bg-primary/20 hover:text-primary hover:border-primary/40' },
        ].map(({ action, label, icon, cls }) => (
          <button key={action} onClick={() => cmd(action)}
            className={`flex-1 bg-surface-container-high text-on-surface border border-outline-variant/30 py-1.5 rounded text-[10px] font-bold uppercase tracking-widest transition-colors flex items-center justify-center gap-1 ${cls}`}>
            <span className="material-symbols-outlined text-[14px]">{icon}</span>
            {label}
          </button>
        ))}
      </div>
    </div>
  )
}

function TelCell({ label, value, sub, valueClass = 'text-on-surface', small = false }) {
  return (
    <div className="flex flex-col">
      <span className="text-[11px] font-bold uppercase tracking-widest text-outline mb-0.5">{label}</span>
      <span className={`font-mono ${small ? 'text-xs' : 'text-sm'} font-medium ${valueClass}`}>{value}</span>
      {sub && <span className="font-mono text-[11px] text-on-surface-variant">{sub}</span>}
    </div>
  )
}

// ─── main Dashboard ──────────────────────────────────────────────────────────

export default function Dashboard() {
  const navigate = useNavigate()
  const [drones, setDrones] = useState({ 1: null, 2: null, 3: null, 4: null, 5: null })
  const [trails, setTrails] = useState({ 1: [], 2: [], 3: [], 4: [], 5: [] })
  const [logs, setLogs] = useState([
    { time: new Date().toLocaleTimeString('en-GB'), type: 'SYS', message: 'Swarm orchestration initialized. Network heartbeat OK.' }
  ])
  const [mapCenter] = useState([34.0522, -118.2437])
  const logsEndRef = useRef(null)
  const connectionRef = useRef(null)

  const addLog = useCallback((type, message, droneId = null) => {
    const time = new Date().toLocaleTimeString('en-GB')
    const prefix = droneId ? `[Drone #${droneId}] ` : ''
    setLogs(prev => [...prev.slice(-MAX_LOGS + 1), { time, type, message: prefix + message }])
  }, [])

  // SignalR connection
  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('http://localhost:5501/hubs/drone', {
        accessTokenFactory: () => getToken(),
      })
      .withAutomaticReconnect()
      .build()

    connection.on('ReceiveTelemetry', (data) => {
      setDrones(prev => ({ ...prev, [data.droneId]: data }))
      if (data.latitude !== 0 || data.longitude !== 0) {
        setTrails(prev => {
          const trail = prev[data.droneId] || []
          const last = trail[trail.length - 1]
          if (last && last[0] === data.latitude && last[1] === data.longitude) return prev
          return { ...prev, [data.droneId]: [...trail, [data.latitude, data.longitude]].slice(-MAX_TRAIL) }
        })
      }
    })

    connection.on('ReceiveEvent', (ev) => {
      addLog(ev.type, ev.message, ev.droneId || null)
    })

    connection.onreconnecting(() => addLog('WARN', 'SignalR reconnecting...'))
    connection.onreconnected(() => addLog('SYS', 'SignalR reconnected.'))
    connection.onclose(() => addLog('WARN', 'SignalR connection closed.'))

    connection.start()
      .then(() => addLog('SYS', 'Connected to AeroSwarm Hub.'))
      .catch(err => addLog('WARN', `Connection failed: ${err.message}`))

    connectionRef.current = connection
    return () => { connection.stop() }
  }, [addLog])

  // Auto-scroll logs
  useEffect(() => {
    logsEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [logs])

  const sendCommand = useCallback(async (droneId, action) => {
    const token = getToken()
    try {
      addLog('CMD', `Sending ${action.toUpperCase()} to Drone #${droneId}...`)
      await axios.post(`${API}/drones/${droneId}/${action}`, {}, {
        headers: { Authorization: `Bearer ${token}` }
      })
      addLog('ACK', `Drone #${droneId} ${action.toUpperCase()} acknowledged.`)
    } catch (err) {
      addLog('WARN', `Command ${action} to Drone #${droneId} failed: ${err.message}`)
    }
  }, [addLog])

  const sendAll = useCallback(async (action) => {
    for (let id = 1; id <= 5; id++) {
      await sendCommand(id, action)
    }
  }, [sendCommand])

  const handleLogout = () => { logout(); navigate('/') }

  const logTypeStyle = {
    SYS: 'text-primary',
    WARN: 'text-tertiary',
    CMD: 'text-secondary',
    ACK: 'text-secondary',
    INFO: 'text-primary',
  }

  return (
    <div className="h-screen w-screen overflow-hidden flex flex-col bg-background text-on-surface" style={{ fontFamily: 'Inter' }}>

      {/* ── TOP BAR ── */}
      <header className="shrink-0 h-16 bg-surface/80 backdrop-blur-xl border-b border-outline-variant/30 flex justify-between items-center px-6">
        <div className="flex items-center gap-4">
          <span className="material-symbols-outlined text-primary text-2xl">hub</span>
          <h1 className="text-xl font-bold tracking-tight text-primary uppercase">AeroSwarm</h1>
          <span className="text-on-surface-variant text-sm ml-2 border-l border-outline-variant/30 pl-4">Multi-UAV Command Center</span>
        </div>
        <div className="flex items-center gap-2">
          {[
            { action: 'arm', label: 'ARM ALL', icon: 'verified', cls: 'bg-secondary text-on-secondary hover:bg-secondary-fixed' },
            { action: 'rtl', label: 'RTL ALL', icon: 'home', cls: 'bg-tertiary-container text-on-tertiary-container hover:bg-tertiary' },
            { action: 'land', label: 'EMERGENCY LAND ALL', icon: 'warning', cls: 'bg-error-container text-on-error-container hover:bg-error' },
            { action: 'disarm', label: 'DISARM ALL', icon: 'power_settings_new', cls: 'bg-error text-on-error hover:bg-error/80 animate-pulse border border-error shadow-[0_0_15px_rgba(255,180,171,0.5)]' },
          ].map(({ action, label, icon, cls }) => (
            <button key={action} onClick={() => sendAll(action)}
              className={`px-3 py-1.5 rounded text-[11px] font-bold uppercase tracking-widest transition-colors flex items-center gap-1.5 ${cls}`}>
              <span className="material-symbols-outlined text-[14px]">{icon}</span>
              {label}
            </button>
          ))}
          <div className="w-px h-6 bg-outline-variant/30 mx-1" />
          <button onClick={handleLogout} className="text-on-surface-variant hover:text-error transition-colors p-2 rounded-full hover:bg-error/10" title="Logout">
            <span className="material-symbols-outlined text-[20px]">logout</span>
          </button>
        </div>
      </header>

      {/* ── MAIN AREA ── */}
      <main className="flex-1 flex overflow-hidden" style={{ minHeight: 0 }}>

        {/* Left: Map (60%) */}
        <section className="w-[60%] relative overflow-hidden" style={{ minHeight: 0 }}>
          <MapContainer
            center={mapCenter}
            zoom={13}
            style={{ position: 'absolute', inset: 0 }}
            zoomControl={false}
          >
            <TileLayer
              url="https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png"
              attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/">CARTO</a>'
              subdomains="abcd"
              maxZoom={19}
            />
            <MapCenterUpdater drones={drones} />

            {Object.entries(drones).map(([id, drone]) => {
              if (!drone || (drone.latitude === 0 && drone.longitude === 0)) return null
              const color = droneColor(drone)
              const icon = createDroneIcon(color, drone.heading)
              const trail = trails[id] || []

              return (
                <Fragment key={id}>
                  {trail.length > 1 && (
                    <Polyline
                      positions={trail}
                      pathOptions={{ color, weight: 2, opacity: 0.5, dashArray: '6 6' }}
                    />
                  )}
                  <Marker position={[drone.latitude, drone.longitude]} icon={icon}>
                    <Popup>
                      <div className="font-mono text-xs" style={{ color: '#d5e3fd', minWidth: 120 }}>
                        <div className="font-bold" style={{ color }}>DRONE #{id}</div>
                        <div>BAT: {drone.batteryPercent}% · {drone.batteryVoltage?.toFixed(1)}V</div>
                        <div>ALT: {drone.altitude?.toFixed(1)}m · {drone.speed?.toFixed(1)} m/s</div>
                        <div>{drone.mode} | {drone.isArmed ? 'ARMED' : 'DISARMED'}</div>
                      </div>
                    </Popup>
                  </Marker>
                </Fragment>
              )
            })}
          </MapContainer>

          {/* Map HUD overlay */}
          <div className="absolute top-4 left-4 z-[1000] flex flex-col gap-2">
            <div className="glass-panel p-2 rounded flex flex-col gap-1">
              {[['add', 'zoom-in'], ['remove', 'zoom-out']].map(([icon]) => (
                <button key={icon} className="text-on-surface hover:text-primary p-1 bg-surface-container-high/50 rounded">
                  <span className="material-symbols-outlined text-xl">{icon}</span>
                </button>
              ))}
            </div>
          </div>

          {/* Footer telemetry overlay */}
          <div className="absolute bottom-4 right-4 z-[1000] glass-panel p-3 rounded flex gap-6 text-xs text-on-surface-variant font-mono">
            <div className="flex flex-col">
              <span className="text-outline text-[10px] uppercase tracking-widest">Wind Speed</span>
              <span>12.4 kt NE</span>
            </div>
            <div className="flex flex-col">
              <span className="text-outline text-[10px] uppercase tracking-widest">Data Link</span>
              <span className="text-secondary">98% STABLE</span>
            </div>
            <div className="flex flex-col">
              <span className="text-outline text-[10px] uppercase tracking-widest">GPS Sats</span>
              <span>{Object.values(drones).filter(Boolean).length * 3 + 6} Active</span>
            </div>
          </div>
        </section>

        {/* Right: Telemetry Cards (40%) */}
        <section className="w-[40%] bg-surface-container-lowest border-l border-outline-variant/20 p-3 flex flex-col gap-3 overflow-y-auto" style={{ minHeight: 0 }}>
          {[1, 2, 3, 4, 5].map(id => (
            <DroneTelemetryCard
              key={id}
              droneId={id}
              drone={drones[id]}
              onCommand={sendCommand}
            />
          ))}
        </section>
      </main>

      {/* ── BOTTOM: Event Logs ── */}
      <footer className="shrink-0 h-[15%] min-h-[120px] bg-black border-t border-outline-variant/40 p-3 overflow-y-auto font-mono text-xs">
        <div className="sticky top-0 bg-black border-b border-outline-variant/30 pb-1 mb-2 flex justify-between items-center">
          <span className="text-outline text-[11px] uppercase tracking-widest font-bold">System Event Logs</span>
          <div className="flex gap-2">
            <button onClick={() => setLogs([])} className="text-outline hover:text-primary" title="Clear logs">
              <span className="material-symbols-outlined text-[14px]">delete_sweep</span>
            </button>
          </div>
        </div>
        <ul className="flex flex-col gap-1 text-on-surface-variant">
          {logs.map((log, i) => (
            <li key={i} className="flex gap-3">
              <span className="text-outline shrink-0">[{log.time}]</span>
              <span className={`shrink-0 ${logTypeStyle[log.type] || 'text-primary'}`}>{log.type}:</span>
              <span>{log.message}</span>
            </li>
          ))}
        </ul>
        <div className="mt-2 flex gap-1 items-center text-outline">
          <span>&gt;</span>
          <span className="w-2 h-4 bg-primary animate-pulse inline-block" />
        </div>
        <div ref={logsEndRef} />
      </footer>
    </div>
  )
}
