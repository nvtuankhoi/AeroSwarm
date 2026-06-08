import { useEffect, useState, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { Canvas, useFrame } from '@react-three/fiber'
import { OrbitControls, Html, Grid } from '@react-three/drei'
import { HubConnectionBuilder } from '@microsoft/signalr'
import { getToken } from '../services/authService'
import { fetchConfig } from '../services/swarmService'
import * as THREE from 'three'

const HUB_URL = import.meta.env.VITE_HUB_URL || 'http://localhost:5501/hubs/drone'
const SITL_IDS = [4, 5, 6, 7, 8]

// CMAC center
const CENTER_LAT = -35.363261
const CENTER_LON = 149.165230
const M_PER_DEG_LAT = 110540
const M_PER_DEG_LON = 111320 * Math.cos(CENTER_LAT * Math.PI / 180)
const SCALE = 0.5 // 1 real meter = 0.5 Three.js units

function gpsTo3d(lat, lon, alt) {
  const x = (lon - CENTER_LON) * M_PER_DEG_LON * SCALE
  const z = -(lat - CENTER_LAT) * M_PER_DEG_LAT * SCALE
  const y = alt * SCALE
  return [x, y, z]
}

function droneColor(drone) {
  if (!drone?.isArmed) return '#ef4444'
  if (drone.mode === 'GUIDED') return '#22c55e'
  if (drone.mode === 'LOITER') return '#f59e0b'
  if (drone.mode === 'AUTO') return '#a855f7'
  return '#3b82f6'
}

// Animated drone model
function DroneEntity({ drone, targetPosition }) {
  const meshRef = useRef()
  const propRef1 = useRef()
  const propRef2 = useRef()
  const propRef3 = useRef()
  const propRef4 = useRef()
  const [currentPos] = useState(() => new THREE.Vector3(...targetPosition))

  const color = droneColor(drone)

  useFrame((_state, delta) => {
    if (!meshRef.current) return
    // Smooth lerp to target
    currentPos.lerp(new THREE.Vector3(...targetPosition), delta * 3)
    meshRef.current.position.copy(currentPos)
    // Rotate to heading
    const targetRotation = -(drone.heading || 0) * (Math.PI / 180)
    meshRef.current.rotation.y = THREE.MathUtils.lerp(meshRef.current.rotation.y, targetRotation, delta * 5)
    // Spin props if armed
    const spinSpeed = drone.isArmed ? 15 : 0
    ;[propRef1, propRef2, propRef3, propRef4].forEach(ref => {
      if (ref.current) ref.current.rotation.y += spinSpeed * delta
    })
  })

  return (
    <group ref={meshRef}>
      {/* Body */}
      <mesh>
        <boxGeometry args={[0.6, 0.15, 0.6]} />
        <meshStandardMaterial color={color} metalness={0.6} roughness={0.3} />
      </mesh>
      {/* Arms */}
      {[
        [0.5, 0, 0.5], [-0.5, 0, 0.5], [0.5, 0, -0.5], [-0.5, 0, -0.5]
      ].map((pos, i) => (
        <mesh key={i} position={pos}>
          <cylinderGeometry args={[0.03, 0.03, 0.8]} />
          <meshStandardMaterial color="#334155" />
          <group rotation={[Math.PI / 2, 0, 0]}>
            <mesh ref={[propRef1, propRef2, propRef3, propRef4][i]} position={[0, 0.05, 0]}>
              <boxGeometry args={[0.4, 0.02, 0.04]} />
              <meshStandardMaterial color="#94a3b8" transparent opacity={0.7} />
            </mesh>
          </group>
        </mesh>
      ))}
      {/* Label */}
      <Html distanceFactor={8} style={{ pointerEvents: 'none' }}>
        <div className="bg-black/70 backdrop-blur px-2 py-1 rounded border text-center" style={{ borderColor: color, transform: 'translate(-50%, -120%)' }}>
          <div className="text-[10px] font-bold font-mono" style={{ color }}>DRONE #{drone.droneId}</div>
          <div className="text-[9px] text-white/70 font-mono">{drone.altitude?.toFixed(1)}m · {drone.mode}</div>
        </div>
      </Html>
      {/* Shadow */}
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, -currentPos.y + 0.02, 0]}>
        <circleGeometry args={[0.5, 16]} />
        <meshBasicMaterial color="black" transparent opacity={0.2} />
      </mesh>
    </group>
  )
}

// Ground plane
function Ground() {
  return (
    <>
      <Grid
        position={[0, 0, 0]}
        args={[200, 200]}
        cellSize={2}
        cellThickness={0.5}
        cellColor="#1e3a5f"
        sectionSize={10}
        sectionThickness={1}
        sectionColor="#3b82f6"
        fadeDistance={100}
        fadeStrength={1}
        infiniteGrid
      />
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, -0.01, 0]}>
        <planeGeometry args={[500, 500]} />
        <meshStandardMaterial color="#0f172a" />
      </mesh>
    </>
  )
}

