using Medora.Application.Abstractions;
using Medora.Application.Common;
using Medora.Domain.Common;
using Medora.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Medora.Application.Features.Favorites;

/// <summary>Saved products (Airbnb wishlist hearts). PUT/DELETE are idempotent: safe to retry, safe for optimistic UIs.</summary>
public sealed class FavoriteService(IAppDbContext db, ICurrentUser user, TimeProvider clock)
{
    public async Task<string[]> ListAsync(CancellationToken ct)
    {
        var userId = user.RequireUserId();
        return await db.Favorites.AsNoTracking().Where(f => f.UserId == userId).OrderByDescending(f => f.CreatedAt).Select(f => f.ProductId).ToArrayAsync(ct);
    }

    public async Task<Result<bool>> AddAsync(string productId, CancellationToken ct)
    {
        var userId = user.RequireUserId();
        if (!Guard.IsId(productId, "prd") || !await db.Products.AnyAsync(p => p.Id == productId, ct)) return Errors.NotFound("Product");
        if (await db.Favorites.AnyAsync(f => f.UserId == userId && f.ProductId == productId, ct)) return true;
        db.Favorites.Add(new Favorite(userId, productId, clock.GetUtcNow()));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Concurrent duplicate PUT hit the composite PK: the end state is what was asked for.
        }
        return true;
    }

    public async Task RemoveAsync(string productId, CancellationToken ct)
    {
        var userId = user.RequireUserId();
        await db.Favorites.Where(f => f.UserId == userId && f.ProductId == productId).ExecuteDeleteAsync(ct);
    }
}
