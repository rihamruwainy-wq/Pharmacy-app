using System.Linq.Expressions;
using Medora.Application.Abstractions;
using Medora.Application.Common;
using Medora.Application.Contracts;
using Medora.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Medora.Application.Features.Catalog;

/// <summary>Search request (all filters optional). Prices are integer cents.</summary>
public sealed record ProductSearch(string? Q, string? Category, bool? Rx, string? Sort, int Page, int PageSize, long? MaxPriceMinor, string? BranchId, bool InStockOnly);

/// <summary>
/// Catalogue reads. Public, identical for every user => cached (L1 in-process + optional L2) with
/// tag invalidation when an admin reprices. Stock is NOT cached here: it changes per order and is
/// served by <see cref="StockAsync"/> with a short client-side staleness instead.
/// </summary>
public sealed class CatalogQueries(IAppDbContext db, ICatalogCache cache)
{
    public const string ProductsTag = "products";
    public const int MaxPageSize = 48;

    public static readonly IReadOnlyList<CategoryDto> Categories =
    [
        new("pain-relief", "Pain & fever", "thermometer"),
        new("cold-flu", "Cold & flu", "snowflake"),
        new("allergy", "Allergy", "flower"),
        new("digestive", "Digestive", "stomach"),
        new("vitamins", "Vitamins", "leaf"),
        new("heart", "Heart & BP", "heart"),
        new("diabetes", "Diabetes", "droplet"),
        new("antibiotics", "Antibiotics", "pill"),
        new("mental-health", "Mental health", "brain"),
        new("respiratory", "Respiratory", "lungs"),
        new("skin", "Skin care", "sparkles"),
        new("baby", "Baby & mother", "baby"),
    ];

    private static readonly HashSet<string> CategoryIds = Categories.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Understands brands, generics, symptoms and Arabic (see <see cref="SearchLanguage"/>); filters by category,
    /// prescription status, maximum price and live stock at a branch; and, when nothing matches, retries once
    /// with a typo-corrected query and reports it as <c>didYouMean</c>.
    /// </summary>
    public ValueTask<SearchResultDto> SearchAsync(ProductSearch s, CancellationToken ct)
    {
        var q = string.IsNullOrWhiteSpace(s.Q) ? null : s.Q.Trim()[..Math.Min(s.Q.Trim().Length, 80)];
        var category = s.Category is not null && CategoryIds.Contains(s.Category) ? s.Category : null;
        var sort = s.Sort is "price_asc" or "price_desc" or "rating" ? s.Sort : "relevance";
        var page = Math.Max(1, s.Page);
        var pageSize = Math.Clamp(s.PageSize, 1, MaxPageSize);
        var maxPrice = s.MaxPriceMinor is > 0 ? s.MaxPriceMinor : null;
        var branch = s.InStockOnly && Guard.IsId(s.BranchId, "brn") ? s.BranchId : null;
        var key = $"products:search:{(q is null ? null : SearchLanguage.Normalize(q))}|{category}|{s.Rx}|{sort}|{page}|{pageSize}|{maxPrice}|{branch}";

        return cache.GetOrCreateAsync(key, async token =>
        {
            var plan = q is null ? new SearchPlan([], []) : SearchLanguage.Plan(q);
            var (total, items) = await RunAsync(plan, category, s.Rx, sort, page, pageSize, maxPrice, branch, token);
            string? corrected = null;
            if (total == 0 && q is not null && (corrected = SearchLanguage.Correct(q, await VocabularyAsync(token))) is not null)
            {
                plan = SearchLanguage.Plan(corrected);
                (total, items) = await RunAsync(plan, category, s.Rx, sort, page, pageSize, maxPrice, branch, token);
                if (total == 0) corrected = null;
            }
            return new SearchResultDto(items, page, pageSize, total, corrected, plan.Categories);
        }, [ProductsTag], branch is null ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(15), ct); // stock filter changes with every order
    }

