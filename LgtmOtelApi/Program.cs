/// <summary>
/// ASP.NET Core Web API application configured with OpenTelemetry observability.
/// Implements the LGTM (Loki, Grafana, Tempo, Mimir) observability stack with:
/// - Distributed tracing via OpenTelemetry traces exported to OTLP endpoint
/// - Metrics collection using OpenTelemetry meters for HTTP requests, duration, and active connections
/// - Structured logging with OpenTelemetry logs exported to OTLP endpoint
/// - Three demonstration endpoints: /ping, /trace, and /metrics-test
/// All telemetry data is exported to a configurable OTLP endpoint (default: localhost:4317)
/// for integration with Grafana Alloy and the LGTM observability stack.
/// </summary>
using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Exporter;

var builder = WebApplication.CreateBuilder(args);

// Identify your service (these become resource attributes)
var resource = ResourceBuilder.CreateDefault()
    .AddService(serviceName: "LokiOtelApi", serviceVersion: "1.0.0")
    .AddAttributes(new[]
    {
        new KeyValuePair<string, object>("deployment.environment", "dev"),
        new KeyValuePair<string, object>("region", "local")
    });

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Create metrics
var meter = new Meter("LokiOtelApi.Metrics", "1.0.0");
var requestCounter = meter.CreateCounter<int>("http_requests_total", "The total number of HTTP requests");
var requestDuration = meter.CreateHistogram<double>("http_request_duration_seconds", "The duration of HTTP requests");
var activeConnections = meter.CreateUpDownCounter<int>("active_connections", "The number of active connections");

// Register the meter as a singleton for DI
builder.Services.AddSingleton(meter);
builder.Services.AddSingleton(requestCounter);
builder.Services.AddSingleton(requestDuration);
builder.Services.AddSingleton(activeConnections);

// OpenTelemetry Tracing (ASP.NET Core + HttpClient + manual source)
builder.Services.AddOpenTelemetry()
    .WithTracing(tp =>
    {
        tp.SetResourceBuilder(resource)
          .AddAspNetCoreInstrumentation(o =>
          {
              o.RecordException = true;
          })
          .AddHttpClientInstrumentation()
          .AddSource("LokiOtelApi.TraceDemo")
          .AddOtlpExporter(o =>
          {
              var otlpEndpoint = builder.Configuration.GetValue<string>("OpenTelemetry:Endpoint") ?? "http://localhost:4317";
              o.Endpoint = new Uri(otlpEndpoint);
              o.Protocol = OtlpExportProtocol.Grpc;
          });
    })
    .WithMetrics(mp =>
    {
        mp.SetResourceBuilder(resource)
          .AddAspNetCoreInstrumentation()
          .AddHttpClientInstrumentation()
          .AddMeter("LokiOtelApi.Metrics")
          .AddOtlpExporter(o =>
          {
              var otlpEndpoint = builder.Configuration.GetValue<string>("OpenTelemetry:Endpoint") ?? "http://localhost:4317";
              o.Endpoint = new Uri(otlpEndpoint);
              o.Protocol = OtlpExportProtocol.Grpc;
          });
    });

