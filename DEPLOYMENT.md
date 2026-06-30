# Deployment Guide - GatewaySunteh4G with Command Response Validation

## Changes Made

### 1. Bug Fix: Command Response Validation
**Problem**: Gateway was accepting ANY RES response without validating command codes, causing incorrect status updates.

**Solution**: 
- Modified `St4315PacketProcessor.cs` to extract command codes (fields 2 and 3) from RES responses
- Updated `ICommandDispatcher.cs` interface to accept command codes
- Enhanced `CommandDispatcher.cs` to validate received codes match sent command type
- Added detection for "Unknown CMD" responses, marking them as status 4 (failure)

### 2. New Feature: Network Commands Support
Added 5 new command types per Suntech protocol specification:
- Type 5: Check-In Maintenance Server (01;01)
- Type 6: Request IMSI (01;02)
- Type 7: Request ICCID (01;03)
- Type 8: Check Network Type (01;04)
- Type 9: Request Phone Number (01;05)

## Deployment Steps

### Step 1: Build Application (Already Done)
```bash
cd C:\Users\torresVale\Documents\GatewaySunteh4G-NET8
dotnet build --configuration Release
```
✅ Build output: `bin\Release\net8.0\GatewaySunteh4G-NET8.dll`

### Step 2: Upload Files to Server
Use plink/pscp to upload the compiled DLL and SQL migration:

```powershell
# Upload DLL
pscp -P 2222 -pw BltRst221311 "C:\Users\torresVale\Documents\GatewaySunteh4G-NET8\bin\Release\net8.0\GatewaySunteh4G-NET8.dll" adriano@209.126.125.173:/opt/gateway-sunteh/GatewaySunteh4G-NET8.dll.new

# Upload SQL migration
pscp -P 2222 -pw BltRst221311 "C:\Users\torresVale\Documents\GatewaySunteh4G-NET8\add_network_commands.sql" adriano@209.126.125.173:/tmp/add_network_commands.sql
```

### Step 3: Connect to Server
```powershell
plink -P 2222 -pw BltRst221311 adriano@209.126.125.173
```

### Step 4: Backup and Replace DLL
```bash
# Backup current version
sudo cp /opt/gateway-sunteh/GatewaySunteh4G-NET8.dll /opt/gateway-sunteh/GatewaySunteh4G-NET8.dll.backup

# Replace with new version
sudo mv /opt/gateway-sunteh/GatewaySunteh4G-NET8.dll.new /opt/gateway-sunteh/GatewaySunteh4G-NET8.dll
sudo chmod 644 /opt/gateway-sunteh/GatewaySunteh4G-NET8.dll
```

### Step 5: Run Database Migration
```bash
export PGPASSWORD='B1tRst@Pg#2026!Gw'
psql -h 127.0.0.1 -U blt_aplicacao -d blt_rastro_pg -f /tmp/add_network_commands.sql
```

Expected output:
```
NOTICE:  Usando schema: localize
NOTICE:  Novos tipos de comando adicionados com sucesso!
INSERT 0 5
```

### Step 6: Restart Gateway Service
```bash
sudo systemctl restart gatewaysunteh4g
# OR
sudo systemctl restart gatewaysuntech4g

# Verify it's running
sudo systemctl status gatewaysunteh4g --no-pager
```

### Step 7: Verify Deployment
```bash
# Check logs for startup
journalctl -u gatewaysunteh4g -n 50 --no-pager

# Verify new command types in database
export PGPASSWORD='B1tRst@Pg#2026!Gw'
psql -h 127.0.0.1 -U blt_aplicacao -d blt_rastro_pg -c "SELECT id, descricao FROM localize.tipo_comando ORDER BY id;"
```

Expected output should show all 9 command types (1-9).

## Testing

### Test 1: Enable1 Command (Bloqueio)
1. Create a command in database: `tipo_comando_id=1`, `device_id=9999999999991`
2. Wait for gateway to send CMD;9999999999991;04;01
3. Simulate device response: `echo "RES;9999999999991;04;01;OK" | nc -w 3 127.0.0.1 9040`
4. Check database - status should change to 2 (Enviado com sucesso)

### Test 2: Disable1 Command (Desbloqueio)
1. Create a command in database: `tipo_comando_id=2`, `device_id=9999999999991`
2. Wait for gateway to send CMD;9999999999991;04;02
3. Simulate device response: `echo "RES;9999999999991;04;02;OK" | nc -w 3 127.0.0.1 9040`
4. Check database - status should change to 2 (Enviado com sucesso)

### Test 3: Wrong Response Code
1. Create Enable1 command (tipo_comando_id=1)
2. Simulate WRONG response: `echo "RES;9999999999991;04;02;OK" | nc -w 3 127.0.0.1 9040`
3. Check database - status should change to 4 (Aparelho desconhecido) with warning log

### Test 4: Unknown Command Response
1. Create any command
2. Simulate rejection: `echo "RES;9999999999991;04;01;Unknown CMD (Not Support)" | nc -w 3 127.0.0.1 9040`
3. Check database - status should change to 4 (Aparelho desconhecido)

### Test 5: New Network Commands
1. Create command: `tipo_comando_id=6` (Request IMSI), `device_id=9999999999991`
2. Gateway should send: CMD;9999999999991;01;02
3. Simulate response: `echo "RES;9999999999991;01;02;460110012345678" | nc -w 3 127.0.0.1 9040`
4. Check database - status should change to 2

## Rollback Procedure
If issues occur:
```bash
# Stop gateway
sudo systemctl stop gatewaysunteh4g

# Restore backup
sudo cp /opt/gateway-sunteh/GatewaySunteh4G-NET8.dll.backup /opt/gateway-sunteh/GatewaySunteh4G-NET8.dll

# Restart
sudo systemctl start gatewaysunteh4g
```

## Key Improvements
1. ✅ **Command validation**: Gateway now validates RES response codes match sent command
2. ✅ **Error detection**: Detects "Unknown CMD" responses and marks as failed
3. ✅ **Better logging**: Logs include command codes for debugging
4. ✅ **New commands**: Supports 5 additional Suntech Network Commands (01;01 through 01;05)
5. ✅ **Type safety**: CommandTypeId properly mapped to expected response codes
