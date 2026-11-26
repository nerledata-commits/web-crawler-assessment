import type { JobStatus } from '../api'

export default function StatusBadge({ status }: { status: JobStatus }) {
  return <span className={`badge badge-${status.toLowerCase()}`}>{status}</span>
}
