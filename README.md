# Grafana LGTM Stack POC

A proof of concept demonstrating how to integrate .NET applications with the Grafana LGTM (Loki, Grafana, Tempo, Mimir) observability stack using OpenTelemetry.

## 🏗️ Architecture Overview

This project showcases a complete observability pipeline:

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   .NET API      │    │     Alloy       │    │      Loki       │    │    Grafana      │
│  (OpenTelemetry)│───▶│  (Collector)    │───▶│   (Log Store)   │───▶│ (Visualization) │
│                 │    │                 │    │                 │    │                 │
└─────────────────┘    └─────────────────┘    └─────────────────┘    └─────────────────┘
        │                        │                        │                        │
        │                        │                        │                        │
    Port 5294                 Port 4317/4318          Port 3100              Port 3000
   (HTTP/HTTPS)                (OTLP gRPC/HTTP)       (Loki API)         (Grafana UI)
```

## 🚀 Components

### 1. LgtmOtelApi (.NET 9 Web API)
- **Framework**: ASP.NET Core 9.0
- **Purpose**: Sample API with OpenTelemetry integration
- **Features**:
  - Structured logging with OpenTelemetry
  - OTLP (OpenTelemetry Protocol) export to Alloy
  - Resource attribution (service name, environment, region)
  - Sample endpoints for testing log generation

### 2. Grafana Alloy (OpenTelemetry Collector)
- **Purpose**: Receives telemetry data from applications and forwards to Loki
- **Configuration**: `config.alloy`
- **Features**:
  - OTLP receiver (gRPC on port 4317, HTTP on port 4318)
  - Attribute processing for Loki label conversion
  - Log export to Loki

### 3. Loki (Log Aggregation)
- **Version**: 3.1.1
- **Purpose**: Stores and indexes log data
- **Storage**: Local filesystem (development setup)

### 4. Grafana (Visualization)
- **Version**: 11.1.4
- **Purpose**: Dashboard and log exploration
- **Default Credentials**: admin/admin

## 📋 Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop)
- [Docker Compose](https://docs.docker.com/compose/)

## 🛠️ Quick Start

### 1. Start the Observability Stack

```powershell
# Navigate to the setup directory
cd Lgtm-Setup

# Start all services
docker-compose up -d
```

### 2. Run the .NET API

```powershell
# Navigate to the API directory
cd LgtmOtelApi

# Restore dependencies and run
dotnet restore
dotnet run
```

### 3. Generate Some Logs

```powershell
# Test the ping endpoint to generate logs
curl http://localhost:5294/ping
```

### 4. View Logs in Grafana

1. Open Grafana: http://localhost:3000
2. Login with `admin/admin`
3. Navigate to "Explore" 
4. Select "Loki" as the data source
5. Query your logs with filters like: `{service_name="LokiOtelApi"}`

## 🔧 Configuration Details

### OpenTelemetry Configuration

The API is configured to send logs with the following resource attributes:
- **Service Name**: `LokiOtelApi`
- **Service Version**: `1.0.0`
- **Environment**: `dev`
- **Region**: `local`

### Alloy Processing

Alloy converts the following attributes to Loki labels:
- **Resource Attributes**: `service.name`, `deployment.environment`, `region`
- **Log Attributes**: `logger.name`

### Port Mapping

| Service | Port | Purpose |
|---------|------|---------|
| .NET API | 5294 (HTTP), 7054 (HTTPS) | Application endpoints |
| Alloy | 4317 | OTLP gRPC receiver |
| Alloy | 4318 | OTLP HTTP receiver |
| Loki | 3100 | Loki API |
| Grafana | 3000 | Web UI |

## 📊 Sample Queries

### Grafana/Loki LogQL Examples

```logql
# All logs from the API
{service_name="LokiOtelApi"}

# Filter by log level
{service_name="LokiOtelApi"} |= "ERROR"

# Filter by logger name
{service_name="LokiOtelApi", logger_name="Demo"}

# Count errors over time
count_over_time({service_name="LokiOtelApi"} |= "ERROR" [5m])
```

## 🗂️ Project Structure

```
Grafana_LGTM_POC/
├── Grafana_LGTM_POC.sln           # Solution file
├── README.md                       # This file
├── Lgtm-Setup/                    # Docker infrastructure
│   ├── docker-compose.yml         # Services orchestration
│   ├── config.alloy               # Alloy configuration
│   └── loki-config.yaml           # Loki configuration
└── LgtmOtelApi/                   # .NET Web API
    ├── Program.cs                  # Application entry point
    ├── LgtmOtelApi.csproj         # Project file
    ├── appsettings.json           # Application configuration
    ├── appsettings.Development.json
    └── Properties/
        └── launchSettings.json     # Launch profiles
```

## 🔍 Troubleshooting

### Common Issues

1. **Logs not appearing in Grafana**
   - Verify all containers are running: `docker-compose ps`
   - Check Alloy logs: `docker-compose logs alloy`
   - Ensure the .NET API is sending to the correct endpoint

2. **Connection refused errors**
   - Make sure Docker services are fully started
   - Check port conflicts with other running services

3. **No data in Loki**
   - Verify Loki configuration and storage permissions
   - Check Alloy → Loki connectivity

### Useful Commands

```powershell
# View all container logs
docker-compose logs

# View specific service logs
docker-compose logs grafana
docker-compose logs loki
docker-compose logs alloy

# Restart specific service
docker-compose restart alloy

# Clean shutdown
docker-compose down
```

## 🚀 Next Steps

This POC demonstrates the "L" (Loki) portion of the LGTM stack. To complete the observability picture, consider adding:

1. **Tempo** for distributed tracing
2. **Mimir** for metrics collection
3. **Additional instrumentation** (HTTP requests, database calls, etc.)
4. **Custom dashboards** in Grafana
5. **Alerting rules** based on log patterns

## 📚 Learn More

- [Grafana Alloy Documentation](https://grafana.com/docs/alloy/)
- [Loki Documentation](https://grafana.com/docs/loki/)
- [OpenTelemetry .NET Documentation](https://opentelemetry.io/docs/languages/net/)
- [Grafana Documentation](https://grafana.com/docs/grafana/)

## 📄 License

This is a proof of concept project for demonstration purposes.
