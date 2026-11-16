using Medora.Application.Abstractions;
using Medora.Application.Features.Ai;
using Medora.Infrastructure.Ai;
using Medora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Medora.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Medora")
            ?? throw new InvalidOperationException("ConnectionStrings:Medora is not configured.");

        // DbContext POOLING: contexts are reset and reused instead of allocated per request.
        services.AddDbContextPool<MedoraDbContext>(o => ConfigureSqlServer(o, connectionString), poolSize: 256);
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<MedoraDbContext>());
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IStockLedger, SqlStockLedger>();
        services.AddScoped<ISlotLock, SqlSlotLock>();
        services.AddScoped<DatabaseSeeder>();

        services.AddOptions<JwtSettings>().Bind(configuration.GetSection(JwtSettings.Section)).ValidateOnStart()
            .Validate(s => s.SigningKey.Length >= 32, "Jwt:SigningKey must be at least 32 characters.");
        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<IPaymentGateway, MockPaymentGateway>();

        services.AddHybridCache(o =>
        {
            o.MaximumPayloadBytes = 1024 * 1024;
            o.DefaultEntryOptions = new() { Expiration = TimeSpan.FromMinutes(10), LocalCacheExpiration = TimeSpan.FromMinutes(2) };
        });
        services.AddSingleton<ICatalogCache, HybridCatalogCache>();

        // AI (staff features). The provider is configuration: Ai:Provider = openai | claude | none.
        // Whatever the provider, the services degrade to their deterministic results when it's off or down.
        services.AddSingleton<IKnowledgeIndexProvider, KnowledgeIndexProvider>();
        services.AddOptions<AiSettings>().Bind(configuration.GetSection(AiSettings.Section));
        services.AddOptions<ClaudeSettings>().Bind(configuration.GetSection(ClaudeSettings.Section));
        var ai = configuration.GetSection(AiSettings.Section).Get<AiSettings>() ?? new AiSettings();
        switch (ai.Provider.Trim().ToLowerInvariant())
        {
            case "openai": // any OpenAI-compatible endpoint: Ollama / LM Studio / vLLM locally, or a hosted provider
                services.AddHttpClient<ChatCompletionsClient>(c => c.Timeout = ai.Timeout);
                services.AddScoped<IClinicalLlm, OpenAiClinicalLlm>();
                services.AddScoped<IAnalyticsLlm, OpenAiAnalyticsLlm>();
                break;
            case "claude":
                services.AddSingleton<ClaudeClientHolder>();
                services.AddSingleton<IClinicalLlm, ClaudeClinicalLlm>();
                services.AddSingleton<IAnalyticsLlm, ClaudeAnalyticsLlm>();
                break;
            default:
                services.AddSingleton<DisabledLlm>();
                services.AddSingleton<IClinicalLlm>(sp => sp.GetRequiredService<DisabledLlm>());
                services.AddSingleton<IAnalyticsLlm>(sp => sp.GetRequiredService<DisabledLlm>());
                break;
        }

        services.AddSingleton<IEmailSender, LoggingEmailSender>();
        services.AddHostedService<HoldExpiryWorker>();
        services.AddHostedService<OutboxDispatcher>();

        services.AddHealthChecks().AddDbContextCheck<MedoraDbContext>("database", tags: ["ready"]);
        return services;
    }

    internal static void ConfigureSqlServer(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString, sql =>
        {
            sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
            sql.CommandTimeout(15);
            sql.UseCompatibilityLevel(150); // SQL Server 2019+ / Azure SQL: primitive collections as JSON
            sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery);
        });
}

/// <summary>Lets `dotnet ef migrations add` build the model without starting the API.</summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<MedoraDbContext>
{
    public MedoraDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<MedoraDbContext>();
        DependencyInjection.ConfigureSqlServer(builder, "Server=(localdb)\\MSSQLLocalDB;Database=Medora;Trusted_Connection=True;TrustServerCertificate=True");
        return new MedoraDbContext(builder.Options);
    }
}
