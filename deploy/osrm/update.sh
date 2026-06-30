#!/usr/bin/env bash
# update.sh — Atualiza os dados do OSRM (re-baixa PBFs e reprocessa)
# Executar mensalmente via cron:
#   0 3 1 * * /srv/blt/deploy/osrm/update.sh >> /var/log/osrm-update.log 2>&1
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DATA_DIR="$SCRIPT_DIR/data"

echo "==> [$(date '+%F %T')] Iniciando atualização do OSRM..."

echo "==> [1/7] Parando serviço OSRM..."
cd "$SCRIPT_DIR"
docker compose down

echo "==> [2/7] Baixando Norte do Brasil..."
wget -c -O "$DATA_DIR/norte-latest.osm.pbf" \
    "https://download.geofabrik.de/south-america/brazil/norte-latest.osm.pbf"

echo "==> [3/7] Baixando Nordeste do Brasil..."
wget -c -O "$DATA_DIR/nordeste-latest.osm.pbf" \
    "https://download.geofabrik.de/south-america/brazil/nordeste-latest.osm.pbf"

echo "==> [4/7] Mesclando regiões..."
osmium merge "$DATA_DIR/norte-latest.osm.pbf" "$DATA_DIR/nordeste-latest.osm.pbf" \
    -o "$DATA_DIR/norte-nordeste.osm.pbf" --overwrite

echo "==> [5/7] Extraindo grafo OSRM..."
docker run --rm -v "$DATA_DIR:/data" osrm/osrm-backend:latest \
    osrm-extract -p /opt/car.lua /data/norte-nordeste.osm.pbf

echo "==> [6/7] Particionando e customizando..."
docker run --rm -v "$DATA_DIR:/data" osrm/osrm-backend:latest \
    osrm-partition /data/norte-nordeste.osrm
docker run --rm -v "$DATA_DIR:/data" osrm/osrm-backend:latest \
    osrm-customize /data/norte-nordeste.osrm

echo "==> [7/7] Iniciando serviço OSRM..."
docker compose up -d

echo "==> Aguardando OSRM ficar saudável..."
for i in $(seq 1 30); do
    if curl -sf http://127.0.0.1:5000/health &>/dev/null; then
        echo "✔ OSRM atualizado e saudável em $(date '+%F %T')"
        exit 0
    fi
    sleep 2
done

echo "AVISO: Health check expirou após atualização. Verifique: docker logs osrm"
exit 1
