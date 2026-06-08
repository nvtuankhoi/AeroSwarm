import { useEffect, useState, useRef, Fragment } from 'react'
import { useNavigate } from 'react-router-dom'
import { MapContainer, TileLayer, Marker, Polyline, Popup } from 'react-leaflet'
import L from 'leaflet'
import { HubConnectionBuilder } from '@microsoft/signalr'
import { getToken } from '../services/authService'
import { fetchConfig } from '../services/swarmService'

const HUB_URL = import.meta.env.VITE_HUB_URL || 'http://localhost:5501/hubs/drone'
const SITL_IDS = [4, 5, 6, 7, 8]
const MAX_TRAIL = 50

function createDroneIcon(color, heading) {
  const svg = `
    <svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" viewBox="0 0 28 28">
      <g transform="rotate(${heading}, 14, 14)">
        <polygon points="14,2 23,26 14,20 5,26"
          fill="${color}"
          stroke="rgba(0,0,0,0.6)"
          stroke-width="1.5"
        />
      </g>
    </svg>`
  return L.divIcon({ html: svg, className: '', iconSize: [28, 28], iconAnchor: [14, 14] })
}

function droneColor(drone) {
  if (!drone?.isArmed) return '#ef4444'
  if (drone.mode === 'GUIDED') return '#22c55e'
  if (drone.mode === 'LOITER') return '#f59e0b'
  if (drone.mode === 'AUTO') return '#a855f7'
  return '#3b82f6'
}

