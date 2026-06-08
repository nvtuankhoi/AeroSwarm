import { useEffect, useState, useRef, useCallback, Fragment } from 'react'
import { useNavigate } from 'react-router-dom'
import { MapContainer, TileLayer, Marker, CircleMarker, Polyline, Popup, useMap, useMapEvents } from 'react-leaflet'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import { HubConnectionBuilder } from '@microsoft/signalr'
import axios from 'axios'
import { getToken, logout } from '../services/authService'
import { fetchConfig, sendSetHome } from '../services/swarmService'
import MissionPlanner from './MissionPlanner'

const API = import.meta.env.VITE_API_URL || 'http://localhost:5501/api'
const MAX_TRAIL = 20
const MAX_LOGS = 100
const DEFAULT_DRONE_IDS = [1, 2, 3, 4, 5]

// ─── helpers ────────────────────────────────────────────────────────────────

function headingToDir(h) {
  const d = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW']
  return d[Math.round((h % 360) / 45) % 8]
}

function droneColor(drone) {
  if (!drone || !drone.isArmed) return '#ffb4ab' // error/red
  if (drone.mode === 'GUIDED') return '#4ae176'   // secondary/green
  if (drone.mode === 'LOITER') return '#ffb95f'   // tertiary/orange
  if (drone.mode === 'AUTO') return '#e88af0'     // mission/purple
  return '#adc6ff'                                 // primary/blue
}

function batteryIcon(pct) {
  if (pct > 75) return 'battery_full'
  if (pct > 50) return 'battery_5_bar'
  if (pct > 25) return 'battery_3_bar'
  return 'battery_1_bar'
}

