# Relatório de Manutenção — Servidor BLT Rastreamentos
**Data de geração:** 18/05/2026  
**Servidor:** `209.126.125.173` (usvds6026x3)

---

## 1. Identificação do Servidor

| Campo | Valor |
|---|---|
| **IP Público** | `209.126.125.173` |
| **Hostname** | `usvds6026x3` |
| **Sistema Operacional** | Ubuntu 24.04.4 LTS |
| **Kernel** | `6.8.0-111-generic` |
| **CPUs** | 6 vCPUs |
| **RAM** | 15 GB total / ~768 MB em uso |
| **Disco** | 385 GB total / 5,1 GB usado (2%) |

---

## 2. Acesso SSH

| Campo | Valor |
|---|---|
| **Porta SSH** | `2222` |
| **Usuário admin** | `adriano` |
| **Senha admin** | `BltRst221311` |
| **Home do adriano** | `/home/adriano` |
| **Shell** | `/bin/bash` |

**Comando de acesso:**
```bash
ssh adriano@209.126.125.173 -p 2222
```
> No Windows com PuTTY: `plink.exe -P 2222 adriano@209.126.125.173`

---

## 3. Domínios e Aplicações Web

| URL | Aplicação | DocumentRoot |
|---|---|---|
| `https://empresa.bltrastreamentos.com.br/` | Sistema administrativo BLT | `/var/www/html` |
| `https://cliente.bltrastreamentos.com.br/` | Portal do cliente | `/var/www/html/app` |
| `https://cliente-hmp.bltrastreamentos.com.br/` | Portal mobile homologação | `/var/www/html/app-mobile` |

> **Importante:** Configure o DNS de cada domínio apontando para `209.126.125.173`.  
> Enquanto o DNS não for configurado, o HTTPS só funciona internamente.

---

## 4. Certificado SSL

| Campo | Valor |
|---|---|
| **Tipo** | Autoassinado (self-signed) |
| **Chave privada** | `/etc/ssl/blt/bltrastreamentos.key` (600 root:root) |
| **Certificado** | `/etc/ssl/blt/bltrastreamentos.crt` (644 root:root) |
| **Emitido em** | 18/05/2026 |
| **Válido até** | 15/05/2036 (10 anos) |
| **CN** | `bltrastreamentos.com.br` |
| **SANs cobertos** | `bltrastreamentos.com.br`, `*.bltrastreamentos.com.br`, `empresa.bltrastreamentos.com.br`, `cliente.bltrastreamentos.com.br`, `cliente-hmp.bltrastreamentos.com.br` |

> **Atenção:** Certificado autoassinado exibe aviso de segurança nos navegadores.  
> Para certificado confiável, use Let's Encrypt após apontar o DNS:
> ```bash
> sudo apt install certbot python3-certbot-apache
> sudo certbot --apache -d empresa.bltrastreamentos.com.br -d cliente.bltrastreamentos.com.br -d cliente-hmp.bltrastreamentos.com.br
> ```

**Renovar/recriar certificado SSL:**
```bash
sudo openssl req -x509 -nodes -days 3650 -newkey rsa:2048 \
  -keyout /etc/ssl/blt/bltrastreamentos.key \
  -out /etc/ssl/blt/bltrastreamentos.crt \
  -config /tmp/blt-ssl.cnf
sudo systemctl reload apache2
```

---

## 5. Serviços e Status

| Serviço | Status | Porta/Socket | Iniciado em |
|---|---|---|---|
| `gateway-sunteh` (.NET 8) | ✅ active | UDP `9040` (entrada), HTTP `127.0.0.1:9054` (métricas) | 18/05/2026 19:55 UTC |
| `apache2` | ✅ active | TCP `80` (redirect), TCP `443` (HTTPS) | 18/05/2026 20:27 UTC |
| `php8.3-fpm` | ✅ active | Socket `/run/php/php8.3-fpm-blt.sock` | 18/05/2026 19:59 UTC |
| `postgresql` (16) | ✅ active | TCP `127.0.0.1:5432` (somente local) | 18/05/2026 19:44 UTC |

