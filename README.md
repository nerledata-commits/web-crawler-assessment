# Job-based Web Crawler

Submit a URL and the system crawls it asynchronously. A worker fetches the pages, parses their links and computes each page's **Domain Link Ratio**. Results are stored in SQL and shown in a React UI as a tree, together with a job history.

```
React (Vite) ──REST, polling──▶ Crawl API (Service A) ──CrawlJobRequested──▶ RabbitMQ ──▶ Crawl Worker (Service B)
                                     │                                                         │
                                     └──────────── SQL: Jobs, Pages, PageLinks ◀───────────────┘
```

| Component | Project | Responsibility |
|---|---|---|
| Crawl API | `src/Crawler.Api` | Creates jobs, publishes messages, serves status, tree, links and history. Swagger at `/swagger`. Health at `/health`. |
| Crawl Worker | `src/Crawler.Worker` | Consumes jobs, fetches pages, parses links, computes metrics, writes progress and results. Health at `/health`. |
| Core | `src/Crawler.Core` | Domain logic with no infrastructure dependencies: URL normalization, Domain Link Ratio, page analysis, the crawl algorithm (`CrawlJobProcessor`) and the ports it uses (`ICrawlStore`, `IPageFetcher`, `ILinkExtractor`, `IJobPublisher`). |
| Infrastructure | `src/Crawler.Infrastructure` | EF Core (PostgreSQL or SQLite), idempotent upserts, RabbitMQ topology, publisher and connection, HttpClient with resilience, AngleSharp parser, logging and health checks. |
| UI | `web` | React + TypeScript (Vite). Screens: Start Crawl, Job Details (polling, progress, tree), History (paginated). |

---

## How to run

### Option A: Docker Compose (as the assignment asks)

```bash
docker compose up --build
```

| URL | What |
|---|---|
| http://localhost:8080 | UI (nginx serves the React app and proxies `/api` to the API) |
| http://localhost:5080/swagger | API (Swagger) |
| http://localhost:5080/health, http://localhost:5081/health | API and worker health |
| http://localhost:15672 | RabbitMQ management (guest / guest) |

Compose runs PostgreSQL 16, RabbitMQ 3.13, the API, the worker and the UI. Startup order uses health checks: DB and broker, then the API (which creates the schema), then the worker.

> **Note on verification.** My development machine can't run Docker (a content filter blocks WSL). The compose stack is verified by the GitHub Actions job `compose-smoke-test`. It runs `docker compose up --build --wait`, checks both health endpoints, and crawls a real site through the UI's nginx proxy until the job is `Completed` (`scripts/smoke-test.sh`).

### Option B: Run without Docker (Windows, how this was developed)

Prerequisites: .NET SDK 8+ (8.0 is the target, and `RollForward=Major` lets it run on a newer runtime), Node 20+, and RabbitMQ on `localhost:5672` (the Windows installer needs Erlang).

```powershell
./run-local.ps1      # builds, then opens API (5080), Worker (5081) and UI (http://localhost:5173)
```

Locally the database is a **SQLite** file (`data/crawler.db`), so nothing extra needs installing. The provider is chosen in configuration (`Database:Provider` = `Sqlite` | `Postgres`). The same SQL runs on both because the upserts use `INSERT … ON CONFLICT`, which both support.

### Tests

```bash
dotnet test           # 60 tests: unit + integration (no broker or DB server needed)
cd web && npm run lint && npm run build
```

- **Unit tests:** URL normalization (relative, `../`, scheme-relative, fragments, default ports, case, rejected schemes), Domain Link Ratio, page analysis (de-duplication, self links, mailto/tel), and HTML link extraction.
- **Integration tests with local HTML fixtures** (`tests/Crawler.IntegrationTests/Fixtures/site`): the real fetcher, parser, processor and EF store run on SQLite, with a fake `HttpMessageHandler` serving the fixtures. They cover depth limit, ratios, non-HTML and 404 pages, the max-pages limit, a start page that fails, cancellation, **the same message delivered twice creating no duplicate rows**, and poison messages.
- **API tests** (`WebApplicationFactory`): validation errors, `202` + `Location`, broker down → `503` and the job marked `Failed`, history ordering and paging, the tree, cancel `202`/`409`, and `404`s.
- `src/Crawler.Api/Crawler.Api.http` holds ready-made requests for the VS Code REST Client and Visual Studio.

