using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Medora.Api.Endpoints;
using Medora.Api.Infrastructure;
using Medora.Application;
using Medora.Application.Abstractions;
using Medora.Application.Features.Identity;
using Medora.Domain.Identity;
using Medora.Infrastructure;
using Medora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateSlimBuilder(args);
var config = builder.Configuration;

// ---------------------------------------------------------------- Kestrel & JSON
builder.WebHost.ConfigureKestrel(k =>
{
    k.AddServerHeader = false;
    k.Limits.MaxRequestBodySize = 64 * 1024; // JSON API: prescriptions carry an image REFERENCE, never bytes
});

// Windows/IIS hosting (e.g. MonsterASP.NET): the slim builder leaves IIS out. Both calls are no-ops
// outside IIS (Docker/Linux keep using Kestrel), and the body limit mirrors Kestrel's under IIS.
builder.WebHost.UseIIS();
builder.WebHost.UseIISIntegration();
builder.Services.Configure<IISServerOptions>(o => o.MaxRequestBodySize = 64 * 1024);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    // Wire contract: unset optional fields are omitted (the app's Zod schemas use .optional()).
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// ---------------------------------------------------------------- Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(config);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddOptions<IdentitySettings>().Bind(config.GetSection(IdentitySettings.Section));

// ---------------------------------------------------------------- AuthN / AuthZ
var jwt = config.GetSection(JwtSettings.Section).Get<JwtSettings>() ?? new JwtSettings();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = jwt.SigningKey.Length >= 32 ? jwt.Key() : null,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "sub",
        RoleClaimType = "role",
    };
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Staff, p => p.RequireClaim("role", Roles.Pharmacist, Roles.Admin))
    .AddPolicy(Policies.Admin, p => p.RequireClaim("role", Roles.Admin));

// ---------------------------------------------------------------- Rate limiting (per user when signed in, else per IP)
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (ctx, _) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)) ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ValueTask.CompletedTask;
    };
    static string Partition(HttpContext c) => c.User.FindFirst("sub")?.Value ?? c.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c =>
        RateLimitPartition.GetTokenBucketLimiter(Partition(c), _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 300, TokensPerPeriod = 100, ReplenishmentPeriod = TimeSpan.FromSeconds(10), QueueLimit = 0, AutoReplenishment = true,
        }));
    o.AddPolicy("auth", c => RateLimitPartition.GetFixedWindowLimiter(Partition(c), _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    o.AddPolicy("writes", c => RateLimitPartition.GetSlidingWindowLimiter(Partition(c), _ => new SlidingWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0 }));
    // AI calls cost money and seconds: a tight per-user budget, with a short queue to absorb double-taps.
    o.AddPolicy("ai", c => RateLimitPartition.GetTokenBucketLimiter(Partition(c), _ => new TokenBucketRateLimiterOptions
    {
        TokenLimit = 10, TokensPerPeriod = 10, ReplenishmentPeriod = TimeSpan.FromMinutes(1), QueueLimit = 2, QueueProcessingOrder = QueueProcessingOrder.OldestFirst, AutoReplenishment = true,
    }));
});

// ---------------------------------------------------------------- Caching, compression, CORS (Expo web dev only; native apps don't use CORS)
builder.Services.AddOutputCache(o => o.AddPolicy("catalog", p => p.Expire(TimeSpan.FromSeconds(60)).SetVaryByQuery("*").Tag("catalog")));
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
    o.MimeTypes = ["application/json", "application/problem+json"];
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
var corsOrigins = config.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("ETag", "X-Correlation-Id", "Retry-After")));

// ---------------------------------------------------------------- Errors, docs, health, telemetry
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("medora-api"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health")).AddHttpClientInstrumentation().AddSource("Microsoft.EntityFrameworkCore"))
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddMeter("Microsoft.EntityFrameworkCore"));
if (!string.IsNullOrWhiteSpace(config["OTEL_EXPORTER_OTLP_ENDPOINT"])) builder.Services.AddOpenTelemetry().UseOtlpExporter();

var app = builder.Build();

// ---------------------------------------------------------------- Database
if (config.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().MigrateAndSeedAsync(config.GetValue("Database:SeedDemoData", false), CancellationToken.None);
}

// ---------------------------------------------------------------- Pipeline (order matters)
app.UseExceptionHandler();
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseResponseCompression();      // outermost body transform: compresses after ETag computed on the raw JSON
app.UseMiddleware<ETagMiddleware>();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseOutputCache();

app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).DisableRateLimiting();
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") }).DisableRateLimiting();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(o => o.WithTitle("Medora API"));
}

var api = app.MapGroup("/api/v1");
api.MapAuth();
api.MapCatalog();
api.MapCustomer();
api.MapStaff();

await app.RunAsync();

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