**Comandos de manutenção de serviços:**
```bash
# Status
sudo systemctl status gateway-sunteh
sudo systemctl status apache2
sudo systemctl status php8.3-fpm
sudo systemctl status postgresql

# Restart
sudo systemctl restart gateway-sunteh
sudo systemctl restart apache2
sudo systemctl restart php8.3-fpm
sudo systemctl restart postgresql

# Ver logs do gateway em tempo real
sudo journalctl -u gateway-sunteh -f

# Ver log de arquivo do gateway
sudo tail -f /opt/gateway-sunteh/log/gateway-atual.log
```

---

## 6. Gateway Sunteh 4G (.NET 8)

### Diretórios
| Finalidade | Caminho |
|---|---|
| **Binários** | `/opt/gateway-sunteh/` |
| **Log atual** | `/opt/gateway-sunteh/log/gateway-atual.log` |
| **Cache de disco** | `/opt/gateway-sunteh/log/cache/` |
| **Logs arquivados** | `/opt/gateway-sunteh/log/arquivados/` |
| **Arquivo systemd** | `/etc/systemd/system/gateway-sunteh.service` |
| **Variáveis de ambiente** | `/etc/default/gateway-sunteh` |

### Variáveis de ambiente (`/etc/default/gateway-sunteh`)
```ini
DOTNET_ENVIRONMENT=Production
Gateway__Udp__Port=9040
Gateway__Metrics__Url=http://127.0.0.1:9054
Gateway__PostgresDatabase__ConnectionString=Host=127.0.0.1;Database=blt_rastro_pg;Username=blt_aplicacao;Password=B1tRst@Pg#2026!Gw;Port=5432
Gateway__PostgresDatabase__ConnectTimeoutSeconds=2
Gateway__PostgresDatabase__CommandTimeoutSeconds=3
Gateway__PostgresDatabase__KeepAliveSeconds=15
Gateway__PostgresDatabase__MaxRetryAttempts=2
Gateway__PostgresDatabase__CircuitOpenSeconds=15
Gateway__Replay__CheckIntervalSeconds=30
Gateway__FileLogging__Directory=log
```

### Endpoints internos (só acessíveis do próprio servidor)
```bash
curl http://127.0.0.1:9054/healthz    # → {"status":"ok","service":"GatewaySunteh4G-NET8"}
curl http://127.0.0.1:9054/metrics    # → métricas Prometheus
```

### Proprietário dos arquivos
- **Usuário do serviço:** `blt_aplicacao` (uid=999, sem login, sem shell)
- **Grupo:** `blt_aplicacao` (gid=988)

### Deploy — recompilar e atualizar o gateway
```powershell
# No Windows (projeto em C:\Users\torresVale\Documents\GatewaySunteh4G-NET8)
dotnet publish -c Release -r linux-x64 --self-contained false -o bin/publish-linux

# Parar serviço
& "C:\Program Files\PuTTY\plink.exe" -P 2222 -pw "BltRst221311" -batch adriano@209.126.125.173 'echo ''BltRst221311'' | sudo -S systemctl stop gateway-sunteh'

# Transferir binários
& "C:\Program Files\PuTTY\pscp.exe" -P 2222 -pw "BltRst221311" -r bin/publish-linux/* adriano@209.126.125.173:/tmp/gateway-new/

# Instalar
& "C:\Program Files\PuTTY\plink.exe" -P 2222 -pw "BltRst221311" -batch adriano@209.126.125.173 'echo ''BltRst221311'' | sudo -S cp /tmp/gateway-new/* /opt/gateway-sunteh/; echo ''BltRst221311'' | sudo -S chown blt_aplicacao:blt_aplicacao /opt/gateway-sunteh/*; echo ''BltRst221311'' | sudo -S systemctl start gateway-sunteh'
```

---

## 7. PostgreSQL 16 + TimescaleDB

| Campo | Valor |
|---|---|
| **Versão** | PostgreSQL 16.13 |
| **TimescaleDB** | 2.26.4 |
| **Host** | `127.0.0.1:5432` (somente local) |
| **Banco de dados** | `blt_rastro_pg` |
| **Config principal** | `/etc/postgresql/16/main/postgresql.conf` |
| **Tuning personalizado** | `/etc/postgresql/16/main/conf.d/99-blt-tuning.conf` |
| **Data directory** | `/var/lib/postgresql/16/main/` |

