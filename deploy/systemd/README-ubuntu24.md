# Deploy systemd - Ubuntu 24.04

## 1) Preparar diretorios e usuario dedicado

```bash
sudo useradd -r -m -d /home/adriano -s /usr/sbin/nologin adriano || true
sudo mkdir -p /opt/gateway-sunteh
sudo mkdir -p /opt/gateway-sunteh/log/arquivados
sudo chown -R adriano:adriano /opt/gateway-sunteh
```

## 2) Copiar publicacao Linux

```bash
# Da maquina de build, copie conteudo de bin/publish-linux para /opt/gateway-sunteh
# Exemplo local:
sudo cp -r ./bin/publish-linux/* /opt/gateway-sunteh/
sudo chown -R adriano:adriano /opt/gateway-sunteh
```

## 3) Instalar unit file e environment

```bash
sudo cp deploy/systemd/gateway-sunteh.service /etc/systemd/system/gateway-sunteh.service
sudo cp deploy/systemd/gateway-sunteh.env /etc/default/gateway-sunteh
sudo chown root:root /etc/systemd/system/gateway-sunteh.service /etc/default/gateway-sunteh
sudo chmod 644 /etc/systemd/system/gateway-sunteh.service /etc/default/gateway-sunteh
```

Edite segredos em `/etc/default/gateway-sunteh` (principalmente `Gateway__PostgresDatabase__ConnectionString`).

## 4) Habilitar e iniciar

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now gateway-sunteh
sudo systemctl status gateway-sunteh --no-pager
```

## 5) Acompanhar logs

```bash
# Journal do servico
journalctl -u gateway-sunteh -f

# Log da aplicacao em arquivo
tail -f /opt/gateway-sunteh/log/gateway-atual.log
```

## 6) Rotacao operacional recomendada (extra)

A aplicacao ja faz rotacao diaria + retencao de 3 dias em `log/arquivados`.
Como camada extra de seguranca para o arquivo ativo:

```bash
sudo cp deploy/logrotate/gateway-sunteh-journal.conf /etc/logrotate.d/gateway-sunteh
sudo chown root:root /etc/logrotate.d/gateway-sunteh
sudo chmod 644 /etc/logrotate.d/gateway-sunteh
sudo logrotate -d /etc/logrotate.d/gateway-sunteh
```
