using System.Text.Json;
using Medora.Application.Abstractions;
using Medora.Domain.Care;
using Medora.Domain.Catalog;
using Medora.Domain.Identity;
using Medora.Domain.Knowledge;
using Medora.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Medora.Infrastructure.Persistence;

/// <summary>
/// Applies migrations and seeds reference data (catalogue, knowledge base) plus DEMO data
/// (accounts, stock, 30 days of order history, one prescription awaiting verification).
/// Idempotent: each block only runs when its table is empty. Demo accounts share the test
/// password documented in the README; never enable SeedDemoData in production.
/// </summary>
internal sealed partial class DatabaseSeeder(MedoraDbContext db, IPasswordHasher hasher, TimeProvider clock, ILogger<DatabaseSeeder> logger)
{
    internal const string DemoPassword = "Password123!";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task MigrateAndSeedAsync(bool seedDemoData, CancellationToken ct)
    {
        await db.Database.MigrateAsync(ct);
        var now = clock.GetUtcNow();

        if (!await db.Products.AnyAsync(ct))
        {
            var catalog = Load<CatalogSeed>("catalog.json");
            db.Branches.AddRange(catalog.Branches.Select(b => new Branch(b.Id, b.Name, b.City, b.Address, b.Latitude, b.Longitude, b.OpeningHours, b.DeliveryFeeMinor, b.Open24h)));
            db.Products.AddRange(catalog.Products.Select(p => new Product(p.Id, p.Name, p.GenericName, p.Brand, p.Category, p.Form, p.Strength, p.PriceMinor, p.Rx, p.MaxQty, p.Image, p.Description, p.Ingredients, p.Rating, p.Reviews)));
            db.CareServices.AddRange(catalog.CareServices.Select(s => new CareService(s.Id, s.BranchId, s.Name, s.Category, s.Description, s.Duration, s.Capacity, s.PriceMinor, s.MinimumAge, s.Image)));
            await db.SaveChangesAsync(ct);
            LogSeeded(logger, catalog.Products.Count, "products");
        }

        if (!await db.MonographSections.AnyAsync(ct))
        {
            var kb = Load<KnowledgeSeed>("knowledge.json");
            foreach (var m in kb.Monographs)
            {
                foreach (var (section, content) in m.Sections)
                {
                    var slug = new string(section.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
                    db.MonographSections.Add(new MonographSection($"kb_{m.Generic.Replace(' ', '-')}_{slug}", m.Generic, m.Title, section, content, kb.Source));
                }
            }
            var interactions = Load<List<InteractionSeed>>("interactions.json");
            db.DrugInteractions.AddRange(interactions.Select(i => new DrugInteraction(i.A, i.B, Enum.Parse<InteractionSeverity>(i.Severity), i.Effect, i.Management)));
            await db.SaveChangesAsync(ct);
            LogSeeded(logger, interactions.Count, "interaction pairs + monographs");
        }

        if (seedDemoData && !await db.Users.AnyAsync(ct))
        {
            await SeedDemoAsync(now, ct);
        }
    }

    private async Task SeedDemoAsync(DateTimeOffset now, CancellationToken ct)
    {
        var hash = hasher.Hash(DemoPassword);
        var demo = User.Seeded("usr_demo", "Dana", "Patient", "demo@medora.local", Roles.Customer, null, now.AddDays(-120));
        demo.SetAllergies("Penicillin (rash)");
        var users = new[]
        {
            demo,
            User.Seeded("usr_pharm", "Omar", "Haddad", "pharmacist@medora.local", Roles.Pharmacist, "brn_downtown", now.AddDays(-400)),
            User.Seeded("usr_admin", "Alex", "Admin", "admin@medora.local", Roles.Admin, null, now.AddDays(-400)),
            User.Seeded("usr_cust2", "Lina", "Morales", "lina@medora.local", Roles.Customer, null, now.AddDays(-90)),
            User.Seeded("usr_cust3", "Sam", "Okafor", "sam@medora.local", Roles.Customer, null, now.AddDays(-60)),
        };
        foreach (var u in users) u.SetPasswordHash(hash);
        db.Users.AddRange(users);

        // Deterministic pseudo-random stock so screenshots and tests are reproducible.
        var random = new Random(42);
        var products = await db.Products.AsNoTracking().ToListAsync(ct);
        var branches = await db.Branches.AsNoTracking().Select(b => new { b.Id, b.DeliveryFeeMinor }).ToListAsync(ct);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        foreach (var b in branches)
        {
            foreach (var p in products)
            {
                var onHand = random.Next(0, 10) < 2 ? random.Next(0, 6) : random.Next(15, 80);   // ~20% low/out
                if (b.Id == "brn_downtown" && p.Id is "prd_clarith500" or "prd_advil200") onHand = Math.Max(onHand, 12); // the seeded pending order reserves these
                var expiry = today.AddDays(random.Next(0, 10) < 2 ? random.Next(10, 60) : random.Next(120, 700));
                db.Inventory.Add(new InventoryItem(b.Id, p.Id, onHand, 8, expiry));
            }
        }
        await db.SaveChangesAsync(ct);

        // 30 days of completed orders (feeds the dashboard and the admin assistant).
        var otc = products.Where(p => !p.RequiresPrescription).ToList();
        var customers = new[] { "usr_cust2", "usr_cust3" }; // the demo patient keeps a clean, clinically meaningful history
        for (var i = 0; i < 90; i++)
        {
            var at = now.AddDays(-random.Next(1, 30)).AddHours(-random.Next(0, 10));
            var branch = branches[random.Next(branches.Count)];
            var picks = otc.OrderBy(_ => random.Next()).Take(random.Next(1, 4)).ToList();
            var lines = picks.Select(p => new OrderLine(p.Id, p.Name, p.GenericName, p.PriceMinor, 1, false)).ToList();
            var order = Order.PlaceHold(customers[random.Next(customers.Length)], branch.Id, Fulfilment.Pickup, null, $"seed-{i:D4}", lines, Pricing.Quote(lines, Fulfilment.Pickup, branch.DeliveryFeeMinor), null, at);
            order.MarkPaid($"pi_seed_{i:D4}", "visa", "4242", at.AddMinutes(2));
            order.TransitionTo(OrderStatus.ReadyForPickup, at.AddHours(1));
            if (random.Next(0, 10) < 9) order.TransitionTo(OrderStatus.Completed, at.AddHours(5));
            db.Orders.Add(order);
        }

        // Demo patient's medication history: warfarin + simvastatin dispensed 3 weeks ago.
        var byId = products.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var rxOld = new Prescription("usr_demo", "Dana Patient", "Dr. R. Chen", "TX-48211", "rx-images/demo-old.jpg", "Long-term anticoagulation; lipid management", today.AddDays(-25), now.AddDays(-22));
        db.Prescriptions.Add(rxOld);
        var history = Lines(byId, ("prd_warfarin5", 1), ("prd_simva20", 1));
        var past = Order.PlaceHold("usr_demo", "brn_downtown", Fulfilment.Pickup, null, "seed-demo-history", history, Pricing.Quote(history, Fulfilment.Pickup, 499), rxOld.Id, now.AddDays(-21));
        past.MarkPaid("pi_seed_demo_hist", "visa", "4242", now.AddDays(-21));
        past.Verify("usr_pharm", now.AddDays(-21));
        past.TransitionTo(OrderStatus.ReadyForPickup, now.AddDays(-21));
        past.TransitionTo(OrderStatus.Completed, now.AddDays(-20));
        db.Orders.Add(past);

        // ...and a NEW order awaiting verification: clarithromycin + ibuprofen -> several real interaction alerts.
        var rxNew = new Prescription("usr_demo", "Dana Patient", "Dr. M. Alvarez", "TX-90317", "rx-images/demo-new.jpg", "Chest infection", today.AddDays(-1), now.AddHours(-3));
        db.Prescriptions.Add(rxNew);
        var pendingLines = Lines(byId, ("prd_clarith500", 1), ("prd_advil200", 1));
        var pending = Order.PlaceHold("usr_demo", "brn_downtown", Fulfilment.Pickup, null, "seed-demo-pending", pendingLines, Pricing.Quote(pendingLines, Fulfilment.Pickup, 499), rxNew.Id, now.AddHours(-2));
        pending.MarkPaid("pi_seed_demo_pending", "visa", "4242", now.AddHours(-2));
        db.Orders.Add(pending);
        await db.SaveChangesAsync(ct);

        // The pending order's stock is reserved, exactly as a live checkout would leave it.
        await db.Inventory.Where(i => i.BranchId == "brn_downtown" && (i.ProductId == "prd_clarith500" || i.ProductId == "prd_advil200"))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Reserved, i => i.Reserved + 1), ct);
        LogSeeded(logger, users.Length, "demo users (+ order history)");
    }

    private static List<OrderLine> Lines(Dictionary<string, Product> byId, params (string Id, int Qty)[] items) =>
        items.Select(x => new OrderLine(x.Id, byId[x.Id].Name, byId[x.Id].GenericName, byId[x.Id].PriceMinor, x.Qty, byId[x.Id].RequiresPrescription)).ToList();

    private static T Load<T>(string file)
    {
        var assembly = typeof(DatabaseSeeder).Assembly;
        using var stream = assembly.GetManifestResourceStream($"Medora.Infrastructure.Persistence.Seed.{file}") ?? throw new InvalidOperationException($"Seed file {file} not embedded.");
        return JsonSerializer.Deserialize<T>(stream, Json) ?? throw new InvalidOperationException($"Seed file {file} is empty.");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} {What}")]
    private static partial void LogSeeded(ILogger logger, int count, string what);

    private sealed record CatalogSeed(List<BranchSeed> Branches, List<ProductSeed> Products, List<CareSeed> CareServices);
    private sealed record BranchSeed(string Id, string Name, string City, string Address, double Latitude, double Longitude, string OpeningHours, long DeliveryFeeMinor, bool Open24h);
    private sealed record ProductSeed(string Id, string Name, string GenericName, string Brand, string Category, string Form, string Strength, long PriceMinor, bool Rx, int MaxQty, string Image, string Description, List<string> Ingredients, double Rating, int Reviews);
    private sealed record CareSeed(string Id, string BranchId, string Name, string Category, string Description, int Duration, int Capacity, long PriceMinor, int MinimumAge, string Image);
    private sealed record KnowledgeSeed(string Source, List<MonographSeed> Monographs);
    private sealed record MonographSeed(string Generic, string Title, Dictionary<string, string> Sections);
    private sealed record InteractionSeed(string A, string B, string Severity, string Effect, string Management);
}
