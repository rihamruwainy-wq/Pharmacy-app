using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ServiceId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    BranchId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CustomerId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    SlotStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SlotId = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    PriceMinor = table.Column<long>(type: "bigint", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    SubjectId = table.Column<string>(type: "varchar(140)", unicode: false, maxLength: 140, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    City = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    OpeningHours = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DeliveryFeeMinor = table.Column<long>(type: "bigint", nullable: false),
                    Open24h = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CareServices",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    BranchId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    CapacityPerSlot = table.Column<int>(type: "int", nullable: false),
                    PriceMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    MinimumAge = table.Column<int>(type: "int", nullable: false),
                    ImageUrl = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareServices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DrugInteractions",
                columns: table => new
                {
                    DrugA = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    DrugB = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    Effect = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Management = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrugInteractions", x => new { x.DrugA, x.DrugB });
                    table.CheckConstraint("CK_Interactions_Ordered", "[DrugA] <= [DrugB]");
                });

            migrationBuilder.CreateTable(
                name: "Favorites",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ProductId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Favorites", x => new { x.UserId, x.ProductId });
                });

            migrationBuilder.CreateTable(
                name: "Inventory",
                columns: table => new
                {
                    BranchId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ProductId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    OnHand = table.Column<int>(type: "int", nullable: false),
                    Reserved = table.Column<int>(type: "int", nullable: false),
                    ReorderLevel = table.Column<int>(type: "int", nullable: false),
                    NearestExpiry = table.Column<DateOnly>(type: "date", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inventory", x => new { x.BranchId, x.ProductId });
                    table.CheckConstraint("CK_Inventory_Quantities", "[OnHand] >= 0 AND [Reserved] >= 0 AND [Reserved] <= [OnHand]");
                });

            migrationBuilder.CreateTable(
                name: "MonographSections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    GenericName = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    DrugTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Section = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", maxLength: -1, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonographSections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CustomerId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    BranchId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Fulfilment = table.Column<int>(type: "int", nullable: false),
                    DeliveryAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    SubtotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    DeliveryFeeMinor = table.Column<long>(type: "bigint", nullable: false),
                    ServiceFeeMinor = table.Column<long>(type: "bigint", nullable: false),
                    RxHandlingFeeMinor = table.Column<long>(type: "bigint", nullable: false),
                    TotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    PrescriptionId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    HoldExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PaymentIntentId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CardBrand = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    CardLast4 = table.Column<string>(type: "varchar(4)", unicode: false, maxLength: 4, nullable: true),
                    RefundId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    RefundMinor = table.Column<long>(type: "bigint", nullable: true),
                    VerifiedBy = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.CheckConstraint("CK_Orders_Status", "[Status] BETWEEN 0 AND 8");
                    table.CheckConstraint("CK_Orders_Total", "[TotalMinor] = [SubtotalMinor] + [DeliveryFeeMinor] + [ServiceFeeMinor] + [RxHandlingFeeMinor] AND [SubtotalMinor] > 0");
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", maxLength: -1, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Prescriptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CustomerId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    PatientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PrescriberName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PrescriberLicense = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ImageReference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prescriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    GenericName = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    Brand = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    Form = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Strength = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PriceMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    RequiresPrescription = table.Column<bool>(type: "bit", nullable: false),
                    MaxQtyPerOrder = table.Column<int>(type: "int", nullable: false),
                    ImageUrl = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ActiveIngredients = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Rating = table.Column<double>(type: "float", nullable: false),
                    ReviewCount = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.CheckConstraint("CK_Products_Price", "[PriceMinor] > 0 AND [MaxQtyPerOrder] > 0");
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    TokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.TokenHash);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "varchar(254)", unicode: false, maxLength: 254, nullable: false),
                    PasswordHash = table.Column<string>(type: "varchar(400)", unicode: false, maxLength: 400, nullable: false),
                    Roles = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BranchId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    BirthDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Allergies = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FailedLogins = table.Column<int>(type: "int", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    GenericName = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    UnitPriceMinor = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    RequiresPrescription = table.Column<bool>(type: "bit", nullable: false),
                    OrderId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderLines_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_CustomerId_SlotStart",
                table: "Appointments",
                columns: new[] { "CustomerId", "SlotStart" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ServiceId_SlotStart_Status",
                table: "Appointments",
                columns: new[] { "ServiceId", "SlotStart", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_At",
                table: "AuditEntries",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_SubjectId_At",
                table: "AuditEntries",
                columns: new[] { "SubjectId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_DrugInteractions_DrugB",
                table: "DrugInteractions",
                column: "DrugB");

            migrationBuilder.CreateIndex(
                name: "IX_Inventory_NearestExpiry",
                table: "Inventory",
                column: "NearestExpiry");

            migrationBuilder.CreateIndex(
                name: "IX_Inventory_ProductId",
                table: "Inventory",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_MonographSections_GenericName",
                table: "MonographSections",
                column: "GenericName");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_OrderId",
                table: "OrderLines",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_ProductId",
                table: "OrderLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BranchId_Status_PaidAt",
                table: "Orders",
                columns: new[] { "BranchId", "Status", "PaidAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId_CreatedAt",
                table: "Orders",
                columns: new[] { "CustomerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId_IdempotencyKey",
                table: "Orders",
                columns: new[] { "CustomerId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PaidAt",
                table: "Orders",
                column: "PaidAt");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PendingHolds",
                table: "Orders",
                column: "HoldExpiresAt",
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Pending",
                table: "OutboxMessages",
                column: "OccurredAt",
                filter: "[ProcessedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_CustomerId_SubmittedAt",
                table: "Prescriptions",
                columns: new[] { "CustomerId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_GenericName",
                table: "Products",
                column: "GenericName");

            migrationBuilder.CreateIndex(
                name: "IX_Products_IsActive_Category_ReviewCount",
                table: "Products",
                columns: new[] { "IsActive", "Category", "ReviewCount" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_FamilyId",
                table: "RefreshTokens",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail",
                table: "Users",
                column: "NormalizedEmail",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Appointments");

            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "Branches");

            migrationBuilder.DropTable(
                name: "CareServices");

            migrationBuilder.DropTable(
                name: "DrugInteractions");

            migrationBuilder.DropTable(
                name: "Favorites");

            migrationBuilder.DropTable(
                name: "Inventory");

            migrationBuilder.DropTable(
                name: "MonographSections");

            migrationBuilder.DropTable(
                name: "OrderLines");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "Prescriptions");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Orders");
        }
    }
}
