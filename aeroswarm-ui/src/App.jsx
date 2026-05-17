import { Component } from 'react'
import { Routes, Route, Navigate } from 'react-router-dom'
import Login from './components/Login'
import Dashboard from './components/Dashboard'
import { isAuthenticated } from './services/authService'

function PrivateRoute({ children }) {
  return isAuthenticated() ? children : <Navigate to="/" replace />
}

class ErrorBoundary extends Component {
  constructor(props) {
    super(props)
    this.state = { error: null }
  }
  static getDerivedStateFromError(error) {
    return { error }
  }
  render() {
    if (this.state.error) {
      return (
        <div style={{ background: '#051426', color: '#ffb4ab', fontFamily: 'monospace', padding: 32, minHeight: '100vh' }}>
          <h2 style={{ color: '#adc6ff', marginBottom: 16 }}>⚠ SYSTEM FAULT — RENDER ERROR</h2>
          <pre style={{ whiteSpace: 'pre-wrap', color: '#ffb4ab', fontSize: 13 }}>
            {this.state.error.toString()}
            {'\n\n'}
            {this.state.error.stack}
          </pre>
          <button
            onClick={() => { this.setState({ error: null }); window.location.href = '/' }}
            style={{ marginTop: 24, padding: '8px 16px', background: '#adc6ff', color: '#002e6a', border: 'none', borderRadius: 4, cursor: 'pointer', fontWeight: 'bold' }}
          >
            Return to Login
          </button>
        </div>
      )
    }
    return this.props.children
  }
}

export default function App() {
  return (
    <ErrorBoundary>
      <Routes>
        <Route path="/" element={<Login />} />
        <Route path="/dashboard" element={<PrivateRoute><Dashboard /></PrivateRoute>} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </ErrorBoundary>
  )
}
