import { useState } from 'react'
import { BrowserRouter, Routes, Route } from 'react-router-dom'
import './App.css'
import { Home } from './pages/Home'
import { GiftExchangeDetail } from './pages/GiftExchangeDetail'
import { ParticipantGiftExchange } from './pages/ParticipantGiftExchange'
import { AuthCallback } from './pages/AuthCallback'
import { SignIn } from './components/SignIn'
import { getSession, signOut, Session } from './auth'

function App() {
  const [session, setSession] = useState<Session | null>(() => getSession())

  const handleSignedIn = () => setSession(getSession())

  const handleSignOut = () => {
    signOut()
    setSession(null)
  }

  return (
    <BrowserRouter>
      <Routes>
        <Route path="/auth" element={<AuthCallback onSignedIn={handleSignedIn} />} />
        <Route
          path="/"
          element={session ? <Home userEmail={session.email} onSignOut={handleSignOut} /> : <SignIn />}
        />
        <Route
          path="/gift-exchange/:hatId"
          element={
            session
              ? <GiftExchangeDetail userEmail={session.email} onSignOut={handleSignOut} />
              : <SignIn />
          }
        />
        <Route
          path="/participating/:hatId"
          element={
            session
              ? <ParticipantGiftExchange userEmail={session.email} onSignOut={handleSignOut} />
              : <SignIn />
          }
        />
        <Route path="*" element={<SignIn />} />
      </Routes>
    </BrowserRouter>
  )
}

export default App
