using Medora.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Medora.Infrastructure.Persistence;

/// <summary>One transaction per unit of work, replayed as a whole by SQL Server's retry strategy on transient faults.</summary>
internal sealed class EfUnitOfWork(MedoraDbContext db) : IUnitOfWork
{
    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null) return await work(cancellationToken); // already inside one: join it
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async token =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(token);
            var result = await work(token);
            await tx.CommitAsync(token);
            return result;
        }, cancellationToken);
    }
}

/// <summary>
/// Stock ledger with conditional, set-based UPDATEs. Each statement is a single atomic row update in
/// SQL Server, so "check available, then reserve" can't interleave between two customers; the CHECK
/// constraint on the table is the final guard. On a partial failure the already-reserved lines are
/// compensated in the same transaction (all-or-nothing without relying on the caller to roll back).
/// </summary>
internal sealed class SqlStockLedger(MedoraDbContext db) : IStockLedger
{
    public async Task<string?> TryReserveAsync(string branchId, IReadOnlyList<StockLine> lines, CancellationToken cancellationToken)
    {
        var reserved = new List<StockLine>(lines.Count);
        foreach (var line in lines.OrderBy(l => l.ProductId, StringComparer.Ordinal)) // fixed order: no deadlocks between concurrent multi-line orders
        {
            var qty = line.Quantity;
            var updated = await db.Inventory
                .Where(i => i.BranchId == branchId && i.ProductId == line.ProductId && i.OnHand - i.Reserved >= qty)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Reserved, i => i.Reserved + qty), cancellationToken);
            if (updated == 0)
            {
                await ReleaseAsync(branchId, reserved, cancellationToken);
                return line.ProductId;
            }
            reserved.Add(line);
        }
        return null;
    }

    public async Task ReleaseAsync(string branchId, IReadOnlyList<StockLine> lines, CancellationToken cancellationToken)
    {
        foreach (var line in lines.OrderBy(l => l.ProductId, StringComparer.Ordinal))
        {
            var qty = line.Quantity;
            await db.Inventory
                .Where(i => i.BranchId == branchId && i.ProductId == line.ProductId && i.Reserved >= qty)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Reserved, i => i.Reserved - qty), cancellationToken);
        }
    }

    public async Task ConsumeAsync(string branchId, IReadOnlyList<StockLine> lines, CancellationToken cancellationToken)
    {
        foreach (var line in lines.OrderBy(l => l.ProductId, StringComparer.Ordinal))
        {
            var qty = line.Quantity;
            await db.Inventory
                .Where(i => i.BranchId == branchId && i.ProductId == line.ProductId && i.Reserved >= qty && i.OnHand >= qty)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Reserved, i => i.Reserved - qty).SetProperty(i => i.OnHand, i => i.OnHand - qty), cancellationToken);
        }
    }
}

/// <summary>UPDLOCK + HOLDLOCK on the service row: concurrent bookings for the same service queue up for the capacity check.</summary>
internal sealed class SqlSlotLock(MedoraDbContext db) : ISlotLock
{
    public async Task LockServiceAsync(string serviceId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("LockServiceAsync must run inside a transaction.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM [CareServices] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id] = {serviceId}", cancellationToken);
    }
}
