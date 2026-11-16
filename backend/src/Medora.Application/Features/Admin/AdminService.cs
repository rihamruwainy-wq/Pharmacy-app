using Medora.Application.Abstractions;
using Medora.Application.Common;
using Medora.Application.Contracts;
using Medora.Application.Features.Catalog;
using Medora.Domain.Common;
using Medora.Domain.Identity;
using Medora.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Medora.Application.Features.Admin;

/// <summary>Back-office commands and dashboard (admin role only; enforced by the API's authorization policy).</summary>
public sealed class AdminService(IAppDbContext db, AdminInsights insights, ICatalogCache cache, ICurrentUser user, TimeProvider clock)
{
    public async Task<DashboardDto> DashboardAsync(string? branchId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var startOfDay = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var today = await insights.SalesSummaryAsync(startOfDay, now, branchId, ct);
        var week = await insights.SalesSummaryAsync(startOfDay.AddDays(-6), now, branchId, ct);
        var byStatus = await insights.OrderStatusBreakdownAsync(startOfDay.AddDays(-6), now, branchId, ct);
        var low = await insights.LowStockAsync(branchId, 500, ct);
        var expiring = await insights.ExpiringStockAsync(60, branchId, 500, ct);
        var pending = await db.Orders.AsNoTracking().CountAsync(o => o.Status == OrderStatus.AwaitingVerification && (branchId == null || o.BranchId == branchId), ct);
        return new DashboardDto(today.RevenueMinor, week.RevenueMinor, today.PaidOrders, pending, low.Count, expiring.Count, byStatus);
    }

    public async Task<Paged<InventoryRowDto>> InventoryAsync(string? branchId, bool lowOnly, string? q, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = from i in db.Inventory.AsNoTracking()
                    join p in db.Products.AsNoTracking() on i.ProductId equals p.Id
                    select new { i, p.Name };
        if (branchId is not null) query = query.Where(x => x.i.BranchId == branchId);
        if (lowOnly) query = query.Where(x => x.i.OnHand - x.i.Reserved <= x.i.ReorderLevel);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.Like(x.Name, Guard.LikeContains(q.Trim())));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.i.OnHand - x.i.Reserved - x.i.ReorderLevel).ThenBy(x => x.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new InventoryRowDto(x.i.BranchId, x.i.ProductId, x.Name, x.i.OnHand, x.i.Reserved, x.i.OnHand - x.i.Reserved, x.i.ReorderLevel, x.i.NearestExpiry, x.i.OnHand - x.i.Reserved <= x.i.ReorderLevel))
            .ToListAsync(ct);
        return new Paged<InventoryRowDto>(rows, page, pageSize, total);
    }

    public async Task<Result<InventoryRowDto>> RestockAsync(string branchId, string productId, RestockRequest request, CancellationToken ct)
    {
        if (request.Quantity is < 1 or > 10_000) return Errors.Validation("Quantity must be between 1 and 10,000.");
        var item = await db.Inventory.FirstOrDefaultAsync(i => i.BranchId == branchId && i.ProductId == productId, ct);
        if (item is null) return Errors.NotFound("Inventory item");
        var now = clock.GetUtcNow();
        if (request.BatchExpiry is { } exp && exp <= DateOnly.FromDateTime(now.UtcDateTime)) return Errors.Validation("This batch is already expired.");
        item.Restock(request.Quantity, request.BatchExpiry);
        db.AuditEntries.Add(new AuditEntry(user.RequireUserId(), "inventory.restocked", $"{branchId}/{productId}", $"+{request.Quantity} exp={request.BatchExpiry}", now));
        await db.SaveChangesAsync(ct); // rowversion: a concurrent order reservation makes this retry, never lose units
        var name = await db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.Name).FirstAsync(ct);
        return new InventoryRowDto(item.BranchId, item.ProductId, name, item.OnHand, item.Reserved, item.Available, item.ReorderLevel, item.NearestExpiry, item.IsLow);
    }

    public async Task<Result<ProductDto>> RepriceAsync(string productId, RepriceRequest request, CancellationToken ct)
    {
        if (request.PriceMinor is < 1 or > 1_000_000) return Errors.Validation("Price must be between $0.01 and $10,000.");
        var product = Guard.IsId(productId, "prd") ? await db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct) : null;
        if (product is null) return Errors.NotFound("Product");
        var old = product.PriceMinor;
        product.Reprice(request.PriceMinor);
        db.AuditEntries.Add(new AuditEntry(user.RequireUserId(), "product.repriced", productId, $"{old} -> {request.PriceMinor}", clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        // Precise invalidation: every cached search/detail entry tagged "products" is dropped (L1 + L2).
        await cache.InvalidateTagAsync(CatalogQueries.ProductsTag, ct);
        return product.ToDto();
    }

    public async Task<IReadOnlyList<AuditRowDto>> AuditAsync(int take, CancellationToken ct) =>
        await db.AuditEntries.AsNoTracking().OrderByDescending(a => a.At).Take(Math.Clamp(take, 1, 200))
            .Select(a => new AuditRowDto(a.At, a.ActorId, a.Action, a.SubjectId, a.Detail)).ToListAsync(ct);
}

public sealed record AuditRowDto(DateTimeOffset At, string ActorId, string Action, string SubjectId, string? Detail);

