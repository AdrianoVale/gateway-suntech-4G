# Correções Implementadas - GatewaySunteh4G

## Atualização 2026-05-22

- Adicionado suporte ao pacote ASCII de heartbeat `ALV;IMEI` no parser, incluindo entrada com aspas duplas no payload (`"ALV;2290032094"`).
- Ajustado tratamento de `RES` para reduzir falsos negativos de correlação de comando:
    - correlação resiliente de `device_id` com e sem zero à esquerda;
    - normalização dos códigos de comando (`4;1` equivalente a `04;01`).
- Corrigida a conclusão de comando para status de sucesso (`status_comando_id = 2`) quando a confirmação `RES` for válida nessas variações.
- Adicionada interpretação de pacotes Normal Data (ASCII) para:
    - `UEX` e `AUEX` (External Serial Device Data Report);
    - `TRV` e `ATRV` (Travel Report).
- Para `UEX/AUEX`, o gateway agora extrai e persiste latitude, longitude, velocidade, curso, satélites, fix status e estados de entrada/saída.
- Para `TRV/ATRV`, o gateway agora extrai e persiste posição final da viagem (fallback para posição inicial), e velocidade média com tratamento de campos opcionais vazios no payload.
- Criado projeto de testes automatizados em `tests/GatewaySunteh4G.NET8.Tests` cobrindo:
    - parsing de `ALV` com aspas;
    - parsing de `UEX`;
    - parsing de `TRV`;
    - confirmação `RES` válida com variação de `device_id` e código;
    - rejeição de `RES` com código divergente.
- Validação local executada:
    - `dotnet test` com 5 testes aprovados;
    - `dotnet build -c Release` sem erros.

## Problema Original
Gateway não estava validando respostas RES dos dispositivos Suntech, aceitando qualquer resposta e marcando comandos como "Enviado com sucesso" incorretamente.

## Análise do Bug

### Código Anterior (INCORRETO)
```csharp
// St4315PacketProcessor.cs - linha 156
private void ProcessResponse(string[] fields, IPEndPoint remoteEndPoint)
{
    var deviceId = GetField(fields, 1);
    _commandDispatcher.HandleResponse(deviceId);  // ❌ Ignora campos 2 e 3!
    // ...
}
```

**Problema**: Ao receber `RES;9999999999991;04;01;OK`, o gateway:
- ✅ Extraía deviceId = "9999999999991"
- ❌ **IGNORAVA** commandCode1 = "04"  
- ❌ **IGNORAVA** commandCode2 = "01"
- ❌ Marcava status=2 sem validar se era resposta do comando correto

**Consequência**: Se enviar comando Enable1 (04;01) e receber resposta Disable1 (04;02), o gateway aceitava como sucesso!

### Código Corrigido (CORRETO)
```csharp
// St4315PacketProcessor.cs - NEW
private void ProcessResponse(string[] fields, IPEndPoint remoteEndPoint)
{
    var deviceId = GetField(fields, 1);
    var commandCode1 = GetField(fields, 2);       // ✅ Extrai código
    var commandCode2 = GetField(fields, 3);       // ✅ Extrai código
    var extraInfo = fields.Length > 4 ? GetOptionalField(fields, 4) : null;
    
    _commandDispatcher.HandleResponse(deviceId, commandCode1, commandCode2, extraInfo);
    // ...
}
```

```csharp
// CommandDispatcher.cs - NEW
public void HandleResponse(string deviceId, string commandCode1, string commandCode2, string? extraInfo)
{
    var pending = _commandRegistry.Complete(deviceId);
    if (pending is null) return;
    
    var expectedCodes = GetExpectedCommandCodes(pending.Command.CommandTypeId);
    var receivedCodes = $"{commandCode1};{commandCode2}";
    
    // ✅ VALIDAÇÃO: resposta corresponde ao comando enviado?
    if (receivedCodes != expectedCodes)
    {
        _logger.LogWarning("Código recebido {ReceivedCodes} != esperado {ExpectedCodes}", 
            receivedCodes, expectedCodes);
        pending.Command.StatusCommandId = 4;  // Marca como falha
        _dataService.UpdateCommand(pending.Command);
        return;
    }
    
    // ✅ VALIDAÇÃO: dispositivo retornou "Unknown CMD"?
    if (!string.IsNullOrEmpty(extraInfo) && extraInfo.Contains("Unknown"))
    {
        _logger.LogWarning("Comando rejeitado pelo dispositivo: {ExtraInfo}", extraInfo);
        pending.Command.StatusCommandId = 4;
        _dataService.UpdateCommand(pending.Command);
        return;
    }
    
    // ✅ CAPTURA DE DADOS: salva informações da resposta em parametros
    if (!string.IsNullOrWhiteSpace(extraInfo))
    {
        switch (pending.Command.CommandTypeId)
        {
            case 6:  // Request IMSI
                pending.Command.Parameters = extraInfo;
                break;
            case 7:  // Request ICCID
                pending.Command.Parameters = extraInfo;
                break;
            case 8:  // Check Network Type
                pending.Command.Parameters = MapNetworkType(extraInfo);
                break;
            case 9:  // Request Phone Number
                pending.Command.Parameters = extraInfo;
                break;
        }
    }
    
    // ✅ Tudo OK - marca como sucesso
    pending.Command.StatusCommandId = 2;
    _dataService.UpdateCommand(pending.Command);
}
```

