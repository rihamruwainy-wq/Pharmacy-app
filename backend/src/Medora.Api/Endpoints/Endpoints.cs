using Microsoft.AspNetCore.Mvc;
using Medora.Api.Infrastructure;
using Medora.Application.Contracts;
using Medora.Application.Features.Admin;
using Medora.Application.Features.Ai;
using Medora.Application.Features.Care;
using Medora.Application.Features.Catalog;
using Medora.Application.Features.Favorites;
using Medora.Application.Features.Identity;
using Medora.Application.Features.Orders;
using Medora.Application.Features.Pharmacist;

namespace Medora.Api.Endpoints;

internal static class Policies
{
    public const string Staff = "staff";
    public const string Admin = "admin";
}

internal static class EndpointMappings
{
    public static RouteGroupBuilder MapAuth(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth").RequireRateLimiting("auth");
        auth.MapPost("/lookup", (LookupRequest body, AuthService s, CancellationToken ct) => s.LookupAsync(body, ct));
        auth.MapPost("/login", async (LoginRequest body, AuthService s, CancellationToken ct) => (await s.LoginAsync(body, ct)).ToHttp());
        auth.MapPost("/register", async (RegisterRequest body, AuthService s, CancellationToken ct) => (await s.RegisterAsync(body, ct)).ToHttp());
        auth.MapPost("/refresh", async (RefreshRequest body, AuthService s, CancellationToken ct) => (await s.RefreshAsync(body, ct)).ToHttp());
        auth.MapPost("/logout", async (RefreshRequest body, AuthService s, CancellationToken ct) =>
        {
            await s.LogoutAsync(body, ct);
            return TypedResults.NoContent();
        });
        return api;
    }

