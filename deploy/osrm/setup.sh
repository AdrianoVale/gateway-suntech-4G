#!/usr/bin/env bash
# setup.sh — Configura o OSRM com dados de Norte + Nordeste do Brasil
# Executar como: bash setup.sh
# Pré-requisito: Docker instalado e usuário no grupo docker
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DATA_DIR="$SCRIPT_DIR/data"

mkdir -p "$DATA_DIR"
cd "$DATA_DIR"

echo "==> [1/7] Instalando osmium-tool (se necessário)..."
if ! command -v osmium &>/dev/null; then
    sudo apt-get update -qq
    sudo apt-get install -y osmium-tool
fi

echo "==> [2/7] Baixando Norte do Brasil (~148 MB)..."
wget -c -O norte-latest.osm.pbf \
    "https://download.geofabrik.de/south-america/brazil/norte-latest.osm.pbf"

echo "==> [3/7] Baixando Nordeste do Brasil (~410 MB)..."
wget -c -O nordeste-latest.osm.pbf \
    "https://download.geofabrik.de/south-america/brazil/nordeste-latest.osm.pbf"

echo "==> [4/7] Mesclando regiões com osmium-tool..."
osmium merge norte-latest.osm.pbf nordeste-latest.osm.pbf \
    -o norte-nordeste.osm.pbf --overwrite

echo "==> [5/7] Extraindo grafo OSRM (pode demorar alguns minutos)..."
docker run --rm -v "$DATA_DIR:/data" osrm/osrm-backend:latest \
    osrm-extract -p /opt/car.lua /data/norte-nordeste.osm.pbf

echo "==> [6/7] Particionando..."
docker run --rm -v "$DATA_DIR:/data" osrm/osrm-backend:latest \
    osrm-partition /data/norte-nordeste.osrm

echo "==> [6/7] Customizando..."
docker run --rm -v "$DATA_DIR:/data" osrm/osrm-backend:latest \
    osrm-customize /data/norte-nordeste.osrm

echo "==> [7/7] Iniciando serviço OSRM..."
cd "$SCRIPT_DIR"
docker compose up -d

echo "==> Aguardando OSRM ficar saudável..."
for i in $(seq 1 30); do
    if curl -sf http://127.0.0.1:5000/health &>/dev/null; then
        echo "✔ OSRM está saudável em http://127.0.0.1:5000"
        exit 0
    fi
    sleep 2
done

echo "AVISO: Health check expirou. Verifique com: docker logs osrm"
exit 1