## Arquivos Modificados

### 1. ICommandDispatcher.cs
- **Antes**: `void HandleResponse(string deviceId)`
- **Depois**: `void HandleResponse(string deviceId, string commandCode1, string commandCode2, string? extraInfo = null)`

### 2. St4315PacketProcessor.cs
- Método `ProcessResponse` (linhas 156-171)
- Agora extrai campos 2, 3 e 4 da resposta RES
- Logs melhorados com códigos de comando

### 3. CommandDispatcher.cs
- Método `HandleResponse` completamente reescrito (linhas 100-188)
- Novo método `GetExpectedCommandCodes` para mapear tipo → código esperado
- **NOVO** método `MapNetworkType` para converter código numérico em descrição de rede
- Método `BuildPayload` atualizado para suportar tipos 5-9 (linhas 208-237)
- **Captura de dados de resposta** em `parametros` para comandos 6, 7, 8, 9
- Validações:
  - Código recebido == código esperado
  - Resposta não contém "Unknown CMD"

### 4. CommandRecord.cs
- Campo `Parameters` alterado de `init` para `set` (permitindo atualização após criação)
- Necessário para gravar dados capturados das respostas

## Novos Recursos Adicionados

### Suporte a Network Commands (Suntech Protocol)
Adicionados 5 novos tipos de comando conforme protocolo Suntech ST4315:

| Tipo | Descrição | Código CMD | Código RES | Dados Capturados |
|------|-----------|------------|------------|------------------|
| 5 | Check-In Maintenance Server | CMD;IMEI;01;01 | RES;IMEI;01;01 | - |
| 6 | Request IMSI | CMD;IMEI;01;02 | RES;IMEI;01;02;460110012345678 | **IMSI** → parametros |
| 7 | Request ICCID | CMD;IMEI;01;03 | RES;IMEI;01;03;89860000000000000000 | **ICCID** → parametros |
| 8 | Check Network Type | CMD;IMEI;01;04 | RES;IMEI;01;04;8 | **Tipo de rede** → parametros |
| 9 | Request Phone Number | CMD;IMEI;01;05 | RES;IMEI;01;05;+5511999999999 | **Número** → parametros |

### Tabela de Mapeamento de Tipo de Rede (Comando Tipo 8)

Quando o dispositivo responde ao comando 0104 (Check Network Type), retorna um código numérico que é automaticamente convertido para descrição:

| Código | Descrição Gravada em parametros |
|--------|----------------------------------|
| 0 | GSM |
| 1 | GSM COMPACT |
| 2 | UTRAN |
| 3 | GSM with EDGE availability |
| 4 | UTRAN with HSDPA availability |
| 5 | UTRAN with HSUPA availability |
| 6 | UTRAN with HSDPA and HSUPA availability |
| 7 | Reserved |
| 8 | LTE Cat M1 |
| 9 | LTE Cat NB1 |
| 10 | LTE Cat 1 |
| 255 | Invalid or No Network |

**Exemplo**:
```
Enviado: CMD;9999999999991;01;04
Recebido: RES;9999999999991;01;04;8
Gravado em parametros: "LTE Cat M1"
```

### Migration SQL
Arquivo `add_network_commands.sql` adiciona os 5 novos tipos ao banco:
```sql
INSERT INTO localize.tipo_comando (id, descricao) VALUES
(5, 'Check-In Maintenance Server'),
(6, 'Request IMSI'),
(7, 'Request ICCID'),
(8, 'Check Network Type'),
(9, 'Request Phone Number')
ON CONFLICT (id) DO UPDATE SET descricao = EXCLUDED.descricao;
```

## Mapeamento Completo de Comandos