### Usuários/Roles do PostgreSQL
| Role | Senha | Permissões |
|---|---|---|
| `postgres` | *(autenticação peer — só do sistema operacional)* | Superuser |
| `blt_aplicacao` | `B1tRst@Pg#2026!Gw` | ALL em `blt_rastro_pg` public + sequences |

### Tabelas principais
| Tabela | Registros | Notas |
|---|---|---|
| `position` | 11.727 | Hypertable TimescaleDB (série temporal) |
| `comando` | 0 | Comandos para rastreadores |
| + 40 outras tabelas | — | Schema completo migrado do dev local |

### Conectar ao banco (da linha de comando do servidor)
```bash
# Como postgres (sem senha — peer auth)
sudo -u postgres psql -d blt_rastro_pg

# Como blt_aplicacao (via TCP)
PGPASSWORD='B1tRst@Pg#2026!Gw' psql -h 127.0.0.1 -U blt_aplicacao -d blt_rastro_pg

# Comandos úteis
\dt            -- listar tabelas
\d+ position   -- descrever tabela position
SELECT count(*) FROM position;
SELECT count(*) FROM comando;
```

### Tuning (99-blt-tuning.conf)
```ini
shared_buffers = 3GB
effective_cache_size = 5GB
work_mem = 32MB
maintenance_work_mem = 512MB
max_connections = 80
```

### Backup do banco
```bash
# Backup com suporte a TimescaleDB (recomendado)
sudo -u postgres bash -c "
  psql -d blt_rastro_pg -c 'SELECT timescaledb_pre_restore();'
  pg_dump --no-owner --no-acl -Fc -d blt_rastro_pg -f /tmp/blt_rastro_pg_$(date +%Y%m%d).dump
  psql -d blt_rastro_pg -c 'SELECT timescaledb_post_restore();'
"

# Restaurar
sudo -u postgres bash -c "
  psql -d blt_rastro_pg -c 'SELECT timescaledb_pre_restore();'
  pg_restore --no-owner --no-acl --disable-triggers -d blt_rastro_pg /tmp/blt_rastro_pg_YYYYMMDD.dump
  psql -d blt_rastro_pg -c 'SELECT timescaledb_post_restore();'
"
```

---

## 8. Apache 2.4 + PHP-FPM 8.3

### Configurações
| Arquivo | Caminho |
|---|---|
| **Sites disponíveis** | `/etc/apache2/sites-available/` |
| **Sites habilitados** | `/etc/apache2/sites-enabled/` |
| **Módulos** | `/etc/apache2/mods-enabled/` |
| **Logs de acesso** | `/var/log/apache2/empresa-access.log`, `cliente-access.log`, `cliente-hmp-access.log` |
| **Logs de erro** | `/var/log/apache2/empresa-error.log`, `cliente-error.log`, `cliente-hmp-error.log` |

### Módulos habilitados relevantes
`ssl`, `proxy`, `proxy_fcgi`, `headers`, `rewrite`, `deflate`, `setenvif`

### PHP-FPM Pools
| Pool | Config | Socket | Usuário |
|---|---|---|---|
| `blt` | `/etc/php/8.3/fpm/pool.d/blt.conf` | `/run/php/php8.3-fpm-blt.sock` | `blt_aplicacao` |
| `www` | `/etc/php/8.3/fpm/pool.d/www.conf` | `/run/php/php8.3-fpm.sock` | `www-data` |

> O pool **blt** é o que serve as 3 aplicações PHP. Todos os processos PHP rodam como `blt_aplicacao`.

### Credenciais do banco nas aplicações PHP
Arquivo: `/var/www/html/config/db_config.php` (protegido: 600, blt_aplicacao)
```php
define('DB_DRIVER',       'pgsql');
define('PG_HOST',         '127.0.0.1');
define('PG_USER',         'blt_aplicacao');
define('PG_PASS',         'B1tRst@Pg#2026!Gw');
define('PG_NAME_DEFAULT', 'blt_rastro_pg');
```

