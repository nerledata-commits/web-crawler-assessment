import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api, formatDate, isFinished, type JobSummary, type JobTree } from '../api'
import StatusBadge from '../components/StatusBadge'
import PageTree from '../components/PageTree'

const POLL_INTERVAL_MS = 1500

export default function JobDetailsPage() {
  const { jobId = '' } = useParams()
  const [job, setJob] = useState<JobSummary | null>(null)
  const [tree, setTree] = useState<JobTree | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [treeError, setTreeError] = useState<string | null>(null)
  const [canceling, setCanceling] = useState(false)

  // Poll job status until it reaches a terminal state, then load the tree once.
  useEffect(() => {
    let stopped = false
    let timer: ReturnType<typeof setTimeout>

    const poll = async () => {
      try {
        const current = await api.getJob(jobId)
        if (stopped) return
        setJob(current)
        setError(null)
        if (isFinished(current.status)) {
          loadTree()
          return
        }
      } catch (e) {
        if (stopped) return
        setError((e as Error).message)
        if ((e as { status?: number }).status === 404) return
      }
      timer = setTimeout(poll, POLL_INTERVAL_MS)
    }

    const loadTree = async () => {
      try {
        const result = await api.getTree(jobId)
        if (!stopped) setTree(result)
      } catch (e) {
        if (!stopped) setTreeError((e as Error).message)
      }
    }

    setJob(null)
    setTree(null)
    poll()
    return () => {
      stopped = true
      clearTimeout(timer)
    }
  }, [jobId])

  const cancel = useCallback(async () => {
    setCanceling(true)
    try {
      await api.cancelJob(jobId)
      setJob(await api.getJob(jobId))
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setCanceling(false)
    }
  }, [jobId])

  if (!job) {
    return error ? <ErrorPanel message={error} /> : <p className="muted">Loading job…</p>
  }

  const running = !isFinished(job.status)
  const percent = job.pagesQueued > 0 ? Math.round((job.pagesCrawled / job.pagesQueued) * 100) : 0

  return (
    <section>
      <h1>Job details</h1>
      {error && <ErrorPanel message={`${error} (retrying…)`} />}

      <dl className="details">
        <dt>Job ID</dt><dd><code>{job.jobId}</code></dd>
        <dt>URL</dt><dd><a href={job.url} target="_blank" rel="noreferrer">{job.url}</a></dd>
        <dt>Status</dt><dd><StatusBadge status={job.status} /></dd>
        <dt>Max depth</dt><dd>{job.maxDepth}</dd>
        <dt>Created</dt><dd>{formatDate(job.createdAt)}</dd>
        <dt>Started</dt><dd>{formatDate(job.startedAt)}</dd>
        <dt>Completed</dt><dd>{formatDate(job.completedAt)}</dd>
        {job.failureReason && (<><dt>Failure reason</dt><dd className="error-text">{job.failureReason}</dd></>)}
        <dt>Pages</dt><dd>{job.pagesCrawled} crawled / {job.pagesQueued} discovered</dd>
      </dl>

      {running && (
        <div className="progress-area">
          <progress value={job.status === 'Pending' ? undefined : percent} max={100} />
          <span className="muted">
            {job.status === 'Pending' ? 'Waiting for a worker…' : `Crawling… ${percent}% of discovered pages`}
          </span>
          <button onClick={cancel} disabled={canceling} className="secondary">
            {canceling ? 'Canceling…' : 'Cancel job'}
          </button>
        </div>
      )}

      {!running && (
        <>
          <h2>Discovered pages</h2>
          {treeError && <ErrorPanel message={treeError} />}
          {!tree && !treeError && <p className="muted">Loading results…</p>}
          {tree && !tree.root && <p className="muted">No pages were crawled.</p>}
          {tree?.root && (
            <>
              <p className="muted">
                {tree.pageCount} pages. Domain Link Ratio = links to the starting host / all outgoing links.
              </p>
              <PageTree jobId={job.jobId} root={tree.root} />
            </>
          )}
        </>
      )}

      <p><Link to="/history">← Back to history</Link></p>
    </section>
  )
}

function ErrorPanel({ message }: { message: string }) {
  return <p className="error-panel" role="alert">{message}</p>
}
