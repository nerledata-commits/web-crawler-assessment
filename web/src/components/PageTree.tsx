import { useState } from 'react'
import { api, formatRatio, type PageLink, type PageNode } from '../api'

/** Recursive tree of crawled pages. Children are expanded by default for the first two levels. */
export default function PageTree({ jobId, root }: { jobId: string; root: PageNode }) {
  return (
    <ul className="tree">
      <TreeNode jobId={jobId} node={root} />
    </ul>
  )
}

function TreeNode({ jobId, node }: { jobId: string; node: PageNode }) {
  const [expanded, setExpanded] = useState(node.depth < 2)
  const hasChildren = node.children.length > 0

  return (
    <li>
      <div className="tree-row">
        <button
          className="toggle"
          onClick={() => setExpanded(!expanded)}
          disabled={!hasChildren}
          aria-label={expanded ? 'Collapse' : 'Expand'}
        >
          {hasChildren ? (expanded ? '▾' : '▸') : '·'}
        </button>
        <a href={node.url} target="_blank" rel="noreferrer" className="tree-url">{node.url}</a>
        {node.error ? (
          <span className="tree-error" title={node.error}>{node.error}</span>
        ) : (
          <span className="tree-metrics">
            ratio <strong>{formatRatio(node.domainLinkRatio)}</strong>
            {' '}({node.internalLinkCount}/{node.outgoingLinkCount} links internal)
          </span>
        )}
        {node.outgoingLinkCount > 0 && <LinksToggle jobId={jobId} pageId={node.pageId} />}
      </div>
      {hasChildren && expanded && (
        <ul>
          {node.children.map((child) => (
            <TreeNode key={child.pageId} jobId={jobId} node={child} />
          ))}
        </ul>
      )}
    </li>
  )
}

/** Outgoing links are loaded on demand: a page can have hundreds of them. */
function LinksToggle({ jobId, pageId }: { jobId: string; pageId: number }) {
  const [links, setLinks] = useState<PageLink[] | null>(null)
  const [open, setOpen] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  const toggle = async () => {
    setOpen(!open)
    if (links || loading) return
    setLoading(true)
    setError(null)
    try {
      setLinks(await api.getLinks(jobId, pageId))
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setLoading(false)
    }
  }

  return (
    <>
      <button className="link-button" onClick={toggle}>{open ? 'hide links' : 'show links'}</button>
      {open && (
        <div className="links-panel">
          {loading && <span className="muted">Loading links…</span>}
          {error && <span className="error-text">{error}</span>}
          {links && (
            <ul>
              {links.map((link) => (
                <li key={link.url}>
                  <span className={link.isInternal ? 'tag-internal' : 'tag-external'}>
                    {link.isInternal ? 'internal' : 'external'}
                  </span>{' '}
                  {link.url}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </>
  )
}