/// <summary>
/// READ-ONLY analytics used by both the dashboard and the admin AI assistant's tools. Every method
/// is bounded (date range, row limits) so a model-chosen argument can never trigger an unbounded scan.
/// </summary>
public sealed class AdminInsights(IAppDbContext db, TimeProvider clock)
{
    public const int MaxRangeDays = 366;

    public sealed record SalesSummary(DateTimeOffset From, DateTimeOffset To, int PaidOrders, long RevenueMinor, long RefundedMinor, long AverageOrderMinor, string Currency);
    public sealed record ProductSales(string ProductId, string Name, int Units, long RevenueMinor);
    public sealed record StockRow(string BranchId, string ProductId, string Name, int Available, int ReorderLevel, DateOnly? NearestExpiry);

    private static readonly OrderStatus[] Revenue = [OrderStatus.AwaitingVerification, OrderStatus.Preparing, OrderStatus.ReadyForPickup, OrderStatus.OutForDelivery, OrderStatus.Completed];

    public async Task<SalesSummary> SalesSummaryAsync(DateTimeOffset from, DateTimeOffset to, string? branchId, CancellationToken ct)
    {
        (from, to) = Clamp(from, to);
        var paid = db.Orders.AsNoTracking().Where(o => o.PaidAt != null && o.PaidAt >= from && o.PaidAt < to && (branchId == null || o.BranchId == branchId));
        var kept = paid.Where(o => Revenue.Contains(o.Status));
        var count = await kept.CountAsync(ct);
        var revenue = await kept.SumAsync(o => (long?)o.TotalMinor, ct) ?? 0;
        var refunded = await paid.SumAsync(o => o.RefundMinor, ct) ?? 0;
        return new SalesSummary(from, to, count, revenue, refunded, count == 0 ? 0 : revenue / count, "USD");
    }

    public async Task<IReadOnlyDictionary<string, int>> OrderStatusBreakdownAsync(DateTimeOffset from, DateTimeOffset to, string? branchId, CancellationToken ct)
    {
        (from, to) = Clamp(from, to);
        var rows = await db.Orders.AsNoTracking().Where(o => o.CreatedAt >= from && o.CreatedAt < to && (branchId == null || o.BranchId == branchId))
            .GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Key.Wire(), r => r.Count, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<ProductSales>> TopProductsAsync(DateTimeOffset from, DateTimeOffset to, string? branchId, int limit, CancellationToken ct)
    {
        (from, to) = Clamp(from, to);
        limit = Math.Clamp(limit, 1, 50);
        // Lines are an owned JSON-free child table; aggregate in SQL over the owned collection.
        var rows = await db.Orders.AsNoTracking()
            .Where(o => o.PaidAt != null && o.PaidAt >= from && o.PaidAt < to && Revenue.Contains(o.Status) && (branchId == null || o.BranchId == branchId))
            .SelectMany(o => o.Lines)
            .GroupBy(l => new { l.ProductId, l.Name })
            .Select(g => new ProductSales(g.Key.ProductId, g.Key.Name, g.Sum(l => l.Quantity), g.Sum(l => l.UnitPriceMinor * l.Quantity)))
            .OrderByDescending(r => r.RevenueMinor).Take(limit).ToListAsync(ct);
        return rows;
    }

    public async Task<IReadOnlyList<StockRow>> LowStockAsync(string? branchId, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 500);
        return await (from i in db.Inventory.AsNoTracking()
                      join p in db.Products.AsNoTracking() on i.ProductId equals p.Id
                      where i.OnHand - i.Reserved <= i.ReorderLevel && (branchId == null || i.BranchId == branchId)
                      orderby i.OnHand - i.Reserved - i.ReorderLevel
                      select new StockRow(i.BranchId, i.ProductId, p.Name, i.OnHand - i.Reserved, i.ReorderLevel, i.NearestExpiry)).Take(limit).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StockRow>> ExpiringStockAsync(int withinDays, string? branchId, int limit, CancellationToken ct)
    {
        withinDays = Math.Clamp(withinDays, 1, 365);
        limit = Math.Clamp(limit, 1, 500);
        var cutoff = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(withinDays);
        return await (from i in db.Inventory.AsNoTracking()
                      join p in db.Products.AsNoTracking() on i.ProductId equals p.Id
                      where i.NearestExpiry != null && i.NearestExpiry <= cutoff && i.OnHand > 0 && (branchId == null || i.BranchId == branchId)
                      orderby i.NearestExpiry
                      select new StockRow(i.BranchId, i.ProductId, p.Name, i.OnHand - i.Reserved, i.ReorderLevel, i.NearestExpiry)).Take(limit).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<object>> BranchesAsync(CancellationToken ct) =>
        await db.Branches.AsNoTracking().OrderBy(b => b.Name).Select(b => (object)new { b.Id, b.Name, b.City }).ToListAsync(ct);

    private (DateTimeOffset From, DateTimeOffset To) Clamp(DateTimeOffset from, DateTimeOffset to)
    {
        var now = clock.GetUtcNow();
        if (to > now) to = now;
        if (from > to) from = to;
        if (to - from > TimeSpan.FromDays(MaxRangeDays)) from = to - TimeSpan.FromDays(MaxRangeDays);
        return (from, to);
    }
}