---

## API

| Method | Route | Description |
|---|---|---|
| `POST` | `/api/jobs` | `{ "url": string, "maxDepth"?: 0-5 (default 2) }` → `202 { "jobId" }`. `400` for a non-http(s) or relative URL. `503` if the broker is down. |
| `GET` | `/api/jobs/{jobId}` | Status, timestamps (created, started, completed), failure reason, progress (`pagesCrawled`, `pagesQueued`). |
| `GET` | `/api/jobs/{jobId}/tree` | Pages nested by the page they were first discovered from, with ratio and link counts per page. A partial tree is available while the job runs. |
| `GET` | `/api/jobs/{jobId}/pages/{pageId}/links` | A page's outgoing links (internal or external). Loaded on demand by the UI. |
| `GET` | `/api/jobs?page=1&pageSize=20` | History, most recent first. `pageSize` is at most 100. |
| `POST` | `/api/jobs/{jobId}/cancel` | Optional feature, implemented. `202`, or `409` if the job has already finished. |

Errors use RFC 7807 problem details. Every response carries `X-Correlation-Id`: the caller's value if one was sent, otherwise a generated one. The ID travels in the message and appears in the worker's logs.

---

## Crawling rules and assumptions

- **Normalization** (`UrlNormalizer`):
  - Resolve against the page URL, or `<base href>` if present (after redirects).
  - Accept only `http`/`https`.
  - Lower-case the scheme and host.
  - Drop the default port and the **fragment**.
  - Keep the path, including any trailing slash, and the query unchanged.
  - Reject URLs longer than 2048 characters.
- **Outgoing links** of a page are its distinct normalized `<a href>` and `<area href>` targets. `mailto:`, `tel:`, `javascript:` and other schemes are left out of both the numerator and the denominator. A link that normalizes to the page itself (`#top`) is not counted as outgoing.
- **Starting domain** is the host of the job URL, compared exactly and case-insensitively. `www.example.com` and `example.com` count as different domains.
- **Domain Link Ratio** = internal outgoing links / all outgoing links. It is `0` when the page has no outgoing links.
- **What gets crawled:** breadth-first, only links on the starting host, up to `maxDepth` (the start page is depth 0, default 2) and **`Crawl:MaxPagesPerJob` (default 200)**. External links are recorded and counted but not fetched. A URL is fetched once per job.
- **HTML only:** responses whose `Content-Type` isn't `text/html` or `application/xhtml+xml` are recorded as pages with the note `Skipped: content type … is not HTML`. Their bodies aren't downloaded (`ResponseHeadersRead`). HTML is read up to 2 MB.
- **Errors per page** (404, DNS failure, timeout after retries) are recorded on the page, and the crawl continues. If the **start page** fails, the job is `Failed` with the reason.
- **HTTP resilience:** each attempt times out after 10 s, with 2 retries using exponential backoff for 5xx, 408, 429, network errors and timeouts. The total budget per request is 35 s. At most 5 redirects are followed. There is also a circuit breaker (`Microsoft.Extensions.Http.Resilience`).

---

## Event-driven design

### Message schema

Queue `crawl.jobs`. JSON (camelCase), persistent, published with **publisher confirms**.

```json
{
  "messageId": "6f1c…",                // unique per publish
  "jobId": "0b5e…",                    // the only field the worker trusts; it re-reads the job from the DB
  "url": "https://example.com/",
  "maxDepth": 2,
  "correlationId": "demo-123",
  "requestedAt": "2026-11-26T10:00:00Z"
}
```

AMQP properties: `message_id`, `correlation_id`, `type = CrawlJobRequested.v1`, `content_type = application/json`, `delivery_mode = 2`. Header: `x-attempt` (starts at 1).

