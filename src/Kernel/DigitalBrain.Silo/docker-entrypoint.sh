#!/bin/sh
# Product packaging: one digitalbrain-kernel image supervises silo + mcp as separate OS processes.
# Northbound MCP is a cluster client process. No secrets here — inject via env / Key Vault.
set -eu

SILO_DIR="${SILO_DIR:-/app/silo}"
MCP_DIR="${MCP_DIR:-/app/mcp}"
SILO_URLS="${SILO_ASPNETCORE_URLS:-http://0.0.0.0:8080}"
MCP_URLS="${MCP_ASPNETCORE_URLS:-http://0.0.0.0:5000}"

silo_pid=""
mcp_pid=""

terminate() {
  code=0
  if [ -n "$silo_pid" ] && kill -0 "$silo_pid" 2>/dev/null; then
    kill -TERM "$silo_pid" 2>/dev/null || true
  fi
  if [ -n "$mcp_pid" ] && kill -0 "$mcp_pid" 2>/dev/null; then
    kill -TERM "$mcp_pid" 2>/dev/null || true
  fi
  if [ -n "$silo_pid" ]; then
    wait "$silo_pid" 2>/dev/null || code=$?
  fi
  if [ -n "$mcp_pid" ]; then
    wait "$mcp_pid" 2>/dev/null || true
  fi
  exit "$code"
}

trap terminate INT TERM

ASPNETCORE_URLS="$SILO_URLS" \
  dotnet "$SILO_DIR/DigitalBrain.Silo.dll" &
silo_pid=$!

# Northbound MCP is a client of the silo — start after silo process is up.
ASPNETCORE_URLS="$MCP_URLS" \
  dotnet "$MCP_DIR/DigitalBrain.Mcp.dll" &
mcp_pid=$!

# Exit when any supervised child exits; the others are torn down by the trap.
while kill -0 "$silo_pid" 2>/dev/null \
  && kill -0 "$mcp_pid" 2>/dev/null; do
  sleep 1
done

terminate
