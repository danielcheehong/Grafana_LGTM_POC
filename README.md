# Grafana LGTM Stack POC (Logs + Traces)

A proof of concept demonstrating how to integrate a .NET 9 minimal API with the Grafana LGTM (Loki, Grafana, Tempo, Mimir*) observability stack using OpenTelemetry for **logs** and **distributed traces**.

> *Mimir (metrics) is not yet included; future enhancement.

## 🏗️ Updated Architecture

```
┌─────────────────┐        OTLP (logs + traces)        ┌─────────────────┐
│   .NET API      │ 4317/4318 (gRPC/HTTP)              │     Alloy       │
│ (OpenTelemetry) │ ─────────────────────────────────▶ │ (Collector)     │
│                 │                                     │                │
└─────────────────┘                                     └──────┬─────────┘
        │                                                   │
        │ Logs (Loki exporter)                              │ Traces (OTLP gRPC)
        ▼                                                   ▼
     ┌──────────────┐                                   ┌─────────────────┐
     │     Loki      │                                   │     Tempo       │
     │ (Log Store)   │                                   │ (Trace Store)   │
     └──────┬────────┘                                   └──────┬─────────┘
        │                           Correlation (trace_id)   │
        └──────────────────────┬─────────────────────────────┘
               ▼
             ┌───────────┐
             │  Grafana  │
             │ (Explore) │
             └───────────┘

Ports:
API: 5294 (HTTP) / 7054 (HTTPS)
Alloy: 4317 (OTLP gRPC), 4318 (OTLP HTTP)
Loki: 3100
Tempo: 3200
Grafana: 3000
```

## 🚀 Components

### 1. LgtmOtelApi (.NET 9 Web API)
Framework: ASP.NET Core 9.0

Features:
- Structured logging with OpenTelemetry
- Distributed tracing (ASP.NET Core + HttpClient + manual spans)
- Resource attribution: service.name, deployment.environment, region, version
- Endpoints:
   - `/ping` – emits INFO/WARN/ERROR logs
   - `/trace` – creates a root span + child span and correlated logs (includes `trace_id=` in log lines)

### 2. Grafana Alloy (Collector)
Config: `Lgtm-Setup/config.alloy`

Pipelines:
- OTLP receiver (gRPC/HTTP)
- Logs: attribute enrichment → Loki exporter
- Traces: OTLP gRPC exporter to Tempo

### 3. Loki (Logs)
Stores structured logs with labels derived from OpenTelemetry resource + selected log attributes.

### 4. Tempo (Traces)
Config: `Lgtm-Setup/tempo-config.yaml` – local filesystem backend for spans (dev only).

### 5. Grafana (Explore / Correlation)
Datasource provisioning: `grafana/provisioning/datasources/datasources.yaml` automatically registers Loki + Tempo and enables:
- Logs → Trace (derived field `traceID` via regex `trace_id=(\w+)`)
- Trace → Logs (Traces to Logs mapping with common tags)
- Service map & node graph (Tempo features)