| CommandTypeId | Descrição | BuildPayload | Código Esperado RES | Dados Capturados |
|---------------|-----------|--------------|---------------------|------------------|
| 1 | Bloqueio (Enable1) | CMD;IMEI;04;01 | 04;01 | - |
| 2 | Desbloqueio (Disable1) | CMD;IMEI;04;02 | 04;02 | - |
| 3 | Posição atual | CMD;IMEI;03;01 | 03;01 | - |
| 4 | Número do Chip | CMD;IMEI;01;03 | 01;03 | - |
| 5 | Check-In Maintenance | CMD;IMEI;01;01 | 01;01 | - |
| 6 | Request IMSI | CMD;IMEI;01;02 | 01;02 | **IMSI** |
| 7 | Request ICCID | CMD;IMEI;01;03 | 01;03 | **ICCID** |
| 8 | Check Network Type | CMD;IMEI;01;04 | 01;04 | **Tipo de rede (texto)** |
| 9 | Request Phone Number | CMD;IMEI;01;05 | 01;05 | **Número de telefone** |
| 10 | Custom (via Parameters) | (valor de Parameters) | - | - |

## Cenários de Teste

### ✅ Cenário 1: Resposta Correta
```
Enviado: CMD;9999999999991;04;01
Recebido: RES;9999999999991;04;01;OK
Resultado: status_comando_id = 2 (Sucesso)
Log: "Comando 123 tipo 1 para device 9999999999991 concluído com sucesso (04;01)"
```

### ❌ Cenário 2: Resposta com Código Errado
```
Enviado: CMD;9999999999991;04;01
Recebido: RES;9999999999991;04;02;OK  (código errado!)
Resultado: status_comando_id = 4 (Falha)
Log: "Resposta RES com código 04;02 não corresponde ao esperado 04;01"
```

### ❌ Cenário 3: Comando Não Suportado pelo Dispositivo
```
Enviado: CMD;9999999999991;04;01
Recebido: RES;9999999999991;04;01;Unknown CMD (Not Support)
Resultado: status_comando_id = 4 (Falha)
Log: "Resposta RES indica comando desconhecido: Unknown CMD (Not Support)"
```

### ✅ Cenário 4: Request IMSI com Captura de Dados
```
Enviado: CMD;9999999999991;01;02
Recebido: RES;9999999999991;01;02;460110012345678
Resultado: 
  - status_comando_id = 2 (Sucesso)
  - parametros = "460110012345678"
Log: "IMSI capturado para device 9999999999991: 460110012345678"
```

### ✅ Cenário 5: Check Network Type com Mapeamento
```
Enviado: CMD;9999999999991;01;04
Recebido: RES;9999999999991;01;04;8
Resultado: 
  - status_comando_id = 2 (Sucesso)
  - parametros = "LTE Cat M1"  (convertido de 8 → "LTE Cat M1")
Log: "Tipo de rede capturado para device 9999999999991: 8 = LTE Cat M1"
```

### ✅ Cenário 6: Request ICCID
```
Enviado: CMD;9999999999991;01;03
Recebido: RES;9999999999991;01;03;89860000000000000000
Resultado: 
  - status_comando_id = 2 (Sucesso)
  - parametros = "89860000000000000000"
Log: "ICCID capturado para device 9999999999991: 89860000000000000000"
```

### ✅ Cenário 7: Request Phone Number
```
Enviado: CMD;9999999999991;01;05
Recebido: RES;9999999999991;01;05;+5511999999999
Resultado: 
  - status_comando_id = 2 (Sucesso)
  - parametros = "+5511999999999"
Log: "Número de telefone capturado para device 9999999999991: +5511999999999"
```

## Build e Compilação
✅ Compilação realizada com sucesso:
```
Compilação com êxito.
    0 Aviso(s)
    0 Erro(s)
```

Binário gerado: `C:\Users\torresVale\Documents\GatewaySunteh4G-NET8\bin\Release\net8.0\GatewaySunteh4G-NET8.dll`

## Próximos Passos para Deploy
1. ✅ Código corrigido e compilado
2. ✅ Migration SQL criado
3. ✅ Script PowerShell de deployment criado (`deploy.ps1`)
4. ⏳ Executar `.\deploy.ps1` para fazer o deployment
5. ⏳ Testar comandos no servidor

## Benefícios da Correção
1. **Segurança**: Não aceita respostas de comandos diferentes do enviado
2. **Confiabilidade**: Detecta quando dispositivo não suporta o comando
3. **Rastreabilidade**: Logs detalhados com códigos de comando para debugging
4. **Extensibilidade**: Suporte a 5 novos tipos de comando Suntech
5. **Manutenibilidade**: Código mais claro e validações explícitas
6. **Captura de Dados**: IMSI, ICCID, tipo de rede e número de telefone são automaticamente salvos
7. **Mapeamento Inteligente**: Códigos numéricos de rede convertidos para descrições legíveis
