#!/bin/bash
# deploy_hub.sh — Ativa WebSocket Hub em produção
# Executado via: printf 'BltRst221311\n' | sudo -S bash /tmp/deploy_hub.sh
set -e

echo "[1/7] Verificando se WS_JWT_SECRET já está no tema.php..."
if grep -q 'WS_JWT_SECRET' /var/www/html/tema.php; then
    echo "      -> WS_JWT_SECRET já definido; pulando append."
else
    echo "      -> Adicionando WS_JWT_SECRET ao tema.php..."
    cat /tmp/ws_addon.php >> /var/www/html/tema.php
    echo "      -> OK"
fi

echo "[2/7] Implantando appsettings.json com Hub habilitado..."
cp /tmp/appsettings_hub.json /opt/gateway-sunteh/appsettings.json
chown root:root /opt/gateway-sunteh/appsettings.json
chmod 644 /opt/gateway-sunteh/appsettings.json
echo "      -> OK"

echo "[3/7] Implantando configs Apache com proxy WebSocket..."
cp /tmp/cliente_hub.conf    /etc/apache2/sites-available/cliente.conf
cp /tmp/cliente_hmp_hub.conf /etc/apache2/sites-available/cliente-hmp.conf
echo "      -> OK"

echo "[4/7] Habilitando módulos Apache (proxy, proxy_http, proxy_wstunnel, rewrite)..."
a2enmod proxy proxy_http proxy_wstunnel rewrite headers 2>&1 | grep -v 'already enabled\|Enabling module' || true
echo "      -> OK"

echo "[5/7] Testando configuração Apache..."
apache2ctl configtest
echo "      -> OK"

echo "[6/7] Reiniciando gateway-sunteh..."
systemctl restart gateway-sunteh
sleep 3
echo "      -> Status gateway-sunteh:"
systemctl is-active gateway-sunteh

echo "[7/7] Recarregando Apache..."
systemctl reload apache2
echo "      -> OK"

echo ""
echo "=== DEPLOY HUB CONCLUÍDO ==="
echo "Gateway status:"
systemctl status gateway-sunteh --no-pager | tail -5
echo ""
echo "Apache status:"
systemctl is-active apache2
