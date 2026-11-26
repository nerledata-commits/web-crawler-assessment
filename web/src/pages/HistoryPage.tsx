import { useEffect, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { api, formatDate, type JobSummary, type PagedResult } from '../api'
import StatusBadge from '../components/StatusBadge'

const PAGE_SIZE = 20

export default function HistoryPage() {
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const page = Math.max(1, Number(params.get('page')) || 1)
  const [result, setResult] = useState<PagedResult<JobSummary> | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let stopped = false
    setLoading(true)
    setError(null)
    api.listJobs(page, PAGE_SIZE)
      .then((r) => !stopped && setResult(r))
      .catch((e: Error) => !stopped && setError(e.message))
      .finally(() => !stopped && setLoading(false))
    return () => {
      stopped = true
    }
  }, [page])

  const totalPages = result ? Math.max(1, Math.ceil(result.totalCount / PAGE_SIZE)) : 1

  return (
    <section>
      <h1>History</h1>
      {error && <p className="error-panel" role="alert">{error}</p>}
      {loading && !result && <p className="muted">Loading history…</p>}
      {result && result.items.length === 0 && (
        <p className="muted">No crawl jobs yet. <Link to="/">Start one.</Link></p>
      )}
      {result && result.items.length > 0 && (
        <>
          <table className="history">
            <thead>
              <tr>
                <th>URL</th>
                <th>Status</th>
                <th>Pages</th>
                <th>Created</th>
                <th>Completed</th>
              </tr>
            </thead>
            <tbody>
              {result.items.map((job) => (
                <tr key={job.jobId} onClick={() => navigate(`/jobs/${job.jobId}`)} className="clickable">
                  <td><Link to={`/jobs/${job.jobId}`}>{job.url}</Link></td>
                  <td><StatusBadge status={job.status} /></td>
                  <td>{job.pagesCrawled}</td>
                  <td>{formatDate(job.createdAt)}</td>
                  <td>{formatDate(job.completedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="pager">
            <button className="secondary" disabled={page <= 1 || loading} onClick={() => setParams({ page: String(page - 1) })}>
              Previous
            </button>
            <span>Page {page} of {totalPages} ({result.totalCount} jobs)</span>
            <button className="secondary" disabled={page >= totalPages || loading} onClick={() => setParams({ page: String(page + 1) })}>
              Next
            </button>
          </div>
        </>
      )}
    </section>
  )
}