    /// <summary>Public catalogue: output-cached on the server, Cache-Control for clients/CDNs, ETags from middleware.</summary>
    public static RouteGroupBuilder MapCatalog(this RouteGroupBuilder api)
    {
        var catalog = api.MapGroup("/catalog").WithTags("Catalog").AddEndpointFilter(async (ctx, next) =>
        {
            // Clients always revalidate (cheap: ETag -> 304), so an admin price change is visible on the next
            // request; shared caches/CDNs may serve it for 60 s. App-level caching lives in the client's query cache.
            ctx.HttpContext.Response.Headers.CacheControl = "public, max-age=0, s-maxage=60, must-revalidate";
            return await next(ctx);
        });
        catalog.MapGet("/categories", () => TypedResults.Ok(CatalogQueries.Categories)).CacheOutput("catalog");
        catalog.MapGet("/branches", (CatalogQueries q, CancellationToken ct) => q.BranchesAsync(ct)).CacheOutput("catalog");
        catalog.MapGet("/products", (string? q, string? category, bool? rx, string? sort, int? page, int? pageSize, long? maxPrice, string? branchId, bool? inStock, CatalogQueries queries, CancellationToken ct) =>
            queries.SearchAsync(new ProductSearch(q, category, rx, sort, page ?? 1, pageSize ?? 20, maxPrice, branchId, inStock ?? false), ct)).CacheOutput("catalog");
        catalog.MapGet("/suggest", (string? q, CatalogQueries queries, CancellationToken ct) => queries.SuggestAsync(q, ct)).CacheOutput("catalog");
        catalog.MapGet("/products/{id}", async Task<IResult> (string id, CatalogQueries q, CancellationToken ct) =>
            await q.GetAsync(id, ct) is { } p ? TypedResults.Ok(p) : ApiResults.Problem(Application.Common.Errors.NotFound("Product"))).CacheOutput("catalog");
        catalog.MapPost("/products/batch", (IdsRequest body, CatalogQueries q, CancellationToken ct) => q.BatchAsync(body.Ids ?? [], ct));
        // Live stock: not output-cached (changes with every order); clients keep it fresh for ~15 s.
        api.MapGet("/catalog/branches/{branchId}/stock", (string branchId, string ids, CatalogQueries q, CancellationToken ct) =>
            q.StockAsync(branchId, ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), ct)).WithTags("Catalog");
        return api;
    }

    public static RouteGroupBuilder MapCustomer(this RouteGroupBuilder api)
    {
        var me = api.MapGroup("/me").WithTags("Me").RequireAuthorization();
        me.MapGet("/", async (AuthService s, CancellationToken ct) => (await s.MeAsync(ct)).ToHttp());
        me.MapPut("/", async (UpdateProfileRequest body, AuthService s, CancellationToken ct) => (await s.UpdateProfileAsync(body, ct)).ToHttp());
        me.MapGet("/favorites", (FavoriteService s, CancellationToken ct) => s.ListAsync(ct));
        me.MapPut("/favorites/{productId}", async (string productId, FavoriteService s, CancellationToken ct) => (await s.AddAsync(productId, ct)).ToHttp(_ => TypedResults.NoContent()));
        me.MapDelete("/favorites/{productId}", async (string productId, FavoriteService s, CancellationToken ct) =>
        {
            await s.RemoveAsync(productId, ct);
            return TypedResults.NoContent();
        });
        me.MapGet("/orders", (OrderService s, CancellationToken ct) => s.MineAsync(ct));
        me.MapGet("/prescriptions", (OrderService s, CancellationToken ct) => s.MyPrescriptionsAsync(ct));
        me.MapPost("/prescriptions", async (NewPrescriptionRequest body, OrderService s, CancellationToken ct) =>
            (await s.SubmitPrescriptionAsync(body, ct)).ToHttp(rx => TypedResults.Created($"/api/v1/me/prescriptions/{rx.Id}", rx))).RequireRateLimiting("writes");
        me.MapGet("/appointments", (AppointmentService s, CancellationToken ct) => s.MineAsync(ct));

        var orders = api.MapGroup("/orders").WithTags("Orders").RequireAuthorization().RequireRateLimiting("writes");
        orders.MapPost("/quote", async (QuoteRequest body, OrderService s, CancellationToken ct) => (await s.QuoteAsync(body, ct)).ToHttp());
        orders.MapPost("/", async (NewOrderRequest body, [FromHeader(Name = "Idempotency-Key")] string? key, OrderService s, CancellationToken ct) =>
            (await s.CreateAsync(body, key, ct)).ToHttp(o => TypedResults.Created($"/api/v1/orders/{o.Id}", o)));
        orders.MapGet("/{id}", async (string id, OrderService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToHttp());
        orders.MapPost("/{id}/pay", async (string id, PayRequest body, OrderService s, CancellationToken ct) => (await s.PayAsync(id, body, ct)).ToHttp());
        orders.MapPost("/{id}/cancel", async (string id, OrderService s, CancellationToken ct) => (await s.CancelAsync(id, ct)).ToHttp());

        var care = api.MapGroup("/care").WithTags("Care services");
        care.MapGet("/services", (AppointmentService s, CancellationToken ct) => s.ServicesAsync(ct)).CacheOutput("catalog");
        care.MapGet("/services/{id}/slots", async (string id, DateOnly? from, int? days, AppointmentService s, TimeProvider clock, CancellationToken ct) =>
            (await s.SlotsAsync(id, from ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), days ?? 7, ct)).ToHttp());
        care.MapPost("/appointments", async (NewAppointmentRequest body, AppointmentService s, CancellationToken ct) =>
            (await s.BookAsync(body, ct)).ToHttp(a => TypedResults.Created($"/api/v1/care/appointments/{a.Id}", a))).RequireAuthorization().RequireRateLimiting("writes");
        care.MapPost("/appointments/{id}/cancel", async (string id, AppointmentService s, CancellationToken ct) => (await s.CancelAsync(id, ct)).ToHttp()).RequireAuthorization();
        return api;
    }

    public static RouteGroupBuilder MapStaff(this RouteGroupBuilder api)
    {
        var rx = api.MapGroup("/pharmacist").WithTags("Pharmacist").RequireAuthorization(Policies.Staff);
        rx.MapGet("/queue", (PharmacistService s, CancellationToken ct) => s.QueueAsync(ct));
        rx.MapGet("/orders/active", (PharmacistService s, CancellationToken ct) => s.ActiveAsync(ct));
        rx.MapGet("/orders/{id}", async (string id, PharmacistService s, CancellationToken ct) => (await s.DetailAsync(id, ct)).ToHttp());
        rx.MapPost("/orders/{id}/verify", async (string id, VerifyRequest body, PharmacistService s, CancellationToken ct) => (await s.VerifyAsync(id, body, ct)).ToHttp());
        rx.MapPost("/orders/{id}/advance", async (string id, AdvanceRequest body, PharmacistService s, CancellationToken ct) => (await s.AdvanceAsync(id, body, ct)).ToHttp());
        rx.MapPost("/copilot", async (CopilotRequest body, PharmacistCopilotService s, CancellationToken ct) => (await s.AskAsync(body, ct)).ToHttp()).RequireRateLimiting("ai");

        var admin = api.MapGroup("/admin").WithTags("Admin").RequireAuthorization(Policies.Admin);
        admin.MapGet("/dashboard", (string? branchId, AdminService s, CancellationToken ct) => s.DashboardAsync(branchId, ct));
        admin.MapGet("/inventory", (string? branchId, bool? lowOnly, string? q, int? page, int? pageSize, AdminService s, CancellationToken ct) =>
            s.InventoryAsync(branchId, lowOnly ?? false, q, page ?? 1, pageSize ?? 50, ct));
        admin.MapPost("/inventory/{branchId}/{productId}/restock", async (string branchId, string productId, RestockRequest body, AdminService s, CancellationToken ct) =>
            (await s.RestockAsync(branchId, productId, body, ct)).ToHttp());
        admin.MapPut("/products/{id}/price", async (string id, RepriceRequest body, AdminService s, Microsoft.AspNetCore.OutputCaching.IOutputCacheStore outputCache, CancellationToken ct) =>
        {
            var result = await s.RepriceAsync(id, body, ct);
            // The data cache was invalidated by the use case; also drop server-rendered catalogue responses.
            if (result.IsSuccess) await outputCache.EvictByTagAsync("catalog", ct);
            return result.ToHttp();
        });
        admin.MapGet("/audit", (int? take, AdminService s, CancellationToken ct) => s.AuditAsync(take ?? 50, ct));
        admin.MapPost("/assistant", async (AssistantRequest body, AdminAssistantService s, CancellationToken ct) => (await s.AskAsync(body, ct)).ToHttp()).RequireRateLimiting("ai");
        return api;
    }
}
