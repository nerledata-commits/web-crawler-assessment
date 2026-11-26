import { BrowserRouter, NavLink, Navigate, Route, Routes } from 'react-router-dom'
import StartCrawlPage from './pages/StartCrawlPage'
import JobDetailsPage from './pages/JobDetailsPage'
import HistoryPage from './pages/HistoryPage'

export default function App() {
  return (
    <BrowserRouter>
      <header className="app-header">
        <strong>Web Crawler</strong>
        <nav>
          <NavLink to="/" end>Start Crawl</NavLink>
          <NavLink to="/history">History</NavLink>
        </nav>
      </header>
      <main>
        <Routes>
          <Route path="/" element={<StartCrawlPage />} />
          <Route path="/jobs/:jobId" element={<JobDetailsPage />} />
          <Route path="/history" element={<HistoryPage />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </BrowserRouter>
  )
}