export default function Simulator() {
  const navigate = useNavigate()
  const [droneIds, setDroneIds] = useState([])
  const [drones, setDrones] = useState({})
  const [trails, setTrails] = useState({})
  const [filter, setFilter] = useState('all') // 'all' | 'sitl' | 'esp32'
  const [selectedId, setSelectedId] = useState(null)
  const connectionRef = useRef(null)

  useEffect(() => {
    fetchConfig().then(cfg => {
      const ids = cfg.droneIds || [1, 2, 3, 4, 5]
      setDroneIds(ids)
      setDrones(prev => Object.fromEntries(ids.map(id => [id, prev[id] || null])))
      setTrails(prev => Object.fromEntries(ids.map(id => [id, prev[id] || []])))
    }).catch(() => {})
  }, [])

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL, { accessTokenFactory: () => getToken() })
      .withAutomaticReconnect()
      .build()

    connection.on('ReceiveTelemetry', (data) => {
      setDrones(prev => ({ ...prev, [data.droneId]: data }))
      if (data.latitude !== 0 || data.longitude !== 0) {
        setTrails(prev => {
          const t = prev[data.droneId] || []
          return { ...prev, [data.droneId]: [...t, [data.latitude, data.longitude]].slice(-MAX_TRAIL) }
        })
      }
    })

    connection.start().catch(() => {})
    connectionRef.current = connection
    return () => { connection.stop() }
  }, [])

  const filteredIds = droneIds.filter(id => {
    if (filter === 'sitl') return SITL_IDS.includes(id)
    if (filter === 'esp32') return !SITL_IDS.includes(id)
    return true
  })

  const activeDrones = filteredIds.map(id => drones[id]).filter(Boolean)

  return (
    <div className="h-screen w-screen flex flex-col bg-[#0a0e1a] text-white" style={{ fontFamily: 'Inter, monospace' }}>
      {/* Header */}
      <header className="shrink-0 h-14 bg-[#0f172a]/90 border-b border-white/10 flex items-center justify-between px-5">
        <div className="flex items-center gap-3">
          <span className="material-symbols-outlined text-[#60a5fa] text-xl">airware</span>
          <h1 className="text-lg font-bold tracking-wide uppercase text-[#60a5fa]">AeroSwarm Simulator</h1>
          <span className="text-white/40 text-sm ml-2 border-l border-white/10 pl-3">Real-time SITL Swarm View</span>
        </div>
        <div className="flex items-center gap-3">
          <div className="flex bg-[#1e293b] rounded-lg p-0.5 border border-white/10">
            {[
              { key: 'all', label: 'ALL' },
              { key: 'sitl', label: 'SITL' },
              { key: 'esp32', label: 'ESP32' },
            ].map(({ key, label }) => (
              <button key={key} onClick={() => setFilter(key)}
                className={`px-3 py-1 rounded-md text-[11px] font-bold uppercase tracking-wider transition-all ${
                  filter === key ? 'bg-[#3b82f6] text-white' : 'text-white/50 hover:text-white'
                }`}>
                {label}
              </button>
            ))}
          </div>
          <button onClick={() => navigate('/dashboard')}
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-[#1e293b] border border-white/10 text-white/70 hover:text-white hover:bg-[#334155] transition-colors text-xs font-bold uppercase tracking-wider">
            <span className="material-symbols-outlined text-sm">dashboard</span>
            Dashboard
          </button>
        </div>
      </header>

      {/* Main */}
      <main className="flex-1 flex overflow-hidden">
        {/* Map */}
        <section className="flex-1 relative">
          <MapContainer center={[-35.363261, 149.165230]} zoom={16} style={{ position: 'absolute', inset: 0 }} zoomControl={false}>
            <TileLayer
              url="https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png"
              attribution='&copy; OSM &copy; CARTO'
              subdomains="abcd" maxZoom={19}
            />
            {filteredIds.map(id => {
              const drone = drones[id]
              if (!drone || (drone.latitude === 0 && drone.longitude === 0)) return null
              const color = droneColor(drone)
              const icon = createDroneIcon(color, drone.heading || 0)
              const trail = trails[id] || []
              return (
                <Fragment key={id}>
                  {trail.length > 1 && (
                    <Polyline positions={trail} pathOptions={{ color, weight: 2, opacity: 0.6, dashArray: '4 4' }} />
                  )}
                  <Marker position={[drone.latitude, drone.longitude]} icon={icon}
                    eventHandlers={{ click: () => setSelectedId(prev => prev === id ? null : id) }}>
                    <Popup>
                      <div className="font-mono text-xs text-[#cbd5e1] min-w-[140px]">
                        <div className="font-bold text-sm mb-1" style={{ color }}>DRONE #{id}</div>
                        <div>Mode: {drone.mode}</div>
                        <div>ARMED: {drone.isArmed ? 'YES' : 'NO'}</div>
                        <div>ALT: {drone.altitude?.toFixed(1)}m</div>
                        <div>SPD: {drone.speed?.toFixed(1)} m/s</div>
                        <div>BAT: {drone.batteryPercent}%</div>
                      </div>
                    </Popup>
                  </Marker>
                </Fragment>
              )
            })}
          </MapContainer>

          {/* Legend */}
          <div className="absolute bottom-4 left-4 z-[1000] bg-[#0f172a]/90 backdrop-blur border border-white/10 rounded-lg p-3 text-xs">
            <div className="text-white/40 uppercase tracking-widest font-bold mb-2 text-[10px]">Drone Types</div>
            <div className="flex flex-col gap-1.5">
              <div className="flex items-center gap-2"><span className="w-2.5 h-2.5 rounded-full bg-[#ef4444]"/>Disarmed</div>
              <div className="flex items-center gap-2"><span className="w-2.5 h-2.5 rounded-full bg-[#3b82f6]"/>Armed (STABILIZE)</div>
              <div className="flex items-center gap-2"><span className="w-2.5 h-2.5 rounded-full bg-[#22c55e]"/>Guided</div>
              <div className="flex items-center gap-2"><span className="w-2.5 h-2.5 rounded-full bg-[#f59e0b]"/>Loiter</div>
              <div className="flex items-center gap-2"><span className="w-2.5 h-2.5 rounded-full bg-[#a855f7]"/>Auto</div>
            </div>
          </div>
        </section>

        {/* Right Panel */}
        <aside className="w-80 bg-[#0f172a] border-l border-white/10 flex flex-col overflow-hidden">
          <div className="p-4 border-b border-white/10">
            <h2 className="text-sm font-bold uppercase tracking-widest text-white/40">Telemetry</h2>
            <div className="text-xs text-white/30 mt-1">{activeDrones.length} drone(s) active</div>
          </div>
          <div className="flex-1 overflow-y-auto p-3 space-y-2">
            {filteredIds.map(id => {
              const d = drones[id]
              if (!d) return (
                <div key={id} className="bg-[#1e293b]/50 rounded-lg p-3 border border-white/5">
                  <div className="text-xs font-bold text-white/30">DRONE #{id}</div>
                  <div className="text-[10px] text-white/20 mt-1">OFFLINE</div>
                </div>
              )
              const isSel = selectedId === id
              const color = droneColor(d)
              return (
                <div key={id}
                  onClick={() => setSelectedId(prev => prev === id ? null : id)}
                  className={`rounded-lg p-3 border cursor-pointer transition-all ${
                    isSel ? 'bg-[#1e293b] border-[#3b82f6]/50 shadow-[0_0_12px_rgba(59,130,246,0.15)]' : 'bg-[#1e293b]/40 border-white/5 hover:border-white/10'
                  }`}>
                  <div className="flex justify-between items-start mb-2">
                    <div className="flex items-center gap-2">
                      <span className="w-2 h-2 rounded-full" style={{ background: color, boxShadow: `0 0 6px ${color}` }} />
                      <span className="text-xs font-bold font-mono">DRONE #{id}</span>
                      <span className="text-[9px] px-1.5 py-0.5 rounded bg-white/5 text-white/40 uppercase">
                        {SITL_IDS.includes(id) ? 'SITL' : 'ESP32'}
                      </span>
                    </div>
                    <span className={`text-[10px] font-bold uppercase ${d.isArmed ? 'text-green-400' : 'text-red-400'}`}>
                      {d.isArmed ? 'ARMED' : 'DISARMED'}
                    </span>
                  </div>
                  <div className="grid grid-cols-2 gap-x-3 gap-y-1 text-[11px] font-mono text-white/60">
                    <div>Mode: <span className="text-white/80">{d.mode}</span></div>
                    <div>Alt: <span className="text-white/80">{d.altitude?.toFixed(1)}m</span></div>
                    <div>Spd: <span className="text-white/80">{d.speed?.toFixed(1)} m/s</span></div>
                    <div>Hdg: <span className="text-white/80">{Math.round(d.heading || 0)}°</span></div>
                    <div>Lat: <span className="text-white/80">{d.latitude?.toFixed(5)}</span></div>
                    <div>Lon: <span className="text-white/80">{d.longitude?.toFixed(5)}</span></div>
                    <div className="col-span-2 flex items-center gap-1">
                      <span className="material-symbols-outlined text-[12px] text-white/30">
                        {d.batteryPercent > 75 ? 'battery_full' : d.batteryPercent > 50 ? 'battery_5_bar' : d.batteryPercent > 25 ? 'battery_3_bar' : 'battery_1_bar'}
                      </span>
                      <span className={d.batteryPercent < 25 ? 'text-red-400' : d.batteryPercent < 50 ? 'text-amber-400' : 'text-white/80'}>
                        {d.batteryPercent}% · {d.batteryVoltage?.toFixed(1)}V
                      </span>
                    </div>
                  </div>
                </div>
              )
            })}
          </div>
        </aside>
      </main>
    </div>
  )
}
