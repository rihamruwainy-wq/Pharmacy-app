using Medora.Application.Abstractions;
using Medora.Domain.Care;
using Medora.Domain.Catalog;
using Medora.Domain.Identity;
using Medora.Domain.Knowledge;
using Medora.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Medora.Infrastructure.Persistence;

public sealed class MedoraDbContext(DbContextOptions<MedoraDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryItem> Inventory => Set<InventoryItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<CareService> CareServices => Set<CareService>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<MonographSection> MonographSections => Set<MonographSection>();
    public DbSet<DrugInteraction> DrugInteractions => Set<DrugInteraction>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Bounded strings by default (no accidental nvarchar(max) on indexable columns); long text opts out explicitly.
        configurationBuilder.Properties<string>().AreUnicode(true).HaveMaxLength(200);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var b = modelBuilder;
        b.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            Id(e.Property(x => x.Id));
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.NormalizedEmail).HasMaxLength(254).IsUnicode(false);
            e.HasIndex(x => x.NormalizedEmail).IsUnique();
            e.Property(x => x.PasswordHash).HasMaxLength(400).IsUnicode(false);
            e.Property(x => x.Allergies).HasMaxLength(500);
            Id(e.Property(x => x.BranchId));
            e.Property(x => x.Roles); // primitive collection -> JSON column
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Ignore(x => x.IsStaff);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasKey(x => x.TokenHash);
            e.Property(x => x.TokenHash).HasMaxLength(64).IsUnicode(false);
            Id(e.Property(x => x.UserId));
            e.HasIndex(x => x.FamilyId);
            e.HasIndex(x => x.UserId);
        });

        b.Entity<Favorite>(e =>
        {
            e.HasKey(x => new { x.UserId, x.ProductId });
            Id(e.Property(x => x.UserId));
            Id(e.Property(x => x.ProductId));
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(60).IsUnicode(false);
            e.Property(x => x.Payload).HasMaxLength(-1);
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.HasIndex(x => x.OccurredAt).HasFilter("[ProcessedAt] IS NULL").HasDatabaseName("IX_Outbox_Pending");
        });

        b.Entity<AuditEntry>(e =>
        {
            e.HasKey(x => x.Id);
            Id(e.Property(x => x.ActorId));
            e.Property(x => x.Action).HasMaxLength(60).IsUnicode(false);
            e.Property(x => x.SubjectId).HasMaxLength(140).IsUnicode(false);
            e.Property(x => x.Detail).HasMaxLength(2000);
            e.HasIndex(x => x.At);
            e.HasIndex(x => new { x.SubjectId, x.At });
        });

        b.Entity<Branch>(e =>
        {
            e.HasKey(x => x.Id);
            Id(e.Property(x => x.Id));
        });

        b.Entity<Product>(e =>
        {
            e.HasKey(x => x.Id);
            Id(e.Property(x => x.Id));
            e.Property(x => x.GenericName).HasMaxLength(80).IsUnicode(false);
            e.Property(x => x.Category).HasMaxLength(40).IsUnicode(false);
            e.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.ImageUrl).HasMaxLength(500).IsUnicode(false);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.ActiveIngredients);
            e.Property(x => x.RowVersion).IsRowVersion();
            // Browse by category ordered by popularity; covering the summary columns avoids key lookups.
            e.HasIndex(x => new { x.IsActive, x.Category, x.ReviewCount });
            e.HasIndex(x => x.GenericName);
            e.ToTable(t => t.HasCheckConstraint("CK_Products_Price", "[PriceMinor] > 0 AND [MaxQtyPerOrder] > 0"));
        });

        b.Entity<InventoryItem>(e =>
        {
            e.HasKey(x => new { x.BranchId, x.ProductId });
            Id(e.Property(x => x.BranchId));
            Id(e.Property(x => x.ProductId));
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Ignore(x => x.Available);
            e.Ignore(x => x.IsLow);
            e.HasIndex(x => x.ProductId);
            e.HasIndex(x => x.NearestExpiry);
            // Last line of defence against overselling: the database itself refuses negative or over-reserved stock.
            e.ToTable(t => t.HasCheckConstraint("CK_Inventory_Quantities", "[OnHand] >= 0 AND [Reserved] >= 0 AND [Reserved] <= [OnHand]"));
        });

        b.Entity<Order>(e =>
        {
            e.HasKey(x => x.Id);
            Id(e.Property(x => x.Id));
            Id(e.Property(x => x.CustomerId));
            Id(e.Property(x => x.BranchId));
            Id(e.Property(x => x.PrescriptionId));
            Id(e.Property(x => x.PaymentIntentId));
            Id(e.Property(x => x.RefundId));
            Id(e.Property(x => x.VerifiedBy));
            e.Property(x => x.IdempotencyKey).HasMaxLength(100).IsUnicode(false);
            e.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.DeliveryAddress).HasMaxLength(300);
            e.Property(x => x.RejectionReason).HasMaxLength(500);
            e.Property(x => x.CardBrand).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.CardLast4).HasMaxLength(4).IsUnicode(false);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.OwnsMany(x => x.Lines, l =>
            {
                l.ToTable("OrderLines");
                l.WithOwner().HasForeignKey("OrderId");
                l.Property<string>("OrderId").HasMaxLength(64).IsUnicode(false); // must match Orders.Id exactly (FK type)
                l.Property<int>("Id").ValueGeneratedOnAdd();
                l.HasKey("Id");
                Id(l.Property(x => x.ProductId));
                l.Property(x => x.GenericName).HasMaxLength(80).IsUnicode(false);
                l.Ignore(x => x.LineTotalMinor);
                l.HasIndex(x => x.ProductId);
            });
            e.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Ignore(x => x.ContainsPrescriptionItems);
            e.HasIndex(x => new { x.CustomerId, x.IdempotencyKey }).IsUnique();            // idempotent create
            e.HasIndex(x => new { x.CustomerId, x.CreatedAt });                             // "my orders"
            e.HasIndex(x => new { x.BranchId, x.Status, x.PaidAt });                        // pharmacist queues
            e.HasIndex(x => x.PaidAt);                                                      // sales analytics
            e.HasIndex(x => x.HoldExpiresAt).HasFilter("[Status] = 0").HasDatabaseName("IX_Orders_PendingHolds");
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Orders_Status", "[Status] BETWEEN 0 AND 8");
                t.HasCheckConstraint("CK_Orders_Total", "[TotalMinor] = [SubtotalMinor] + [DeliveryFeeMinor] + [ServiceFeeMinor] + [RxHandlingFeeMinor] AND [SubtotalMinor] > 0");
            });
        });

        b.Entity<Prescription>(e =>
        {
            e.HasKey(x => x.Id);
            Id(e.Property(x => x.Id));
            Id(e.Property(x => x.CustomerId));
            e.Property(x => x.ImageReference).HasMaxLength(300);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasIndex(x => new { x.CustomerId, x.SubmittedAt });
        });

        b.Entity<CareService>(e =>
        {
            e.HasKey(x => x.Id);
            Id(e.Property(x => x.Id));
            Id(e.Property(x => x.BranchId));
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            e.Property(x => x.ImageUrl).HasMaxLength(500).IsUnicode(false);
        });

        b.Entity<Appointment>(e =>
        {
            e.HasKey(x => x.Id);
            Id(e.Property(x => x.Id));
            Id(e.Property(x => x.ServiceId));
            Id(e.Property(x => x.BranchId));
            Id(e.Property(x => x.CustomerId));
            e.Property(x => x.SlotId).HasMaxLength(80).IsUnicode(false);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.ServiceId, x.SlotStart, x.Status });
            e.HasIndex(x => new { x.CustomerId, x.SlotStart });
        });

        b.Entity<MonographSection>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(80).IsUnicode(false);
            e.Property(x => x.GenericName).HasMaxLength(80).IsUnicode(false);
            e.Property(x => x.Content).HasMaxLength(-1);
            e.Ignore(x => x.Title);
            e.HasIndex(x => x.GenericName);
        });

        b.Entity<DrugInteraction>(e =>
        {
            e.HasKey(x => new { x.DrugA, x.DrugB });
            e.Property(x => x.DrugA).HasMaxLength(80).IsUnicode(false);
            e.Property(x => x.DrugB).HasMaxLength(80).IsUnicode(false);
            e.Property(x => x.Effect).HasMaxLength(1000);
            e.Property(x => x.Management).HasMaxLength(1000);
            e.HasIndex(x => x.DrugB);
            e.ToTable(t => t.HasCheckConstraint("CK_Interactions_Ordered", "[DrugA] <= [DrugB]"));
        });
    }

    /// <summary>Opaque ASCII ids (prefix_ + 32 hex): varchar(64) keeps indexes narrow.</summary>
    private static void Id<T>(PropertyBuilder<T> p) => p.HasMaxLength(64).IsUnicode(false);
}