### Deploy da aplicação PHP (blt/)
```powershell
# No Windows, a partir de C:\www\blt
# Criar zip (excluindo .git e phpmyadmin)
powershell -File C:\temp\make_blt_zip.ps1

# Transferir
& "C:\Program Files\PuTTY\pscp.exe" -P 2222 -pw "BltRst221311" C:\temp\blt.zip adriano@209.126.125.173:/tmp/blt.zip

# Extrair no servidor
& "C:\Program Files\PuTTY\plink.exe" -P 2222 -pw "BltRst221311" -batch adriano@209.126.125.173 'echo ''BltRst221311'' | sudo -S unzip -o /tmp/blt.zip -d /var/www/html/; echo ''BltRst221311'' | sudo -S chown -R blt_aplicacao:www-data /var/www/html/; echo ''BltRst221311'' | sudo -S find /var/www/html -type d -exec chmod 750 {} +; echo ''BltRst221311'' | sudo -S find /var/www/html -type f -exec chmod 640 {} +'
```

---

## 9. Usuários do Sistema

| Usuário | UID | Shell | Função |
|---|---|---|---|
| `adriano` | 1000 | `/bin/bash` | Admin SSH (sudo completo), senha: `BltRst221311` |
| `blt_aplicacao` | 999 | `/usr/sbin/nologin` | Serviço gateway + PHP-FPM + dono de `/opt/gateway-sunteh` e `/var/www/html` |
| `postgres` | 110 | `/bin/bash` | PostgreSQL (somente peer auth, sem senha SSH) |
| `www-data` | 33 | `/usr/sbin/nologin` | Apache worker process |

---

## 10. Firewall (ufw)

| Porta/Protocolo | Ação | Função |
|---|---|---|
| `2222/tcp` | ALLOW | SSH |
| `80/tcp` | ALLOW | HTTP → redirect HTTPS |
| `443/tcp` | ALLOW | HTTPS (3 aplicações) |
| `9040/udp` | ALLOW | Gateway UDP (rastreadores) |
| `20/tcp`, `21/tcp` | ALLOW | FTP (vsftpd ativo) |
| `990/tcp` | ALLOW | FTPS |
| `40000-50000/tcp` | ALLOW | FTP passivo |
| `9054/tcp` | — | **NÃO exposta** (bind em 127.0.0.1) — métricas/healthz |
| `5432/tcp` | — | **NÃO exposta** (bind em 127.0.0.1) — PostgreSQL |

---

## 11. Estrutura de Diretórios

```
/opt/gateway-sunteh/                   ← aplicação .NET 8
├── GatewaySunteh4G-NET8.dll
├── GatewaySunteh4G-NET8               (executável nativo)
├── appsettings.json
├── appsettings.Development.json
├── Npgsql.dll
├── deploy/
│   ├── systemd/gateway-sunteh.service
│   └── systemd/gateway-sunteh.env
└── log/
    ├── gateway-atual.log              ← log corrente
    ├── cache/                         ← posições em cache de disco
    └── arquivados/                    ← logs rotacionados

/var/www/html/                         ← aplicação web principal (empresa)
├── index.php
├── conexao.php
├── Database.php
├── config/
│   ├── db_config.php                  ← credenciais PostgreSQL (600, blt_aplicacao)
│   └── db_config.example.php
├── app/                               ← portal do cliente
│   ├── index.php
│   ├── conexao.php
│   └── DataBase.php
├── app-mobile/                        ← portal mobile homologação
│   ├── index.php
│   ├── conexao.php
│   └── DataBase.php
├── vendor/                            ← dependências (select2, etc.)
├── css/, js/, images/, fonts/
└── ... (3014 arquivos no total)

/etc/ssl/blt/                          ← certificado SSL
├── bltrastreamentos.crt               (644 — certificado público)
└── bltrastreamentos.key               (600 — chave privada)

/etc/apache2/
├── sites-enabled/
│   ├── empresa.conf  → DocumentRoot /var/www/html
│   ├── cliente.conf  → DocumentRoot /var/www/html/app
│   └── cliente-hmp.conf → DocumentRoot /var/www/html/app-mobile
└── sites-available/   (mesmos arquivos)

/etc/php/8.3/fpm/pool.d/
├── blt.conf          ← pool produção (blt_aplicacao)
└── www.conf          ← pool padrão (www-data)

/etc/postgresql/16/main/
├── postgresql.conf
└── conf.d/
    └── 99-blt-tuning.conf

/etc/systemd/system/
└── gateway-sunteh.service

/etc/default/
└── gateway-sunteh    ← variáveis de ambiente do serviço .NET

/etc/logrotate.d/
└── gateway-sunteh    ← rotação de logs

/etc/security/limits.d/
└── blt_aplicacao.conf   (nofile 65535)

/etc/sysctl.d/
└── 99-gateway.conf  (UDP buffers, swappiness)
```

