export type JobStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Canceled'

export interface JobSummary {
  jobId: string
  url: string
  maxDepth: number
  status: JobStatus
  createdAt: string
  startedAt: string | null
  completedAt: string | null
  failureReason: string | null
  pagesCrawled: number
  pagesQueued: number
}

export interface PageNode {
  pageId: number
  url: string
  depth: number
  httpStatusCode: number | null
  error: string | null
  outgoingLinkCount: number
  internalLinkCount: number
  domainLinkRatio: number
  children: PageNode[]
}

export interface JobTree {
  jobId: string
  status: JobStatus
  pageCount: number
  root: PageNode | null
}

export interface PageLink {
  url: string
  isInternal: boolean
}

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

export const isFinished = (status: JobStatus) =>
  status === 'Completed' || status === 'Failed' || status === 'Canceled'

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response
  try {
    response = await fetch(path, {
      ...init,
      headers: { 'Content-Type': 'application/json', ...init?.headers },
    })
  } catch {
    throw new ApiError(0, 'Cannot reach the API. Is it running?')
  }

  if (!response.ok) {
    throw new ApiError(response.status, await readProblem(response))
  }

  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

/** Turns an RFC 7807 problem details body into a readable message. */
async function readProblem(response: Response): Promise<string> {
  try {
    const problem = await response.json()
    const fieldErrors = problem.errors
      ? Object.values(problem.errors as Record<string, string[]>).flat().join(' ')
      : ''
    return fieldErrors || problem.detail || problem.title || `Request failed (${response.status})`
  } catch {
    return response.status === 404 ? 'Not found.' : `Request failed (${response.status})`
  }
}

export const api = {
  createJob: (url: string, maxDepth?: number) =>
    request<{ jobId: string }>('/api/jobs', {
      method: 'POST',
      body: JSON.stringify({ url, maxDepth }),
    }),
  getJob: (jobId: string) => request<JobSummary>(`/api/jobs/${jobId}`),
  getTree: (jobId: string) => request<JobTree>(`/api/jobs/${jobId}/tree`),
  getLinks: (jobId: string, pageId: number) =>
    request<PageLink[]>(`/api/jobs/${jobId}/pages/${pageId}/links`),
  listJobs: (page: number, pageSize: number) =>
    request<PagedResult<JobSummary>>(`/api/jobs?page=${page}&pageSize=${pageSize}`),
  cancelJob: (jobId: string) => request<void>(`/api/jobs/${jobId}/cancel`, { method: 'POST' }),
}

export const formatDate = (value: string | null) =>
  value ? new Date(value).toLocaleString() : '—'

export const formatRatio = (ratio: number) => `${(ratio * 100).toFixed(1)}%`
