#!/usr/bin/env bash
set -euo pipefail

DB_HOST="127.0.0.1"
DB_NAME="blt_rastro_pg"
DB_USER="blt_aplicacao"
DB_PASS="B1tRst@Pg#2026!Gw"
UDP_HOST="127.0.0.1"
UDP_PORT="9040"
CMD_DEVICE="9999999999991"
SERIAL_DEVICE="0360000001"

export PGPASSWORD="$DB_PASS"

echo "[A] Inserindo comando de teste tipo=1 para validar RES -> status 2"
CMD_ID=$(psql -h "$DB_HOST" -U "$DB_USER" -d "$DB_NAME" -q -t -A -c "INSERT INTO public.comando (device_id, criado, atualizado, parametros, tipo_comando_id, status_comando_id) VALUES ($CMD_DEVICE, now(), now(), '', 1, 1) RETURNING id;" | head -n 1 | tr -d '[:space:]')
if [[ -z "$CMD_ID" ]]; then
    echo "Falha ao capturar CMD_ID" >&2
    exit 1
fi
echo "CMD_ID=$CMD_ID"

python3 - <<'PY'
import socket
import time

host = "127.0.0.1"
port = 9040
packet = b"RES;9999999999991;04;01;OK"
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
for _ in range(60):
    sock.sendto(packet, (host, port))
    time.sleep(0.25)
sock.close()
print("RES flood sent")
PY

echo "[B] Status final do comando"
psql -h "$DB_HOST" -U "$DB_USER" -d "$DB_NAME" -t -A -F '|' -c "SELECT id, status_comando_id, tipo_comando_id, device_id, atualizado FROM public.comando WHERE id = $CMD_ID;"

echo "[C] Enviando pacote UEX"
python3 - <<'PY'
import socket
packet = "UEX;0360000001;3FFFFF;36;1.0.14;1;20161117;08:37:39;0000004F;450;0;0014;20;+37.479323;+126.887827;62.03;65.43;10;1;00000101;00001000;25; Welcome to ST SUNLAB World!;12;0; 4759;78245;13.5"
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.sendto(packet.encode("ascii"), ("127.0.0.1", 9040))
sock.close()
print("UEX sent")
PY

echo "[D] Enviando pacote TRV"
python3 - <<'PY'
import socket
packet = "TRV;0360000001;07FFFFF;36;1.0.14;1;20161117;08:37:39;+37.479323;+126.887827;+38.479323;+127.887827;500000193E0CCD01;23824;10800;436;2;;325;3;102.59;38.29; 78245; 319;0;0;0;0;0;0;0;0;0;0;0;0;0;0;0;0;0;0;0;0"
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.sendto(packet.encode("ascii"), ("127.0.0.1", 9040))
sock.close()
print("TRV sent")
PY

echo "[E] Últimas posições do device serial"
psql -h "$DB_HOST" -U "$DB_USER" -d "$DB_NAME" -t -A -F '|' -c "SELECT id, device_id, msg_type_id, round(lat::numeric,6), round(lon::numeric,6), speed, gps, sat, datetime FROM public.position WHERE device_id = '$SERIAL_DEVICE' ORDER BY id DESC LIMIT 4;"

echo "[F] Logs recentes relevantes"
printf '%s\n' 'BltRst221311' | sudo -S journalctl -u gateway-sunteh.service -n 120 --no-pager | grep -E 'Comando|Resposta RES|Pacote UEX|Pacote TRV' || true
