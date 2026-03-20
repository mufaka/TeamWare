# Deploying TeamWare as a systemd Service

This guide covers publishing the TeamWare application and running it as a systemd service on a Linux host.

---

## Prerequisites

| Requirement | Notes |
|-------------|-------|
| .NET 10 SDK or Runtime | Install from <https://dot.net/download> or use the `dotnet-install` script. |
| Linux host with systemd | Ubuntu 22.04+, Debian 12+, RHEL 9+, etc. |
| A dedicated service account | Runs the app without root privileges. |

---

## 1. Publish the Application

On your build machine, produce a release build:

```bash
dotnet publish TeamWare.Web/TeamWare.Web.csproj \
  -c Release \
  -o ./publish
```

> **Tip:** Add `--self-contained` and `-r linux-x64` if the target host will not have the .NET runtime installed.

Create the target directory on the remote host and copy the published files:

```bash
ssh user@host 'sudo mkdir -p /opt/teamware'
scp -r ./publish/* user@host:/opt/teamware
```

---

## 2. Create a Service Account

```bash
sudo useradd --system --no-create-home --shell /usr/sbin/nologin teamware
```

Set ownership of the application directory:

```bash
sudo chown -R teamware:teamware /opt/teamware
```

---

## 3. Create the systemd Unit File

Create `/etc/systemd/system/teamware.service`:

```ini
[Unit]
Description=TeamWare Web Application
After=network.target

[Service]
Type=notify
User=teamware
Group=teamware
WorkingDirectory=/opt/teamware
ExecStart=/opt/teamware/TeamWare.Web
Restart=on-failure
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=teamware

# Environment
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://+:5000
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

# Hardening
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/opt/teamware

[Install]
WantedBy=multi-user.target
```

> **Note:** The `ReadWritePaths` directive allows the app to write its SQLite database and any log files within `/opt/teamware`. Adjust the path if you store data elsewhere.

If you published as a **framework-dependent** deployment (no `--self-contained`), change `ExecStart` to:

```ini
ExecStart=/usr/bin/dotnet /opt/teamware/TeamWare.Web.dll
```

---

## 4. Enable and Start the Service

```bash
# Reload systemd so it picks up the new unit file
sudo systemctl daemon-reload

# Enable the service to start on boot
sudo systemctl enable teamware

# Start the service now
sudo systemctl start teamware
```

---

## 5. Verify the Service

```bash
# Check status
sudo systemctl status teamware

# Follow live logs
sudo journalctl -u teamware -f
```

You should see the application listening on `http://+:5000`.

---

## 6. (Optional) Reverse Proxy with Nginx

It is recommended to place a reverse proxy in front of Kestrel for TLS termination and public exposure.

Install Nginx:

```bash
sudo apt install nginx   # Debian/Ubuntu
```

Create `/etc/nginx/sites-available/teamware`:

```nginx
server {
    listen 80;
    server_name your-domain.com;

    location / {
        proxy_pass         http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header   Upgrade $http_upgrade;
        proxy_set_header   Connection "upgrade";
        proxy_set_header   Host $host;
        proxy_set_header   X-Real-IP $remote_addr;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
    }
}
```

Enable the site and restart Nginx:

```bash
sudo ln -s /etc/nginx/sites-available/teamware /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl restart nginx
```

Add TLS with Let's Encrypt:

```bash
sudo apt install certbot python3-certbot-nginx
sudo certbot --nginx -d your-domain.com
```

---

## Common Commands

| Action | Command |
|--------|---------|
| Start | `sudo systemctl start teamware` |
| Stop | `sudo systemctl stop teamware` |
| Restart | `sudo systemctl restart teamware` |
| View logs | `sudo journalctl -u teamware -n 100` |
| Check status | `sudo systemctl status teamware` |

---

## Updating the Application

```bash
sudo systemctl stop teamware
# Copy new published files to /opt/teamware
sudo chown -R teamware:teamware /opt/teamware
sudo systemctl start teamware
```