// Home base marker
function HomeBase() {
  return (
    <group position={[0, 0, 0]}>
      <mesh>
        <cylinderGeometry args={[1, 1, 0.1, 32]} />
        <meshStandardMaterial color="#f59e0b" emissive="#f59e0b" emissiveIntensity={0.5} />
      </mesh>
      <Html distanceFactor={10}>
        <div className="bg-amber-500/20 border border-amber-500/50 px-2 py-1 rounded text-[10px] font-bold text-amber-400 font-mono" style={{ transform: 'translate(-50%, -150%)' }}>
          HOME (CMAC)
        </div>
      </Html>
    </group>
  )
}

// Camera that follows selected drone
function CameraRig({ targetPosition }) {
  const controlsRef = useRef()
  useFrame(() => {
    if (controlsRef.current && targetPosition) {
      const [x, y, z] = targetPosition
      controlsRef.current.target.lerp(new THREE.Vector3(x, y, z), 0.05)
      controlsRef.current.update()
    }
  })
  return <OrbitControls ref={controlsRef} enableDamping maxPolarAngle={Math.PI / 2.1} minDistance={3} maxDistance={200} />
}

export default function Simulator3D() {
  const navigate = useNavigate()
  const [droneIds, setDroneIds] = useState([])
  const [drones, setDrones] = useState({})
  const [filter, setFilter] = useState('all')
  const [selectedId, setSelectedId] = useState(null)
  const connectionRef = useRef(null)

  useEffect(() => {
    fetchConfig().then(cfg => {
      const ids = cfg.droneIds || [1, 2, 3, 4, 5]
      setDroneIds(ids)
      setDrones(prev => Object.fromEntries(ids.map(id => [id, prev[id] || null])))
    }).catch(() => {})
  }, [])

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL, { accessTokenFactory: () => getToken() })
      .withAutomaticReconnect()
      .build()

    connection.on('ReceiveTelemetry', (data) => {
      setDrones(prev => ({ ...prev, [data.droneId]: data }))
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
  const selectedDrone = selectedId ? drones[selectedId] : null
  const cameraTarget = selectedDrone ? gpsTo3d(selectedDrone.latitude, selectedDrone.longitude, selectedDrone.altitude) : [0, 10, 0]

  return (
    <div className="h-screen w-screen flex flex-col bg-[#0a0e1a] text-white" style={{ fontFamily: 'Inter, monospace' }}>
      {/* Header */}
      <header className="shrink-0 h-14 bg-[#0f172a]/90 border-b border-white/10 flex items-center justify-between px-5">
        <div className="flex items-center gap-3">
          <span className="material-symbols-outlined text-[#60a5fa] text-xl">airware</span>
          <h1 className="text-lg font-bold tracking-wide uppercase text-[#60a5fa]">AeroSwarm 3D</h1>
          <span className="text-white/40 text-sm ml-2 border-l border-white/10 pl-3">Real-time SITL Simulator</span>
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
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-[#1e293b] border border-white/10 text-white/70 hover:text-white hover:bg-[#334155] transition-colors text-[11px] font-bold uppercase tracking-wider">
            <span className="material-symbols-outlined text-[14px]">dashboard</span>
            Dashboard
          </button>
        </div>
      </header>

      {/* Main: 3D Canvas + Side Panel */}
      <main className="flex-1 flex overflow-hidden">
        {/* 3D Canvas */}
        <section className="flex-1 relative">
          <Canvas camera={{ position: [20, 20, 20], fov: 60 }} shadows>
            <ambientLight intensity={0.4} />
            <directionalLight position={[50, 100, 50]} intensity={1} castShadow />
            <hemisphereLight args={['#87ceeb', '#1a3a1a', 0.3]} />

            <CameraRig targetPosition={cameraTarget} />
            <Ground />
            <HomeBase />

            {/* Sky gradient */}
            <color attach="background" args={['#0a1628']} />
            <fog attach="fog" args={['#0a1628', 50, 300]} />

            {activeDrones.map(drone => {
              const pos = gpsTo3d(drone.latitude || CENTER_LAT, drone.longitude || CENTER_LON, drone.altitude || 0)
              return (
                <DroneEntity
                  key={drone.droneId}
                  drone={drone}
                  targetPosition={pos}
                />
              )
            })}
          </Canvas>

          {/* Overlay: Scale info */}
          <div className="absolute bottom-4 left-4 bg-black/50 backdrop-blur px-3 py-2 rounded border border-white/10 text-[10px] font-mono text-white/50">
            <div>Center: CMAC ({CENTER_LAT}, {CENTER_LON})</div>
            <div>Scale: 1m = {SCALE} units</div>
            {selectedDrone && (
              <div className="mt-1 text-[#60a5fa]">Following: Drone #{selectedId}</div>
            )}
          </div>
        </section>

        {/* Right Panel */}
        <aside className="w-72 bg-[#0f172a] border-l border-white/10 flex flex-col overflow-hidden">
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
