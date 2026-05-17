import ReactDOM from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from './App'
import './index.css'

// NOTE: React.StrictMode is intentionally removed.
// react-leaflet v5 MapContainer uses a ref-based callback to initialize the Leaflet map.
// StrictMode's double-invocation of effects in React 19 calls map.remove() in cleanup
// but does NOT reset mapInstanceRef, so the second mount never re-creates the map,
// leaving MapContainer permanently blank. Remove StrictMode to prevent this.
ReactDOM.createRoot(document.getElementById('root')).render(
  <BrowserRouter>
    <App />
  </BrowserRouter>
)