// Route ASP.NET Core logs to OpenTelemetry → OTLP (Alloy)
builder.Logging.ClearProviders();
builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
    options.SetResourceBuilder(resource);

    // Send logs to Alloy (gRPC exporter)
    options.AddOtlpExporter(o =>
    {
        var otlpEndpoint = builder.Configuration.GetValue<string>("OpenTelemetry:Endpoint") ?? "http://localhost:4317"; // Alloy in docker
        o.Endpoint = new Uri(otlpEndpoint); // ensure endpoint applied
        o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
        // For HTTP alternative, comment above and uncomment:
        // o.Endpoint = new Uri("http://localhost:4318");
        // o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/ping", (ILoggerFactory lf, Counter<int> requestCounter, Histogram<double> requestDuration) =>
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var log = lf.CreateLogger("Demo");
    try
    {
        // Record metrics
        requestCounter.Add(1, new KeyValuePair<string, object?>("endpoint", "/ping"));
        
        log.LogInformation("Ping hit at {Time}", DateTimeOffset.UtcNow);
        log.LogWarning("Sample warning with userId={UserId}", 42);
        log.LogError("Sample error to test severity mapping");
        
        stopwatch.Stop();
        requestDuration.Record(stopwatch.Elapsed.TotalSeconds, 
            new KeyValuePair<string, object?>("endpoint", "/ping"),
            new KeyValuePair<string, object?>("status", "success"));
            
        return Results.Ok(new { ok = true, at = DateTimeOffset.UtcNow });
    }
    catch (Exception ex)
    {
        stopwatch.Stop();
        requestDuration.Record(stopwatch.Elapsed.TotalSeconds, 
            new KeyValuePair<string, object?>("endpoint", "/ping"),
            new KeyValuePair<string, object?>("status", "error"));
            
        log.LogError(ex, "Error processing ping request");
        return Results.Problem("Internal server error");
    }
});

// Manual tracing endpoint
var activitySource = new ActivitySource("LokiOtelApi.TraceDemo");
app.MapGet("/trace", async (ILogger<Program> log) =>
{
    using var root = activitySource.StartActivity("TraceEndpoint", ActivityKind.Server);
    root?.SetTag("endpoint", "/trace");
    log.LogInformation("Handling /trace request trace_id={TraceId}", root?.TraceId.ToString());

    using (var child = activitySource.StartActivity("ChildWork", ActivityKind.Internal))
    {
        child?.SetTag("work.stage", "child");
        await Task.Delay(50);
    }

    log.LogInformation("Finished /trace trace_id={TraceId}", root?.TraceId.ToString());
    return Results.Ok(new { ok = true, traceId = root?.TraceId.ToString() });
});

// Metrics test endpoint
app.MapGet("/metrics-test", (
    ILogger<Program> log, 
    Counter<int> requestCounter, 
    Histogram<double> requestDuration,
    UpDownCounter<int> activeConnections) =>
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var random = new Random();
    
    try
    {
        // Simulate active connection
        activeConnections.Add(1);
        
        // Record request
        requestCounter.Add(1, new KeyValuePair<string, object?>("endpoint", "/metrics-test"));
        
        // Simulate some work with random duration
        var workDuration = random.Next(50, 500);
        Thread.Sleep(workDuration);
        
        // Generate random business metrics
        var orderCount = random.Next(1, 10);
        var revenue = random.NextDouble() * 1000;
        
        // Create custom metrics using tags
        for (int i = 0; i < orderCount; i++)
        {
            requestCounter.Add(1, 
                new KeyValuePair<string, object?>("metric_type", "order_processed"),
                new KeyValuePair<string, object?>("customer_type", random.Next(0, 2) == 0 ? "premium" : "standard"));
        }
        
        log.LogInformation("Metrics test completed - Orders: {OrderCount}, Revenue: ${Revenue:F2}, Duration: {Duration}ms", 
            orderCount, revenue, workDuration);
        
        stopwatch.Stop();
        requestDuration.Record(stopwatch.Elapsed.TotalSeconds,
            new KeyValuePair<string, object?>("endpoint", "/metrics-test"),
            new KeyValuePair<string, object?>("status", "success"));
        
        // Simulate connection closed
        activeConnections.Add(-1);
        
        return Results.Ok(new { 
            ok = true, 
            ordersProcessed = orderCount,
            revenue = Math.Round(revenue, 2),
            processingTimeMs = workDuration,
            at = DateTimeOffset.UtcNow 
        });
    }
    catch (Exception ex)
    {
        stopwatch.Stop();
        requestDuration.Record(stopwatch.Elapsed.TotalSeconds,
            new KeyValuePair<string, object?>("endpoint", "/metrics-test"),
            new KeyValuePair<string, object?>("status", "error"));
        
        activeConnections.Add(-1); // Ensure connection is decremented
        
        log.LogError(ex, "Error in metrics test endpoint");
        return Results.Problem("Internal server error");
    }
});

app.Run();

