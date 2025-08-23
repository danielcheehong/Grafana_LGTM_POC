using System.Diagnostics;
using OpenTelemetry.Logs;
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

app.MapGet("/ping", (ILoggerFactory lf) =>
{
    var log = lf.CreateLogger("Demo");
    try
    {
        log.LogInformation("Ping hit at {Time}", DateTimeOffset.UtcNow);
        log.LogWarning("Sample warning with userId={UserId}", 42);
        log.LogError("Sample error to test severity mapping");
        return Results.Ok(new { ok = true, at = DateTimeOffset.UtcNow });
    }
    catch (Exception ex)
    {
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

app.Run();