### Topology

```
crawl.jobs ──(reject, requeue=false)──▶ crawl.jobs.dlq
    ▲
    └──(TTL 10 s expires)── crawl.jobs.retry ◀──(worker republishes with x-attempt+1)
```

### Idempotency (at-least-once delivery)

The worker acks **only after** the job has been processed. Any message can therefore arrive twice, and processing it again is safe:

1. **Unique keys:** `Pages (JobId, Url)` and `PageLinks (PageId, Url)`, where `Url` is normalized.
2. **Upserts:** pages use `INSERT … ON CONFLICT (JobId, Url) DO UPDATE` (fetch results are refreshed; the parent and depth from first discovery are kept). Links use `INSERT … ON CONFLICT DO NOTHING`.
3. **Guarded state changes:** `Pending|Running → Running`, `Running → Completed`, and `Pending|Running → Failed|Canceled` are each a conditional `UPDATE … WHERE Status IN (…)`. A late duplicate can never move a finished or canceled job back.
4. **A duplicate of a finished job** is acked without any work.
5. **Progress counters** are *set* to absolute values, not incremented, so replaying never double-counts.

The test `Same_message_delivered_twice_does_not_duplicate_pages_or_links` checks this with row counts.

### Retry policy: what counts as transient

| Failure | Handling |
|---|---|
| A single page fails (HTTP error, timeout, DNS, non-HTML) | **Not a message failure.** The HttpClient retries it, then the error is recorded on the page and the crawl continues. |
| Any other exception while processing (database unavailable, deadlock, broker hiccup…) | **Transient.** The message is republished to `crawl.jobs.retry` with `x-attempt + 1` and the original is acked. After 10 s the TTL sends it back to `crawl.jobs`. At most **3 attempts** (`RabbitMq:MaxDeliveryAttempts`). |
| Worker shutting down mid-job | `nack(requeue: true)`. Another delivery resumes the job idempotently. |

### Dead-letter queue (`crawl.jobs.dlq`)

Messages end up there in these cases:

- **Poison:** the body isn't valid JSON, has an empty `jobId`, or refers to a job that doesn't exist. These go straight to the DLQ with no retries.
- **Retries exhausted:** the 3rd attempt failed. The job is also marked `Failed` with the reason.

The DLQ is durable and is inspected or replayed by hand (RabbitMQ management UI: move the messages back to `crawl.jobs`). Because processing is idempotent, replaying is safe.

---

## Data model and performance

```
Jobs      (Id PK uuid, Url, MaxDepth, Status, CreatedAt, StartedAt, CompletedAt, FailureReason,
           PagesCrawled, PagesQueued, CorrelationId)
           IX Jobs(CreatedAt DESC)                     -- history: ORDER BY CreatedAt DESC with paging
Pages     (Id PK bigint, JobId FK, Url, ParentPageId FK (tree edge), Depth, HttpStatusCode, ContentType,
           OutgoingLinkCount, InternalLinkCount, DomainLinkRatio, Error, CrawledAt)
           UX Pages(JobId, Url)                        -- idempotency + "all pages of a job"
PageLinks (Id PK bigint, JobId, PageId FK, Url, IsInternal)
           UX PageLinks(PageId, Url)                   -- idempotency + "links of a page"
```

- **Edges:** `Pages.ParentPageId` is the parent → child edge of the discovery tree, which the UI shows. `PageLinks` holds every outgoing edge of a page, including external links and links to pages already seen, so the full link graph is kept.
- **Precomputed metrics:** the ratio and link counts are stored on the page, so building the tree is one indexed query (`WHERE JobId = ?`) plus grouping in memory. That is O(pages) with no N+1 queries.
- **Links are loaded lazily** per page, because they are the largest table and the tree doesn't need them.
- **Batched inserts:** links are inserted in batches of 200 rows per statement. A page stores at most 1000 links (`Crawl:MaxLinksPerPage`).
- **Status** is stored as text for readability, and timestamps are UTC.
- **History** uses offset paging on an index. Keyset paging (`WHERE CreatedAt < @cursor`) would be the next step for very large tables.
- **Schema creation** uses `EnsureCreated` at startup, with retries. Production would use EF migrations (see *Next steps*).

