# Deploy GatewaySunteh4G to Production Server
$ErrorActionPreference = "Stop"
$SERVER = "209.126.125.173"
$PORT = "2222"
$USER = "adriano"
$PASSWORD = "BltRst221311"
$SERVICE = "gateway-sunteh.service"
$DLL_PATH = "C:\Users\torresVale\Documents\GatewaySunteh4G-NET8\bin\Release\net8.0\GatewaySunteh4G-NET8.dll"
$SQL_PATH = "C:\Users\torresVale\Documents\GatewaySunteh4G-NET8\add_network_commands.sql"

Write-Host "================================" -ForegroundColor Cyan
Write-Host "GatewaySunteh4G Deployment" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan

Write-Host "[1/6] Verificando arquivos..." -ForegroundColor Yellow
if (!(Test-Path $DLL_PATH)) { Write-Host "ERRO: DLL nao encontrada" -ForegroundColor Red; exit 1 }
if (!(Test-Path $SQL_PATH)) { Write-Host "ERRO: SQL nao encontrado" -ForegroundColor Red; exit 1 }
Write-Host "OK Arquivos encontrados" -ForegroundColor Green

Write-Host "[2/6] Uploading DLL..." -ForegroundColor Yellow
& pscp -P $PORT -pw $PASSWORD $DLL_PATH "${USER}@${SERVER}:/tmp/GatewaySunteh4G-NET8.dll.new"
if ($LASTEXITCODE -ne 0) { Write-Host "ERRO: Falha no upload da DLL" -ForegroundColor Red; exit 1 }
Write-Host "OK DLL enviada" -ForegroundColor Green

Write-Host "[3/6] Uploading SQL..." -ForegroundColor Yellow
& pscp -P $PORT -pw $PASSWORD $SQL_PATH "${USER}@${SERVER}:/tmp/add_network_commands.sql"
if ($LASTEXITCODE -ne 0) { Write-Host "ERRO: Falha no upload do SQL" -ForegroundColor Red; exit 1 }
Write-Host "OK SQL enviado" -ForegroundColor Green

Write-Host "[4/6] Executando deployment no servidor..." -ForegroundColor Yellow
$deployScriptPath = "$env:TEMP\gw_deploy.sh"
$sh = "echo '${PASSWORD}' | sudo -S systemctl stop ${SERVICE}`n"
$sh += "echo '${PASSWORD}' | sudo -S cp /opt/gateway-sunteh/GatewaySunteh4G-NET8.dll /opt/gateway-sunteh/GatewaySunteh4G-NET8.dll.backup`n"
$sh += "echo '${PASSWORD}' | sudo -S cp /tmp/GatewaySunteh4G-NET8.dll.new /opt/gateway-sunteh/GatewaySunteh4G-NET8.dll`n"
$sh += "echo '${PASSWORD}' | sudo -S chmod 644 /opt/gateway-sunteh/GatewaySunteh4G-NET8.dll`n"
$sh += "echo '${PASSWORD}' | sudo -S systemctl start ${SERVICE}`n"
$sh += "sleep 2`n"
$sh += "systemctl is-active ${SERVICE}`n"
[System.IO.File]::WriteAllText($deployScriptPath, $sh, [System.Text.UTF8Encoding]::new($false))
& pscp -P $PORT -pw $PASSWORD $deployScriptPath "${USER}@${SERVER}:/tmp/gw_deploy.sh"
& plink -P $PORT -pw $PASSWORD -batch "${USER}@${SERVER}" "bash /tmp/gw_deploy.sh"
if ($LASTEXITCODE -ne 0) { Write-Host "ERRO: Falha no deployment" -ForegroundColor Red; exit 1 }
Write-Host "OK Gateway reiniciado" -ForegroundColor Green

Write-Host "[5/6] Executando migracao do banco..." -ForegroundColor Yellow
$sqlCmd = "export PGPASSWORD='B1tRst@Pg#2026!Gw'; psql -h 127.0.0.1 -U blt_aplicacao -d blt_rastro_pg -f /tmp/add_network_commands.sql"
& plink -P $PORT -pw $PASSWORD -batch "${USER}@${SERVER}" $sqlCmd
if ($LASTEXITCODE -ne 0) {
    Write-Host "AVISO: Migracao pode ter falhado (provavelmente ja executada)" -ForegroundColor Yellow
} else {
    Write-Host "OK Banco atualizado" -ForegroundColor Green
}

Write-Host "[6/6] Verificando deployment..." -ForegroundColor Yellow
$verifyScriptPath = "$env:TEMP\gw_verify.sh"
$vs = "echo '${PASSWORD}' | sudo -S journalctl -u ${SERVICE} -n 10 --no-pager`n"
[System.IO.File]::WriteAllText($verifyScriptPath, $vs, [System.Text.UTF8Encoding]::new($false))
& pscp -P $PORT -pw $PASSWORD $verifyScriptPath "${USER}@${SERVER}:/tmp/gw_verify.sh"
& plink -P $PORT -pw $PASSWORD -batch "${USER}@${SERVER}" "bash /tmp/gw_verify.sh"

Write-Host "================================" -ForegroundColor Cyan
Write-Host "OK DEPLOYMENT CONCLUIDO!" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Cyan