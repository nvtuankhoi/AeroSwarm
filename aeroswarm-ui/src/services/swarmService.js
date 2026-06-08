import axios from 'axios'
import { getToken } from './authService'

const BASE_URL = 'http://localhost:5000/api'

function authHeaders() {
  return { headers: { Authorization: `Bearer ${getToken()}` } }
}

export async function fetchConfig() {
  const res = await axios.get(`${BASE_URL}/config`)
  return res.data // { droneCount, droneIds, udpPort, dropoutThresholdSec, lowVoltage, criticalVoltage }
}

export async function uploadSwarmMission({ formation, spacingM, leaderWaypoints }) {
  const res = await axios.post(
    `${BASE_URL}/missions/swarm`,
    { formation, spacingM, leaderWaypoints },
    authHeaders(),
  )
  return res.data
}

export async function listFlights(limit = 50) {
  const res = await axios.get(`${BASE_URL}/flights?limit=${limit}`, authHeaders())
  return res.data
}

export async function getFlight(id) {
  const res = await axios.get(`${BASE_URL}/flights/${id}`, authHeaders())
  return res.data
}

export async function sendTakeoff(droneId, altitude = 10) {
  const res = await axios.post(
    `${BASE_URL}/drones/${droneId}/takeoff`,
    { altitude },
    authHeaders(),
  )
  return res.data
}

export async function sendGoto(droneId, lat, lon, alt = 10) {
  const res = await axios.post(
    `${BASE_URL}/drones/${droneId}/goto`,
    { lat, lon, alt },
    authHeaders(),
  )
  return res.data
}

export async function sendSetHome(droneId, lat, lon, alt = 0) {
  const res = await axios.post(
    `${BASE_URL}/drones/${droneId}/sethome`,
    { lat, lon, alt },
    authHeaders(),
  )
  return res.data
}