---

## 12. Resumo de Credenciais

| Sistema | Usuário | Senha | Observação |
|---|---|---|---|
| **SSH** | `adriano` | `BltRst221311` | Porta 2222, sudo completo |
| **PostgreSQL role** | `blt_aplicacao` | `B1tRst@Pg#2026!Gw` | Acesso TCP local ao blt_rastro_pg |
| **PostgreSQL superuser** | `postgres` | *(peer auth)* | Apenas via `sudo -u postgres psql` |
| **PHP app** | `blt_aplicacao` | `B1tRst@Pg#2026!Gw` | Definido em `/var/www/html/config/db_config.php` |
| **Gateway env** | `blt_aplicacao` | `B1tRst@Pg#2026!Gw` | Definido em `/etc/default/gateway-sunteh` |

---

## 13. Verificação Rápida de Saúde

Execute estes comandos no servidor para confirmar que tudo está operacional:

```bash
# Status de todos os serviços
sudo systemctl is-active gateway-sunteh apache2 php8.3-fpm postgresql

# Healthz do gateway
curl http://127.0.0.1:9054/healthz

# Testar PHP nos 3 vhosts internamente
curl -sk -o /dev/null -w "empresa=%{http_code}\n" --resolve empresa.bltrastreamentos.com.br:443:127.0.0.1 https://empresa.bltrastreamentos.com.br/
curl -sk -o /dev/null -w "cliente=%{http_code}\n" --resolve cliente.bltrastreamentos.com.br:443:127.0.0.1 https://cliente.bltrastreamentos.com.br/
curl -sk -o /dev/null -w "hmp=%{http_code}\n" --resolve cliente-hmp.bltrastreamentos.com.br:443:127.0.0.1 https://cliente-hmp.bltrastreamentos.com.br/

# Contar posições no banco
PGPASSWORD='B1tRst@Pg#2026!Gw' psql -h 127.0.0.1 -U blt_aplicacao -d blt_rastro_pg -c 'SELECT count(*) FROM position;'

# Ver socket FPM
ls /run/php/php8.3-fpm-blt.sock
```

---

## 14. Pendências e Recomendações

| Prioridade | Item | Ação necessária |
|---|---|---|
| 🔴 Alta | **DNS dos domínios** | Apontar `empresa`, `cliente`, `cliente-hmp` para `209.126.125.173` no registrador de domínio |
| 🔴 Alta | **Certificado SSL confiável** | Após DNS: `sudo certbot --apache -d empresa.bltrastreamentos.com.br -d cliente.bltrastreamentos.com.br -d cliente-hmp.bltrastreamentos.com.br` |
| 🟡 Média | **Credenciais do adriano** | Considerar troca da senha SSH (`BltRst221311`) após estabilização |
| 🟡 Média | **phpMyAdmin** | Pasta `/var/www/html/phpmyadmin` não foi deployada (segurança). Se necessário, instalar com senha e IP restrito |
| 🟡 Média | **Backup automático** | Configurar cron para `pg_dump` diário + envio para storage externo |
| 🟢 Baixa | **HTTPS para app/app-mobile** | Verificar se `/var/www/html/app/config/` e `/var/www/html/app-mobile/config/` precisam de `db_config.php` próprio |
| 🟢 Baixa | **FTP** | `vsftpd` está ativo (portas 20/21/990). Verificar se está em uso e proteger com SSL/TLS |

---

*Relatório gerado automaticamente em 18/05/2026 por GitHub Copilot.*