## 📋 Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop)
- [Docker Compose](https://docs.docker.com/compose/)

## 🛠️ Quick Start

```powershell
# 1. Start observability stack
cd Lgtm-Setup
docker compose up -d   # or: docker-compose up -d

# 2. Run API
cd ../LgtmOtelApi
dotnet restore
dotnet run

# 3. Generate logs
curl http://localhost:5294/ping

# 4. Generate trace (returns traceId)
curl http://localhost:5294/trace
```

Then open Grafana: http://localhost:3000 (admin / admin)

Explore → Tempo: find recent trace (filter by Service = LokiOtelApi). From a span choose “View logs”.

Explore → Loki: query `{service_name="LokiOtelApi"}` and click a `traceID` value to pivot to the trace.

## 🔧 Configuration Details

### OpenTelemetry (API)
Resource attributes:
- service.name = LokiOtelApi
- service.version = 1.0.0
- deployment.environment = dev
- region = local

Tracing configuration:
- ASP.NET Core & HttpClient instrumentation
- Manual `ActivitySource("LokiOtelApi.TraceDemo")`
- OTLP exporter (gRPC) → Alloy (`OpenTelemetry:Endpoint` configurable, default http://localhost:4317)

Logging configuration:
- Logs exported via OTLP gRPC to Alloy
- Formatted message + scopes enabled

### Alloy Processing
Labels derived:
- Resource: service.name, deployment.environment, region
- Log attribute: logger.name

Traces forwarded unchanged to Tempo via internal endpoint `tempo:4317`.

### Port Mapping

| Service    | Port(s)              | Purpose                     |
|------------|----------------------|-----------------------------|
| .NET API   | 5294 (HTTP) / 7054   | Sample endpoints            |
| Alloy      | 4317 (gRPC), 4318    | OTLP ingest (logs+traces)   |
| Loki       | 3100                 | Log storage API             |
| Tempo      | 3200                 | Trace query / ingest gRPC*  |
| Grafana    | 3000                 | UI / Explore                |

*Traces sent to Tempo via gRPC exporter endpoint exposed internally on 4317 (default)

## 📊 Sample Queries & Filters

### LogQL (Loki)
```logql
{service_name="LokiOtelApi"}
{service_name="LokiOtelApi"} |= "trace_id"
{service_name="LokiOtelApi"} |= "ERROR"
count_over_time({service_name="LokiOtelApi"} |= "ERROR" [5m])
```

### Tempo Search Fields
- Service: `LokiOtelApi`
- Span name: `TraceEndpoint` or `ChildWork`
- Attribute: `endpoint=/trace`

## 🗂️ Project Structure (Updated)

```
Grafana_LGTM_POC/
├── Grafana_LGTM_POC.sln
├── README.md
├── Lgtm-Setup/
│   ├── docker-compose.yml
│   ├── config.alloy
│   ├── loki-config.yaml
│   ├── tempo-config.yaml
│   └── grafana/
│       └── provisioning/datasources/datasources.yaml
└── LgtmOtelApi/
   ├── Program.cs
   ├── LgtmOtelApi.csproj
   ├── appsettings.json
   ├── appsettings.Development.json
   └── Properties/launchSettings.json
```

## 🔍 Troubleshooting

### Common Issues

| Symptom | Action |
|---------|--------|
| Logs missing | `docker compose logs alloy` – verify OTLP exporter endpoint | 
| Traces missing | Call `/trace`; check Alloy → Tempo exporter `tempo:4317` reachable |
| No log↔trace pivot | Ensure log lines contain `trace_id=` and derived field regex unchanged |
| Grafana shows no datasources | Confirm provisioning file is mounted (see compose volumes) |
| Tempo empty after many calls | Time range too small – widen to 15m in Explore |

### Useful Commands

```powershell
# View all container logs
docker compose logs

# View specific service logs
docker compose logs grafana
docker compose logs loki
docker compose logs tempo
docker compose logs alloy

# Restart a service
docker compose restart alloy

# Clean shutdown
docker compose down
```

## 🚀 Next Steps

Implemented: Loki (logs) + Tempo (traces). Upcoming ideas:
1. Add metrics (Prometheus receiver → Mimir) with exemplars linking traces.
2. Add custom dashboards (latency, error rate, log volume per service).
3. Introduce downstream dependency call to demonstrate distributed trace propagation.
4. Add semantic conventions (http.*, net.peer.*, exception.*) enrichment.
5. Add alerting (Grafana Alerting or Loki/Tempo rules) for error rate & latency SLOs.

## 📚 References

- Grafana Alloy: https://grafana.com/docs/alloy/
- Grafana Loki: https://grafana.com/docs/loki/
- Grafana Tempo: https://grafana.com/docs/tempo/
- OpenTelemetry .NET: https://opentelemetry.io/docs/languages/net/
- Grafana: https://grafana.com/docs/grafana/

## 📄 License

Proof of concept for demonstration purposes.