---

## Observability

- **Structured logs** through `Microsoft.Extensions.Logging`: JSON console (`AddJsonConsole`, with scopes) in Docker, single-line text in Development. Every worker log line carries a scope with `JobId`, `CorrelationId`, `MessageId` and `Attempt`. API requests carry `CorrelationId`. Examples: `Crawled {Url} depth {Depth} status {StatusCode}: {LinkCount} links, ratio {DomainLinkRatio}` and `Attempt {Attempt}/{MaxAttempts} failed; retrying`.
- **Health:** `/health` on both services checks the database (`DbContext`) and the RabbitMQ connection, returning JSON with each check. Compose uses these endpoints for its health checks.

---

## What I did first, what I cut, what's next

**Built first:**

1. **Domain rules and their tests:** normalization and Domain Link Ratio. Correctness is 30% of the rubric, and everything else depends on these rules.
2. **The worker pipeline and idempotent persistence:** unique keys and upserts, followed by the retry and DLQ topology. This is the core of the event-driven part of the rubric.
3. **The API**, then **the UI**, then **delivery** (compose, CI, README).

**Decisions and trade-offs:**

- **One message per job, with an in-memory breadth-first crawl in the worker.** I chose this over one message per page because it keeps job completion, the page limit and depth handling simple and correct in the time available. Idempotency still holds because all writes are upserts. The cost is that one job can't be split across several workers; multiple workers do process different jobs in parallel.
- **Raw `RabbitMQ.Client`, not MassTransit,** so the retry and DLQ mechanics are explicit and easy to review. It also avoids MassTransit v9's commercial licence.
- **Dual database provider (PostgreSQL / SQLite)** so the system runs on a machine without Docker. The SQL stays portable through `ON CONFLICT`.
- **Polling** (every 1.5 s) instead of SignalR or SSE. The spec allows it and it's simpler to run.

**Cut, or simplified:**

- **No transactional outbox.** If the publish fails after the job row is saved, the API marks the job `Failed` and returns `503`. It is never left `Pending` forever.
- **No robots.txt support, no crawl delay, no SSRF protection** (private IP ranges aren't blocked).
- **No auth, no rate limiting.** There is no DLQ replay tool beyond the RabbitMQ UI, and no metrics or tracing (OpenTelemetry).
- **Front-end automated tests** weren't written. The UI is checked by type-checking, lint and build in CI.

**Next steps:**

1. **One message per page (`CrawlPageRequested`)** so a single job can be crawled by many workers. Uniqueness would use `INSERT … ON CONFLICT DO NOTHING RETURNING` before publishing children. Job completion would be tracked with a counter of outstanding pages (`UPDATE … SET Outstanding = Outstanding - 1 RETURNING`).
2. **A transactional outbox** for job creation, plus EF Core migrations.
3. **OpenTelemetry tracing** across API → RabbitMQ → worker, using the correlation ID, plus metrics (pages/s, retries, DLQ depth).
4. **Politeness:** robots.txt, a per-host delay, and an SSRF guard.
5. **Push progress to the UI** with SSE or SignalR, plus Testcontainers tests against real PostgreSQL and RabbitMQ.

---

## Known limitations

- `/` and `/index.html`, or `http` and `https` versions of a URL, count as different pages. Normalization follows the rules above and doesn't guess at server-side aliases.
- If the start URL redirects to another host (for example `example.com` → `www.example.com`), that host is external by definition, so only the start page is crawled.
- A job is processed by one worker. A very long crawl holds one unacked message, and RabbitMQ's consumer timeout defaults to 30 minutes. The defaults (200 pages, 35 s per request) keep a job well below that in practice.
- The retry delay is a single fixed TTL (10 s) instead of exponential backoff. Exponential backoff would need one delay queue per step.
- `EnsureCreated` doesn't change an existing schema. Drop `data/crawler.db` or the `pgdata` volume after changing the model.
