using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

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

// Route ASP.NET Core logs to OpenTelemetry → OTLP (Alloy)
builder.Logging.ClearProviders();
builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
    options.SetResourceBuilder(resource);

    // Send logs to Alloy (choose gRPC OR HTTP)
    options.AddOtlpExporter(o =>
    {
        // gRPC (4317)—uncomment one:
        var otlpEndpoint = builder.Configuration.GetValue<string>("OpenTelemetry:Endpoint") ?? "http://localhost:4317"; // Alloy in docker
        o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;

        // // HTTP (4318) alternative:
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

app.Run();

