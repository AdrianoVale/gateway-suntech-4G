using GatewaySunteh4G_NET8.Options;
using GatewaySunteh4G_NET8.Services;
using GatewaySunteh4G_NET8.Services.Logging;
using GatewaySunteh4G_NET8.Workers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
	options.TimestampFormat = "dd/MM/yyyy HH:mm:ss ";
	options.SingleLine = true;
});
builder.Logging.AddDebug();
builder.Logging.AddDailyFileLogger();

builder.Host.ConfigureHostOptions(options =>
{
	options.ShutdownTimeout = TimeSpan.FromSeconds(20);
});

builder.Services.AddOptions<GatewayOptions>()
	.Bind(builder.Configuration.GetSection(GatewayOptions.SectionName))
	.ValidateDataAnnotations()
	.ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IGatewayMetrics, GatewayMetrics>();
builder.Services.AddSingleton<IDeviceRegistry, DeviceRegistry>();
builder.Services.AddSingleton<ICommandRegistry, CommandRegistry>();
builder.Services.AddSingleton<IUdpTransport, UdpTransport>();
builder.Services.AddSingleton<IGatewayDataService, PostgresDataService>();
builder.Services.AddSingleton<IPostgresDataService>(sp => (IPostgresDataService)sp.GetRequiredService<IGatewayDataService>());
builder.Services.AddSingleton<IDiskCacheStore, DiskCacheStore>();
builder.Services.AddSingleton<IPositionPersistenceService, PositionPersistenceService>();
builder.Services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
builder.Services.AddKeyedSingleton<ICommandDispatcher>("postgres", (sp, _) =>
    new CommandDispatcher(
        sp.GetRequiredService<ILogger<CommandDispatcher>>(),
        sp.GetRequiredService<IPostgresDataService>(),
        sp.GetRequiredService<IDeviceRegistry>(),
        sp.GetRequiredService<ICommandRegistry>(),
        sp.GetRequiredService<IUdpTransport>(),
        sp.GetRequiredService<IGatewayMetrics>(),
        sp.GetRequiredService<TimeProvider>(),
        sp.GetRequiredService<IOptions<GatewayOptions>>(),
        commandHubPublisher: sp.GetRequiredService<ICommandHubPublisher>(),
        postgresDataService: null));
builder.Services.AddSingleton<IGatewayPacketProcessor, St4315PacketProcessor>();

// ── SignalR Hub + JWT (ativados apenas quando Hub.Enabled = true) ───────────
var hubEnabled = builder.Configuration.GetValue<bool>("Gateway:Hub:Enabled");
if (hubEnabled)
{
    var jwtSecret  = builder.Configuration["Gateway:Hub:JwtSecret"] ?? string.Empty;
    var jwtIssuer  = builder.Configuration["Gateway:Hub:JwtIssuer"]  ?? "blt-php";
    var jwtAudience = builder.Configuration["Gateway:Hub:JwtAudience"] ?? "gateway-pos";

    builder.Services.AddMemoryCache();
    builder.Services.AddSingleton<IPermissionQueryService, PermissionQueryService>();
    builder.Services.AddSingleton<IPositionHubPublisher, PositionHubPublisher>();
    builder.Services.AddSingleton<ICommandHubPublisher, CommandHubPublisher>();

    builder.Services.AddSignalR();

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.MapInboundClaims = false; // mantém claim "sub" como "sub", não remapeia para ClaimTypes.NameIdentifier
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidateAudience         = true,
                ValidateLifetime         = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer              = jwtIssuer,
                ValidAudience            = jwtAudience,
                IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                ClockSkew                = TimeSpan.FromSeconds(30)
            };
            // SignalR envia o token via query string para WebSocket
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    var token = ctx.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(token) &&
                        ctx.HttpContext.Request.Path.StartsWithSegments("/hub/posicoes"))
                    {
                        ctx.Token = token;
                    }
                    return Task.CompletedTask;
                }
            };
        });

    builder.Services.AddAuthorization();
}
else
{
    // Hub desativado: publishers no-op para não quebrar injeção no processador
    builder.Services.AddSingleton<IPositionHubPublisher, NullPositionHubPublisher>();
    builder.Services.AddSingleton<ICommandHubPublisher, NullCommandHubPublisher>();
}

builder.Services.AddHostedService<UdpGatewayWorker>();
builder.Services.AddHostedService<InactiveDeviceCleanupWorker>();
builder.Services.AddHostedService<CommandPollingWorker>();
builder.Services.AddHostedService<ReplayWorker>();
builder.Services.AddHostedService<PositionMaintenanceWorker>();

var app = builder.Build();
var gatewayOptions = app.Services.GetRequiredService<IOptions<GatewayOptions>>().Value;

app.Urls.Clear();
app.Urls.Add(gatewayOptions.Metrics.Url);
if (gatewayOptions.Hub.Enabled)
{
    app.Urls.Add(gatewayOptions.Hub.Url);
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "GatewaySunteh4G-NET8" }));
app.MapGet("/metrics", (IGatewayMetrics metrics) => Results.Text(metrics.RenderPrometheus(), "text/plain; version=0.0.4; charset=utf-8"));

if (gatewayOptions.Hub.Enabled)
{
    app.MapHub<GatewaySunteh4G_NET8.Hubs.PositionHub>("/hub/posicoes");
}

await app.RunAsync();
