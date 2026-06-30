#!/usr/bin/env bash
set -euo pipefail

SUDOPASS="${1:-}"
if [[ -z "$SUDOPASS" ]]; then
  echo "Missing sudo password argument" >&2
  exit 1
fi

run_sudo() {
  printf '%s\n' "$SUDOPASS" | sudo -S "$@"
}

stop_service() {
  if run_sudo systemctl stop gatewaysunteh4g 2>/dev/null; then
    echo "Stopped gatewaysunteh4g"
    return
  fi

  if run_sudo systemctl stop gatewaysuntech4g 2>/dev/null; then
    echo "Stopped gatewaysuntech4g"
    return
  fi

  echo "No known gateway service to stop"
}

start_service() {
  if run_sudo systemctl start gatewaysunteh4g 2>/dev/null; then
    echo "Started gatewaysunteh4g"
    return
  fi

  run_sudo systemctl start gatewaysuntech4g
  echo "Started gatewaysuntech4g"
}

show_status() {
  if run_sudo systemctl status gatewaysunteh4g --no-pager -n 0 2>/dev/null; then
    return
  fi

  run_sudo systemctl status gatewaysuntech4g --no-pager -n 0
}

show_logs() {
  if run_sudo journalctl -u gatewaysunteh4g -n 30 --no-pager 2>/dev/null; then
    return
  fi

  run_sudo journalctl -u gatewaysuntech4g -n 30 --no-pager
}

echo "[1/6] stop service"
stop_service || true

echo "[2/6] backup current dll"
run_sudo cp /opt/gateway-sunteh/GatewaySunteh4G-NET8.dll /opt/gateway-sunteh/GatewaySunteh4G-NET8.dll.backup || true

echo "[3/6] replace dll"
run_sudo mv /tmp/GatewaySunteh4G-NET8.dll.new /opt/gateway-sunteh/GatewaySunteh4G-NET8.dll
run_sudo chmod 644 /opt/gateway-sunteh/GatewaySunteh4G-NET8.dll

echo "[4/6] start service"
start_service

echo "[5/6] service status"
show_status

echo "[6/6] recent logs"
show_logs