function createDroneIcon(color, heading, isSelected = false) {
  const ring = isSelected
    ? `<circle cx="16" cy="16" r="15" fill="none" stroke="${color}" stroke-width="1.5" opacity="0.9" stroke-dasharray="4 3"/>`
    : ''
  const svg = `
    <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
      ${ring}
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
          <feGaussianBlur stdDeviation="${isSelected ? 3 : 2}" result="coloredBlur"/>
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

// Listens for map clicks. Mode precedence:
//   planning mode → add waypoint to mission planner
//   set-home mode → set home for all online drones, exit mode
//   drone-selected → send GOTO to that drone
function MapClickHandler({ planningMode, onAddWaypoint, setHomeMode, onSetHomeAll, selectedDroneId, onGoto }) {
  useMapEvents({
    click: (e) => {
      if (planningMode) {
        onAddWaypoint(e.latlng.lat, e.latlng.lng)
      } else if (setHomeMode) {
        onSetHomeAll(e.latlng.lat, e.latlng.lng)
      } else if (selectedDroneId) {
        onGoto(selectedDroneId, e.latlng.lat, e.latlng.lng)
      }
    }
  })
  return null
}

function DroneTelemetryCard({ droneId, drone, onCommand, onTakeoff, onSelect, isSelected }) {
  const isOffline = drone && drone.isOnline === false
  const colorClass = isOffline
    ? 'border-t-error text-error animate-pulse'
    : !drone || !drone.isArmed
    ? 'border-t-error text-error'
    : drone?.mode === 'GUIDED'
    ? 'border-t-secondary text-secondary'
    : drone?.mode === 'LOITER'
    ? 'border-t-tertiary text-tertiary'
    : drone?.mode === 'AUTO'
    ? 'border-t-[#e88af0] text-[#e88af0]'
    : 'border-t-primary text-primary'

  const statusTextClass = !drone || !drone.isArmed
    ? 'text-error'
    : drone?.mode === 'GUIDED'
    ? 'text-secondary'
    : drone?.mode === 'LOITER'
    ? 'text-tertiary'
    : drone?.mode === 'AUTO'
    ? 'text-[#e88af0]'
    : 'text-primary'

  const dotClass = !drone || !drone.isArmed
    ? 'bg-error shadow-[0_0_5px_#ffb4ab]'
    : drone?.mode === 'GUIDED'
    ? 'bg-secondary shadow-[0_0_5px_#4ae176]'
    : drone?.mode === 'LOITER'
    ? 'bg-tertiary shadow-[0_0_5px_#ffb95f]'
    : drone?.mode === 'AUTO'
    ? 'bg-[#e88af0] shadow-[0_0_5px_#e88af0]'
    : 'bg-primary shadow-[0_0_5px_#adc6ff]'

  const [isPending, setIsPending] = useState(false)
  const [lastCmd, setLastCmd] = useState(null)

  const isArmed = drone?.isArmed
  const mode = drone?.mode || 'STABILIZE'
  const inAuto = (mode === 'LAND' || mode === 'RTL') && drone?.altitude > 1.0
  const isFlying = isArmed && drone?.altitude > 1.0

  // Optimistic: if we just sent takeoff/guided, lock disarm until telemetry confirms
  const optimisticFlying = lastCmd === 'takeoff' || lastCmd === 'guided' || lastCmd === 'rtl' || lastCmd === 'land'

  const can = {
    arm: !isArmed && !inAuto && !optimisticFlying,
    disarm: isArmed && !isFlying && !inAuto && !optimisticFlying,
    rtl: isArmed && !inAuto,
    land: isArmed && !inAuto,
    guided: !inAuto && !optimisticFlying && mode !== 'GUIDED',
    takeoff: isArmed && !isFlying && !inAuto,
  }

  const cmd = async (action) => {
    if (isPending || !can[action]) return
    setIsPending(true)
    setLastCmd(action)
    try {
      await onCommand(droneId, action)
    } finally {
      setTimeout(() => {
        setIsPending(false)
        setLastCmd(null)
      }, 2500)
    }
  }

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
              {isOffline && (
                <span className="text-[10px] font-bold uppercase tracking-widest px-1.5 py-0.5 rounded bg-error/30 text-error border border-error/60 animate-pulse">
                  DROPOUT
                </span>
              )}
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

      {/* Command Buttons Row 1: ARM/DISARM/RTL/LAND */}
      <div className="flex gap-1.5">
        {[
          { action: 'arm', label: 'ARM', icon: 'verified', cls: 'hover:bg-secondary/20 hover:text-secondary hover:border-secondary/40' },
          { action: 'disarm', label: 'DISARM', icon: 'power_settings_new', cls: 'hover:bg-error/20 hover:text-error hover:border-error/40' },
          { action: 'rtl', label: 'RTL', icon: 'home', cls: 'hover:bg-tertiary/20 hover:text-tertiary hover:border-tertiary/40' },
          { action: 'land', label: 'LAND', icon: 'flight_land', cls: 'hover:bg-primary/20 hover:text-primary hover:border-primary/40' },
        ].map(({ action, label, icon, cls }) => {
          const ok = can[action] && !isPending
          return (
            <button key={action} onClick={() => ok && cmd(action)}
              disabled={!ok}
              className={`flex-1 bg-surface-container-high text-on-surface border border-outline-variant/30 py-1.5 rounded text-[10px] font-bold uppercase tracking-widest transition-colors flex items-center justify-center gap-1 ${cls} ${!ok ? 'opacity-40 cursor-not-allowed' : ''}`}>
              <span className="material-symbols-outlined text-[14px]">{icon}</span>
              {label}
            </button>
          )
        })}
      </div>

      {/* Command Buttons Row 2: GUIDED/TAKEOFF/SELECT (Click-to-Fly) */}
      <div className="flex gap-1.5">
        <button onClick={() => can.guided && !isPending && cmd('guided')}
          disabled={!can.guided || isPending}
          className={`flex-1 bg-surface-container-high text-on-surface border border-outline-variant/30 py-1.5 rounded text-[10px] font-bold uppercase tracking-widest transition-colors flex items-center justify-center gap-1 hover:bg-primary/20 hover:text-primary hover:border-primary/40 ${(!can.guided || isPending) ? 'opacity-40 cursor-not-allowed' : ''}`}>
          <span className="material-symbols-outlined text-[14px]">joystick</span>
          GUIDED
        </button>
        <button onClick={async () => {
            if (!can.takeoff || isPending) return
            setIsPending(true)
            try { await onTakeoff(droneId) } finally { setTimeout(() => setIsPending(false), 1200) }
          }}
          disabled={!can.takeoff || isPending}
          className={`flex-1 bg-surface-container-high text-on-surface border border-outline-variant/30 py-1.5 rounded text-[10px] font-bold uppercase tracking-widest transition-colors flex items-center justify-center gap-1 hover:bg-secondary/20 hover:text-secondary hover:border-secondary/40 ${(!can.takeoff || isPending) ? 'opacity-40 cursor-not-allowed' : ''}`}>
          <span className="material-symbols-outlined text-[14px]">flight_takeoff</span>
          TAKEOFF
        </button>
        <button
          onClick={() => onSelect(droneId)}
          className={`flex-1 border py-1.5 rounded text-[10px] font-bold uppercase tracking-widest transition-colors flex items-center justify-center gap-1
            ${isSelected
              ? 'bg-secondary/20 text-secondary border-secondary/60 shadow-[0_0_8px_rgba(74,225,118,0.3)]'
              : 'bg-surface-container-high text-on-surface border-outline-variant/30 hover:bg-tertiary/20 hover:text-tertiary hover:border-tertiary/40'
            }`}>
          <span className="material-symbols-outlined text-[14px]">{isSelected ? 'gps_fixed' : 'navigation'}</span>
          {isSelected ? 'TARGETING' : 'GOTO'}
        </button>
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
  const [droneIds, setDroneIds] = useState(DEFAULT_DRONE_IDS)
  const [drones, setDrones] = useState(() =>
    Object.fromEntries(DEFAULT_DRONE_IDS.map(id => [id, null])))
  const [trails, setTrails] = useState(() =>
    Object.fromEntries(DEFAULT_DRONE_IDS.map(id => [id, []])))
  const [logs, setLogs] = useState([
    { time: new Date().toLocaleTimeString('en-GB'), type: 'SYS', message: 'Swarm orchestration initialized. Network heartbeat OK.' }
  ])
  const [mapCenter] = useState([34.0522, -118.2437])
  const [selectedDroneId, setSelectedDroneId] = useState(null) // Click-to-Fly selection
  const [gotoAlt, setGotoAlt] = useState(10)                   // Target altitude (m)

  // Mission planner state
  const [missionPlannerOpen, setMissionPlannerOpen] = useState(false)
  const [missionWaypoints, setMissionWaypoints] = useState([]) // array of [lat, lon]

  // Set-home mode (right-click map or via button)
  const [setHomeMode, setSetHomeMode] = useState(false)

  const logsEndRef = useRef(null)
  const connectionRef = useRef(null)

  // ── derived map overlay values ──────────────────────────────────────────────
  const activeDrones = Object.values(drones).filter(Boolean)

  const avgLinkQuality = activeDrones.length > 0
    ? Math.round(activeDrones.reduce((sum, d) => sum + (d.linkQuality || 0), 0) / activeDrones.length)
    : null

  const totalGpsSats = activeDrones.reduce((sum, d) => sum + (d.gpsSatellites || 0), 0)

  // Use wind data from the first drone that reports it (environmental, same for swarm)
  const windDrone = activeDrones.find(d => d.windSpeed > 0)
  const windSpeedKt = windDrone ? (windDrone.windSpeed * 1.944).toFixed(1) : null
  const windDirStr  = windDrone ? headingToDir(windDrone.windDirectionDeg) : ''

  const addLog = useCallback((type, message, droneId = null) => {
    const time = new Date().toLocaleTimeString('en-GB')
    const prefix = droneId ? `[Drone #${droneId}] ` : ''
    setLogs(prev => [...prev.slice(-MAX_LOGS + 1), { time, type, message: prefix + message }])
  }, [])

  // Fetch swarm config from backend (drone count, IDs)
  useEffect(() => {
    let cancelled = false
    fetchConfig()
      .then(cfg => {
        if (cancelled) return
        const ids = cfg.droneIds ?? DEFAULT_DRONE_IDS
        setDroneIds(ids)
        setDrones(prev => {
          const next = Object.fromEntries(ids.map(id => [id, prev[id] ?? null]))
          return next
        })
        setTrails(prev => {
          const next = Object.fromEntries(ids.map(id => [id, prev[id] ?? []]))
          return next
        })
        addLog('SYS', `Swarm config loaded — ${ids.length} drone slot(s).`)
      })
      .catch(err => addLog('WARN', `Config fetch failed: ${err.message}`))
    return () => { cancelled = true }
  }, [addLog])

  // SignalR connection
  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(import.meta.env.VITE_HUB_URL || 'http://localhost:5501/hubs/drone', {
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

    connection.on('ReceiveDroneStatus', (status) => {
      setDrones(prev => {
        const cur = prev[status.droneId]
        if (!cur) return prev
        return { ...prev, [status.droneId]: { ...cur, isOnline: status.isOnline } }
      })
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
    const connected = Object.entries(drones)
      .filter(([, d]) => d !== null)
      .map(([id]) => Number(id))

    if (connected.length === 0) {
      addLog('WARN', `No drones connected — ${action.toUpperCase()} ALL aborted.`)
      return
    }
    for (const id of connected) {
      const d = drones[id]
      // RTL/LAND only makes sense for armed/flying drones
      if ((action === 'rtl' || action === 'land') && (!d?.isArmed || d?.altitude < 1.0)) {
        addLog('INFO', `Skipped ${action.toUpperCase()} for Drone #${id} — not flying.`)
        continue
      }
      await sendCommand(id, action)
    }
  }, [sendCommand, drones, addLog])

  const sendTakeoff = useCallback(async (droneId) => {
    const token = getToken()
    try {
      addLog('CMD', `TAKEOFF ${gotoAlt}m`, droneId)
      await axios.post(`${API}/drones/${droneId}/takeoff`,
        { altitude: gotoAlt },
        { headers: { Authorization: `Bearer ${token}` } })
      addLog('ACK', `TAKEOFF acknowledged`, droneId)
    } catch (err) {
      addLog('WARN', `TAKEOFF failed: ${err.message}`, droneId)
    }
  }, [addLog, gotoAlt])

  const sendGoto = useCallback(async (droneId, lat, lon) => {
    const token = getToken()
    try {
      addLog('CMD', `GOTO (${lat.toFixed(5)}, ${lon.toFixed(5)}) alt=${gotoAlt}m`, droneId)
      await axios.post(`${API}/drones/${droneId}/goto`,
        { lat, lon, alt: gotoAlt },
        { headers: { Authorization: `Bearer ${token}` } })
      addLog('ACK', `GOTO acknowledged`, droneId)
      setSelectedDroneId(null) // Deselect after sending goto
    } catch (err) {
      addLog('WARN', `GOTO failed: ${err.message}`, droneId)
    }
  }, [addLog, gotoAlt])

  // ── Mission planner handlers ──────────────────────────────────────────────
  const openMissionPlanner = useCallback(() => {
    setMissionPlannerOpen(true)
    setSetHomeMode(false)
    setSelectedDroneId(null)
  }, [])

  const closeMissionPlanner = useCallback(() => {
    setMissionPlannerOpen(false)
  }, [])

  const addMissionWaypoint = useCallback((lat, lon) => {
    setMissionWaypoints(prev => [...prev, [lat, lon]])
  }, [])

  const removeMissionWaypoint = useCallback((idx) => {
    setMissionWaypoints(prev => prev.filter((_, i) => i !== idx))
  }, [])

  const clearMissionWaypoints = useCallback(() => {
    setMissionWaypoints([])
  }, [])

  const handleMissionUploaded = useCallback(() => {
    // Reset trails so users see fresh tracks for the new mission
    setTrails(prev => Object.fromEntries(Object.keys(prev).map(id => [id, []])))
    setMissionWaypoints([])
  }, [])

  // ── Set Home handlers ─────────────────────────────────────────────────────
  const toggleSetHomeMode = useCallback(() => {
    setSetHomeMode(prev => !prev)
    setSelectedDroneId(null)
  }, [])

  const handleSetHomeAll = useCallback(async (lat, lon) => {
    const online = Object.values(drones).filter(d => d && d.isOnline !== false)
    if (online.length === 0) {
      addLog('WARN', 'No online drones — Set Home aborted.')
      setSetHomeMode(false)
      return
    }
    addLog('CMD', `Set Home (${lat.toFixed(5)}, ${lon.toFixed(5)}) → ${online.length} drone(s).`)
    for (const d of online) {
      try {
        await sendSetHome(d.droneId, lat, lon, 0)
        addLog('ACK', 'SET_HOME acknowledged', d.droneId)
      } catch (err) {
        addLog('WARN', `SET_HOME failed: ${err.message}`, d.droneId)
      }
    }
    setSetHomeMode(false)
  }, [drones, addLog])

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
          <button
            onClick={openMissionPlanner}
            className={`px-3 py-1.5 rounded text-[11px] font-bold uppercase tracking-widest transition-colors flex items-center gap-1.5 border ${
              missionPlannerOpen
                ? 'bg-secondary/30 text-secondary border-secondary/60'
                : 'bg-surface-container-high text-on-surface border-outline-variant/30 hover:bg-secondary/20 hover:text-secondary hover:border-secondary/40'
            }`}
            title="Plan a swarm mission (V-shape / line)"
          >
            <span className="material-symbols-outlined text-[14px]">route</span>
            PLAN MISSION
          </button>
          <button
            onClick={toggleSetHomeMode}
            className={`px-3 py-1.5 rounded text-[11px] font-bold uppercase tracking-widest transition-colors flex items-center gap-1.5 border ${
              setHomeMode
                ? 'bg-tertiary/30 text-tertiary border-tertiary/60 animate-pulse'
                : 'bg-surface-container-high text-on-surface border-outline-variant/30 hover:bg-tertiary/20 hover:text-tertiary hover:border-tertiary/40'
            }`}
            title="Click map to set home position for all online drones"
          >
            <span className="material-symbols-outlined text-[14px]">{setHomeMode ? 'my_location' : 'pin_drop'}</span>
            {setHomeMode ? 'CLICK MAP' : 'SET HOME'}
          </button>
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
            style={{ position: 'absolute', inset: 0, cursor: (missionPlannerOpen || selectedDroneId) ? 'crosshair' : undefined }}
            zoomControl={false}
          >
            <TileLayer
              url="https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png"
              attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/">CARTO</a>'
              subdomains="abcd"
              maxZoom={19}
            />
            <MapCenterUpdater drones={drones} />
            <MapClickHandler
              planningMode={missionPlannerOpen}
              onAddWaypoint={addMissionWaypoint}
              setHomeMode={setHomeMode}
              onSetHomeAll={handleSetHomeAll}
              selectedDroneId={selectedDroneId}
              onGoto={sendGoto}
            />

            {/* Mission planner waypoint preview */}
            {missionPlannerOpen && missionWaypoints.length > 0 && (
              <Polyline
                positions={missionWaypoints}
                pathOptions={{ color: '#4ae176', weight: 2, opacity: 0.8, dashArray: '4 6' }}
              />
            )}
            {missionPlannerOpen && missionWaypoints.map((wp, i) => (
              <CircleMarker
                key={`wp-${i}`}
                center={wp}
                radius={8}
                pathOptions={{ color: '#4ae176', fillColor: '#4ae176', fillOpacity: 0.6, weight: 2 }}
              >
                <Popup>
                  <div className="font-mono text-xs">
                    Leader WP #{i + 1}<br />
                    {wp[0].toFixed(5)}, {wp[1].toFixed(5)}
                  </div>
                </Popup>
              </CircleMarker>
            ))}

            {Object.entries(drones).map(([id, drone]) => {
              if (!drone || (drone.latitude === 0 && drone.longitude === 0)) return null
              const numId = Number(id)
              const color = droneColor(drone)
              const isSelected = selectedDroneId === numId
              const icon = createDroneIcon(color, drone.heading, isSelected)
              const trail = trails[id] || []

              return (
                <Fragment key={id}>
                  {trail.length > 1 && (
                    <Polyline
                      positions={trail}
                      pathOptions={{ color, weight: 2, opacity: 0.5, dashArray: '6 6' }}
                    />
                  )}
                  <Marker
                    position={[drone.latitude, drone.longitude]}
                    icon={icon}
                    eventHandlers={{
                      click: () => setSelectedDroneId(prev => prev === numId ? null : numId)
                    }}
                  >
                    <Popup>
                      <div className="font-mono text-xs" style={{ color: '#d5e3fd', minWidth: 130 }}>
                        <div className="font-bold" style={{ color }}>DRONE #{id}</div>
                        <div>BAT: {drone.batteryPercent}% · {drone.batteryVoltage?.toFixed(1)}V</div>
                        <div>ALT: {drone.altitude?.toFixed(1)}m · {drone.speed?.toFixed(1)} m/s</div>
                        <div>{drone.mode} | {drone.isArmed ? 'ARMED' : 'DISARMED'}</div>
                        <div style={{ marginTop: 4, color: '#4ae176', cursor: 'pointer' }}
                          onClick={() => setSelectedDroneId(numId)}>
                          ▶ SELECT FOR GOTO
                        </div>
                      </div>
                    </Popup>
                  </Marker>
                </Fragment>
              )
            })}
          </MapContainer>

          {/* Mission Planning mode indicator */}
          {missionPlannerOpen && (
            <div className="absolute top-4 left-1/2 -translate-x-1/2 z-[1000] glass-panel px-5 py-2 rounded-full flex items-center gap-3 font-mono text-sm pointer-events-auto border border-secondary/40 shadow-[0_0_12px_rgba(74,225,118,0.2)]">
              <span className="w-2 h-2 rounded-full bg-secondary animate-pulse shrink-0" />
              <span className="text-secondary font-bold uppercase tracking-widest text-xs">Planning Mode</span>
              <span className="text-on-surface-variant text-xs">Click on the map to add waypoints</span>
              <button
                onClick={() => setMissionPlannerOpen(false)}
                className="text-outline hover:text-error transition-colors ml-1"
                title="Close planner"
              >
                <span className="material-symbols-outlined text-[16px]">close</span>
              </button>
            </div>
          )}

          {/* GOTO mode indicator — shown when a drone is selected */}
          {selectedDroneId && (
            <div className="absolute top-4 left-1/2 -translate-x-1/2 z-[1000] glass-panel px-4 py-2 rounded-full flex items-center gap-3 font-mono text-sm pointer-events-auto">
              <span className="w-2 h-2 rounded-full bg-secondary animate-pulse shrink-0" />
              <span className="text-secondary font-bold">DRONE #{selectedDroneId}</span>
              <span className="text-on-surface-variant text-xs">SELECTED — click map to navigate</span>
              <div className="flex items-center gap-1.5 border-l border-outline-variant/30 pl-3">
                <span className="text-outline text-[10px] uppercase">ALT</span>
                <input
                  type="number"
                  value={gotoAlt}
                  onChange={e => setGotoAlt(Math.max(2, Math.min(120, Number(e.target.value))))}
                  onClick={e => e.stopPropagation()}
                  className="w-14 bg-surface-container text-on-surface text-xs font-mono px-2 py-1 rounded border border-outline-variant/30 outline-none focus:border-primary/60"
                  min={2} max={120}
                />
                <span className="text-outline text-[10px]">m</span>
              </div>
              <button
                onClick={() => setSelectedDroneId(null)}
                className="text-outline hover:text-error transition-colors ml-1"
                title="Cancel"
              >
                <span className="material-symbols-outlined text-[16px]">close</span>
              </button>
            </div>
          )}

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
              <span>{windSpeedKt ? `${windSpeedKt} kt ${windDirStr}` : 'N/A'}</span>
            </div>
            <div className="flex flex-col">
              <span className="text-outline text-[10px] uppercase tracking-widest">Data Link</span>
              {avgLinkQuality === null
                ? <span className="text-outline">NO SIGNAL</span>
                : <span className={avgLinkQuality >= 80 ? 'text-secondary' : avgLinkQuality >= 50 ? 'text-tertiary' : 'text-error'}>
                    {avgLinkQuality}% {avgLinkQuality >= 80 ? 'STABLE' : avgLinkQuality >= 50 ? 'DEGRADED' : 'POOR'}
                  </span>
              }
            </div>
            <div className="flex flex-col">
              <span className="text-outline text-[10px] uppercase tracking-widest">GPS Sats</span>
              <span>{activeDrones.length > 0 ? `${totalGpsSats} Active` : 'N/A'}</span>
            </div>
          </div>
        </section>

        {/* Right: Telemetry Cards (40%) */}
        <section className="w-[40%] bg-surface-container-lowest border-l border-outline-variant/20 p-3 flex flex-col gap-3 overflow-y-auto" style={{ minHeight: 0 }}>
          {droneIds.map(id => (
            <DroneTelemetryCard
              key={id}
              droneId={id}
              drone={drones[id]}
              onCommand={sendCommand}
              onTakeoff={sendTakeoff}
              onSelect={(dId) => setSelectedDroneId(prev => prev === dId ? null : dId)}
              isSelected={selectedDroneId === id}
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

      <MissionPlanner
        open={missionPlannerOpen}
        waypoints={missionWaypoints}
        onRemoveWaypoint={removeMissionWaypoint}
        onClear={clearMissionWaypoints}
        onClose={closeMissionPlanner}
        onUploaded={handleMissionUploaded}
        onLog={addLog}
      />
    </div>
  )
}
