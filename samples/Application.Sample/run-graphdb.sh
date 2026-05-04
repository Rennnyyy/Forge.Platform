#!/usr/bin/env bash
# run-graphdb.sh — start (or resume) a local GraphDB container via Podman, then run
# the Application.Sample against it.
#
# Usage:
#   ./run-graphdb.sh [--port 7200] [--repo forge] [--image ontotext/graphdb:10.8.5]
#
# The script is idempotent:
#   • container already running  → leave it as-is
#   • container exists but stopped → start it
#   • container does not exist   → create and start it
#
# After GraphDB is healthy the sample is launched with ASPNETCORE_ENVIRONMENT=GraphDb
# so appsettings.GraphDb.json is merged in automatically.
set -euo pipefail

# ── Defaults (override via flags) ─────────────────────────────────────────────
GRAPHDB_IMAGE="ontotext/graphdb:10.8.5"
GRAPHDB_PORT="7200"
GRAPHDB_REPO="forge"
CONTAINER_NAME="forge-graphdb"
HEALTH_TIMEOUT=60   # seconds to wait for GraphDB to become ready

# ── Argument parsing ───────────────────────────────────────────────────────────
while [[ $# -gt 0 ]]; do
  case "$1" in
    --port)  GRAPHDB_PORT="$2"; shift 2 ;;
    --repo)  GRAPHDB_REPO="$2"; shift 2 ;;
    --image) GRAPHDB_IMAGE="$2"; shift 2 ;;
    *) echo "Unknown option: $1" >&2; exit 1 ;;
  esac
done

GRAPHDB_BASE_URL="http://localhost:${GRAPHDB_PORT}"
GRAPHDB_REPOS_URL="${GRAPHDB_BASE_URL}/rest/repositories"

# ── Helpers ───────────────────────────────────────────────────────────────────
log()  { echo "[run-graphdb] $*"; }
die()  { echo "[run-graphdb] ERROR: $*" >&2; exit 1; }

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || die "'$1' not found on PATH."
}

wait_for_graphdb() {
  log "Waiting for GraphDB to be ready (timeout ${HEALTH_TIMEOUT}s)…"
  local deadline=$(( $(date +%s) + HEALTH_TIMEOUT ))
  until curl -sf "${GRAPHDB_BASE_URL}/rest/repositories" >/dev/null 2>&1; do
    [[ $(date +%s) -lt $deadline ]] || die "GraphDB did not become ready within ${HEALTH_TIMEOUT}s."
    sleep 1
  done
  log "GraphDB is ready."
}

ensure_repository() {
  # Check whether the repository already exists
  local check_code
  check_code=$(curl -s -o /dev/null -w "%{http_code}" "${GRAPHDB_REPOS_URL}/${GRAPHDB_REPO}")
  if [[ "$check_code" == "200" ]]; then
    log "Repository '${GRAPHDB_REPO}' already exists."
    return
  fi

  log "Creating repository '${GRAPHDB_REPO}'…"

  # Fetch the server's own default config template, then stamp in our id/title.
  # This is version-agnostic: whatever params GraphDB requires, they are all present
  # with their defaults — we only override id and title.
  local default_config
  default_config=$(curl -sf "${GRAPHDB_BASE_URL}/rest/repositories/default-config/graphdb") \
    || die "Could not fetch default repository config from GraphDB."

  local payload
  payload=$(printf '%s' "$default_config" \
    | python3 -c "
import sys, json
cfg = json.load(sys.stdin)
cfg['id'] = '${GRAPHDB_REPO}'
cfg['title'] = 'Forge sample repository'
cfg['params']['id']['value'] = '${GRAPHDB_REPO}'
print(json.dumps(cfg))
") || die "Failed to build repository config payload (is python3 available?)."

  local resp_file
  resp_file=$(mktemp /tmp/forge-graphdb-resp.XXXXXX)
  trap 'rm -f "$resp_file"' RETURN

  local http_code
  http_code=$(curl -s -w "%{http_code}" -o "$resp_file" \
    -X POST "${GRAPHDB_REPOS_URL}" \
    -H "Content-Type: application/json" \
    -d "$payload")

  if [[ "$http_code" == "201" || "$http_code" == "200" ]]; then
    log "Repository '${GRAPHDB_REPO}' created."
  else
    log "GraphDB response body:"
    cat "$resp_file" >&2
    die "Failed to create repository '${GRAPHDB_REPO}' (HTTP ${http_code})."
  fi
}

# ── Pre-flight ─────────────────────────────────────────────────────────────────
require_cmd podman
require_cmd curl
require_cmd dotnet

# ── Container lifecycle ────────────────────────────────────────────────────────
CONTAINER_STATE=$(podman inspect --format '{{.State.Status}}' "${CONTAINER_NAME}" 2>/dev/null || echo "absent")

case "$CONTAINER_STATE" in
  running)
    log "Container '${CONTAINER_NAME}' is already running."
    ;;
  exited|stopped|created)
    log "Container '${CONTAINER_NAME}' exists but is stopped — starting it."
    podman start "${CONTAINER_NAME}"
    ;;
  absent)
    log "Container '${CONTAINER_NAME}' does not exist — creating and starting it."
    podman run -d \
      --name "${CONTAINER_NAME}" \
      -p "${GRAPHDB_PORT}:7200" \
      "${GRAPHDB_IMAGE}"
    ;;
  *)
    die "Container '${CONTAINER_NAME}' is in unexpected state '${CONTAINER_STATE}'."
    ;;
esac

# ── Wait + ensure repository ───────────────────────────────────────────────────
wait_for_graphdb
ensure_repository

# ── Run the sample ─────────────────────────────────────────────────────────────
log "Starting Application.Sample with GraphDB backend…"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

ASPNETCORE_ENVIRONMENT=GraphDb \
Forge__GraphDb__BaseUrl="${GRAPHDB_BASE_URL}" \
Forge__GraphDb__RepositoryId="${GRAPHDB_REPO}" \
  dotnet run --project "${SCRIPT_DIR}/Forge.Application.Sample.csproj"