    /// <summary>Type-ahead: up to 6 products and the categories the words map to. Same language understanding as search.</summary>
    public ValueTask<SuggestResponse> SuggestAsync(string? q, CancellationToken ct)
    {
        var text = q?.Trim() ?? string.Empty;
        if (text.Length < 2) return ValueTask.FromResult(new SuggestResponse([], [], null));
        text = text[..Math.Min(text.Length, 80)];
        return cache.GetOrCreateAsync($"products:suggest:{SearchLanguage.Normalize(text)}", async token =>
        {
            var plan = SearchLanguage.Plan(text);
            var (_, items) = await RunAsync(plan, null, null, "relevance", 1, 6, null, null, token);
            string? corrected = null;
            if (items.Count == 0 && (corrected = SearchLanguage.Correct(text, await VocabularyAsync(token))) is not null)
            {
                plan = SearchLanguage.Plan(corrected);
                (_, items) = await RunAsync(plan, null, null, "relevance", 1, 6, null, null, token);
                if (items.Count == 0) corrected = null;
            }
            return new SuggestResponse(items.Select(p => new SuggestionDto(p.Id, p.Name, p.Brand, p.RequiresPrescription)).ToList(), plan.Categories, corrected);
        }, [ProductsTag], TimeSpan.FromMinutes(5), ct);
    }

    private async Task<(int Total, List<ProductSummaryDto> Items)> RunAsync(SearchPlan plan, string? category, bool? rx, string sort, int page, int pageSize, long? maxPrice, string? branchId, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking().Where(p => p.IsActive);
        if (category is not null) query = query.Where(p => p.Category == category);
        if (plan.Categories.Count > 0) query = query.Where(p => plan.Categories.Contains(p.Category));
        foreach (var group in plan.TermGroups) query = query.Where(MatchesAny(group)); // AND across words, OR across synonyms
        if (rx is not null) query = query.Where(p => p.RequiresPrescription == rx);
        if (maxPrice is not null) query = query.Where(p => p.PriceMinor <= maxPrice);
        if (branchId is not null) query = query.Where(p => db.Inventory.Any(i => i.BranchId == branchId && i.ProductId == p.Id && i.OnHand - i.Reserved > 0));

        var lead = plan.TermGroups.Count > 0 && plan.TermGroups[0].Count > 0 ? plan.TermGroups[0][0] : null;
        query = sort switch
        {
            "price_asc" => query.OrderBy(p => p.PriceMinor).ThenBy(p => p.Id),
            "price_desc" => query.OrderByDescending(p => p.PriceMinor).ThenBy(p => p.Id),
            "rating" => query.OrderByDescending(p => p.Rating).ThenBy(p => p.Id),
            // Relevance: products whose NAME starts with what was typed first, then popularity.
            _ => lead is null
                ? query.OrderByDescending(p => p.ReviewCount).ThenBy(p => p.Id)
                : query.OrderByDescending(p => p.Name.StartsWith(lead) || p.Brand.StartsWith(lead)).ThenByDescending(p => p.ReviewCount).ThenBy(p => p.Id),
        };
        var total = await query.CountAsync(ct);
        var items = total == 0 ? [] : await query.Skip((page - 1) * pageSize).Take(pageSize).Select(Mapping.ProductSummary).ToListAsync(ct);
        return (total, items);
    }

