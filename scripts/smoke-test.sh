#!/usr/bin/env bash
# Usage: scripts/smoke-test.sh <base-url> <site-to-crawl>
# Creates a crawl job, polls until it finishes, prints the tree. Fails unless the job Completed.
set -euo pipefail

BASE="${1:-http://localhost:8080}"
SITE="${2:-https://example.com/}"

job_id=$(curl -fsS -X POST "$BASE/api/jobs" -H 'Content-Type: application/json' \
  -d "{\"url\":\"$SITE\",\"maxDepth\":1}" | sed -E 's/.*"jobId":"([^"]+)".*/\1/')
echo "Created job $job_id"

for _ in $(seq 1 60); do
  status=$(curl -fsS "$BASE/api/jobs/$job_id" | sed -E 's/.*"status":"([^"]+)".*/\1/')
  echo "status: $status"
  case "$status" in
    Completed) curl -fsS "$BASE/api/jobs/$job_id/tree"; echo; exit 0 ;;
    Failed|Canceled) curl -fsS "$BASE/api/jobs/$job_id"; echo; exit 1 ;;
  esac
  sleep 2
done

echo "Timed out waiting for job $job_id"
exit 1
