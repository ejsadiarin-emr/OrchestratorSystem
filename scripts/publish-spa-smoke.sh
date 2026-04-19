#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ARTIFACTS_DIR="${ROOT_DIR}/.artifacts/publish-spa-smoke"

ORCHESTRATOR_PROJECT="${ROOT_DIR}/apps/orchestrator/backend/DeploymentPoC.Orchestrator.csproj"
AGENT_PROJECT="${ROOT_DIR}/apps/agent/backend/DeploymentPoC.Agent.csproj"

ORCHESTRATOR_PUBLISH_DIR="${ARTIFACTS_DIR}/orchestrator"
AGENT_PUBLISH_DIR="${ARTIFACTS_DIR}/agent"

ORCHESTRATOR_BINARY="${ORCHESTRATOR_PUBLISH_DIR}/DeploymentPoC.Orchestrator"
AGENT_BINARY="${AGENT_PUBLISH_DIR}/DeploymentPoC.Agent"

ORCHESTRATOR_PORT="5181"
AGENT_PORT="5182"

PIDS=()

cleanup() {
  local exit_code=$?
  for pid in "${PIDS[@]:-}"; do
    if kill -0 "${pid}" 2>/dev/null; then
      kill "${pid}" 2>/dev/null || true
      wait "${pid}" 2>/dev/null || true
    fi
  done
  exit "${exit_code}"
}

trap cleanup EXIT INT TERM

publish_backend() {
  local project_path="$1"
  local output_dir="$2"

  dotnet publish "${project_path}" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o "${output_dir}" \
    >/dev/null
}

start_backend() {
  local binary_path="$1"
  local port="$2"
  local log_path="$3"

  ASPNETCORE_URLS="http://127.0.0.1:${port}" \
  "${binary_path}" >"${log_path}" 2>&1 &

  local pid=$!
  PIDS+=("${pid}")
}

check_spa_index() {
  local name="$1"
  local port="$2"

  local headers_file
  local body_file
  headers_file="$(mktemp)"
  body_file="$(mktemp)"

  local attempt
  for attempt in $(seq 1 40); do
    if curl -sS "http://127.0.0.1:${port}/" -D "${headers_file}" -o "${body_file}"; then
      break
    fi
    sleep 0.5
  done

  if [[ "${attempt}" -eq 40 ]]; then
    echo "${name}: failed to become reachable on port ${port}" >&2
    rm -f "${headers_file}" "${body_file}"
    return 1
  fi

  local status_line
  status_line="$(grep -m1 '^HTTP/' "${headers_file}" || true)"
  local status_code
  status_code="$(awk '{print $2}' <<<"${status_line}")"

  if [[ "${status_code}" != "200" ]]; then
    echo "${name}: GET / returned status ${status_code:-unknown}, expected 200" >&2
    rm -f "${headers_file}" "${body_file}"
    return 1
  fi

  if ! grep -Eiq '^Content-Type:[[:space:]]*text/html' "${headers_file}"; then
    echo "${name}: GET / did not return HTML content type" >&2
    rm -f "${headers_file}" "${body_file}"
    return 1
  fi

  rm -f "${headers_file}" "${body_file}"
}

mkdir -p "${ORCHESTRATOR_PUBLISH_DIR}" "${AGENT_PUBLISH_DIR}"

echo "Publishing orchestrator and agent (Release)..."
publish_backend "${ORCHESTRATOR_PROJECT}" "${ORCHESTRATOR_PUBLISH_DIR}"
publish_backend "${AGENT_PROJECT}" "${AGENT_PUBLISH_DIR}"

if [[ ! -x "${ORCHESTRATOR_BINARY}" ]]; then
  echo "Orchestrator binary not found: ${ORCHESTRATOR_BINARY}" >&2
  exit 1
fi

if [[ ! -x "${AGENT_BINARY}" ]]; then
  echo "Agent binary not found: ${AGENT_BINARY}" >&2
  exit 1
fi

echo "Launching published binaries..."
start_backend "${ORCHESTRATOR_BINARY}" "${ORCHESTRATOR_PORT}" "${ARTIFACTS_DIR}/orchestrator.log"
start_backend "${AGENT_BINARY}" "${AGENT_PORT}" "${ARTIFACTS_DIR}/agent.log"

echo "Validating SPA index responses..."
check_spa_index "orchestrator" "${ORCHESTRATOR_PORT}"
check_spa_index "agent" "${AGENT_PORT}"

echo "PASS: published orchestrator and agent serve SPA index with 200 text/html"