    /// <summary>p => Name LIKE t1 OR GenericName LIKE t1 OR Brand LIKE t1 OR ... for every synonym t (parameterised, index-friendly per column).</summary>
    private static Expression<Func<Product, bool>> MatchesAny(IReadOnlyList<string> terms)
    {
        var p = Expression.Parameter(typeof(Product), "p");
        var like = typeof(DbFunctionsExtensions).GetMethod(nameof(DbFunctionsExtensions.Like), [typeof(DbFunctions), typeof(string), typeof(string)])!;
        Expression? body = null;
        foreach (var term in terms)
        {
            var pattern = Expression.Property(Expression.Constant(new LikePattern(Guard.LikeContains(term))), nameof(LikePattern.Value)); // captured => SQL parameter
            foreach (var field in (string[])[nameof(Product.Name), nameof(Product.GenericName), nameof(Product.Brand)])
            {
                Expression call = Expression.Call(like, Expression.Constant(EF.Functions), Expression.Property(p, field), pattern);
                body = body is null ? call : Expression.OrElse(body, call);
            }
        }
        return Expression.Lambda<Func<Product, bool>>(body ?? Expression.Constant(true), p);
    }

    private sealed record LikePattern(string Value);

    /// <summary>Every word in product names, generics and brands (the typo corrector's dictionary). Cached with the catalogue.</summary>
    private ValueTask<string[]> VocabularyAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync("products:vocabulary", async token =>
        {
            var rows = await db.Products.AsNoTracking().Where(p => p.IsActive).Select(p => new { p.Name, p.GenericName, p.Brand }).ToListAsync(token);
            return rows.SelectMany(r => SearchLanguage.Words($"{r.Name} {r.GenericName} {r.Brand}")).Where(w => w.Length >= 4 && !w.Any(char.IsDigit)).Distinct(StringComparer.Ordinal).ToArray();
        }, [ProductsTag], TimeSpan.FromMinutes(30), ct);

    public async Task<ProductDto?> GetAsync(string id, CancellationToken ct)
    {
        if (!Guard.IsId(id, "prd")) return null;
        return await cache.GetOrCreateAsync($"products:one:{id}", async token =>
        {
            var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.IsActive, token);
            return product?.ToDto();
        }, [ProductsTag, $"product:{id}"], TimeSpan.FromMinutes(10), ct);
    }

    /// <summary>Batch lookup (wishlist, cart rehydration): one query, no N+1.</summary>
    public async Task<IReadOnlyList<ProductSummaryDto>> BatchAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        var valid = ids.Where(i => Guard.IsId(i, "prd")).Distinct(StringComparer.Ordinal).Take(100).ToList();
        if (valid.Count == 0) return [];
        return await db.Products.AsNoTracking().Where(p => valid.Contains(p.Id)).Select(Mapping.ProductSummary).ToListAsync(ct);
    }

    public ValueTask<IReadOnlyList<BranchDto>> BranchesAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync<IReadOnlyList<BranchDto>>("branches:all", async token =>
            (await db.Branches.AsNoTracking().OrderBy(b => b.City).ThenBy(b => b.Name).ToListAsync(token)).Select(b => b.ToDto()).ToList(),
            ["branches"], TimeSpan.FromHours(1), ct);

    /// <summary>Live availability for up to 50 products at a branch (bucketed: customers see in/low/out, not exact counts above 10).</summary>
    public async Task<IReadOnlyList<StockDto>> StockAsync(string branchId, IReadOnlyList<string> productIds, CancellationToken ct)
    {
        if (!Guard.IsId(branchId, "brn")) return [];
        var ids = productIds.Where(i => Guard.IsId(i, "prd")).Distinct(StringComparer.Ordinal).Take(50).ToList();
        var rows = await db.Inventory.AsNoTracking()
            .Where(i => i.BranchId == branchId && ids.Contains(i.ProductId))
            .Select(i => new { i.ProductId, Available = i.OnHand - i.Reserved, i.ReorderLevel })
            .ToListAsync(ct);
        var byId = rows.ToDictionary(r => r.ProductId, StringComparer.Ordinal);
        return ids.Select(id => byId.TryGetValue(id, out var r)
            ? new StockDto(id, r.Available <= 0 ? "out" : r.Available <= r.ReorderLevel ? "low" : "in_stock", Math.Clamp(r.Available, 0, 10))
            : new StockDto(id, "out", 0)).ToList();
    }
}
