import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api'

export default function StartCrawlPage() {
  const navigate = useNavigate()
  const [url, setUrl] = useState('')
  const [maxDepth, setMaxDepth] = useState('2')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setSubmitting(true)
    setError(null)
    try {
      const { jobId } = await api.createJob(url.trim(), maxDepth === '' ? undefined : Number(maxDepth))
      navigate(`/jobs/${jobId}`)
    } catch (e) {
      setError((e as Error).message)
      setSubmitting(false)
    }
  }

  return (
    <section>
      <h1>Start a crawl</h1>
      <form onSubmit={submit} className="form">
        <label>
          Website URL
          <input
            type="url"
            required
            placeholder="https://example.com"
            value={url}
            onChange={(e) => setUrl(e.target.value)}
            autoFocus
          />
        </label>
        <label>
          Max depth (0–5, default 2)
          <input
            type="number"
            min={0}
            max={5}
            value={maxDepth}
            onChange={(e) => setMaxDepth(e.target.value)}
          />
        </label>
        <button type="submit" disabled={submitting || url.trim() === ''}>
          {submitting ? 'Starting…' : 'Start Crawl'}
        </button>
        {error && <p className="error-text" role="alert">{error}</p>}
      </form>
    </section>
  )
}
