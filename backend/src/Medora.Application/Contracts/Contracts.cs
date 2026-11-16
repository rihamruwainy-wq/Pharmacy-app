namespace Medora.Application.Contracts;

// ------------------------------------------------------------------ Shared
public sealed record Paged<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
public sealed record IdsRequest(IReadOnlyList<string>? Ids);

// ------------------------------------------------------------------ Identity
public sealed record UserDto(string Id, string FirstName, string LastName, string Email, IReadOnlyList<string> Roles, string? BranchId, string? Allergies);
public sealed record LookupRequest(string Email);
public sealed record LookupResponse(bool Exists);
public sealed record LoginRequest(string Email, string Password);
public sealed record RegisterRequest(string FirstName, string LastName, string Email, string Password, DateOnly BirthDate);
public sealed record RefreshRequest(string RefreshToken);
public sealed record AuthResponse(UserDto User, string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);
public sealed record UpdateProfileRequest(string FirstName, string LastName, string? Allergies);

// ------------------------------------------------------------------ Catalog
public sealed record CategoryDto(string Id, string Label, string Icon);
public sealed record BranchDto(string Id, string Name, string City, string Address, double Latitude, double Longitude, string OpeningHours, long DeliveryFeeMinor, bool Open24h);
public sealed record ProductSummaryDto(string Id, string Name, string Brand, string Category, string Form, string Strength, long PriceMinor, string Currency,
    bool RequiresPrescription, string ImageUrl, double Rating, int ReviewCount);
public sealed record ProductDto(string Id, string Name, string GenericName, string Brand, string Category, string Form, string Strength, long PriceMinor, string Currency,
    bool RequiresPrescription, int MaxQtyPerOrder, string ImageUrl, string Description, IReadOnlyList<string> ActiveIngredients, double Rating, int ReviewCount);
/// <summary>Search page plus what the server understood: a typo correction and the categories a symptom mapped to.</summary>
public sealed record SearchResultDto(IReadOnlyList<ProductSummaryDto> Items, int Page, int PageSize, int Total, string? DidYouMean, IReadOnlyList<string> MatchedCategories);
public sealed record SuggestionDto(string Id, string Name, string Brand, bool RequiresPrescription);
public sealed record SuggestResponse(IReadOnlyList<SuggestionDto> Products, IReadOnlyList<string> Categories, string? DidYouMean);
/// <summary>status: in_stock | low | out (exact counts are not exposed to customers).</summary>
public sealed record StockDto(string ProductId, string Status, int Available);

// ------------------------------------------------------------------ Orders
public sealed record OrderLineRequest(string ProductId, int Quantity);
public sealed record QuoteRequest(string BranchId, string Fulfilment, IReadOnlyList<OrderLineRequest>? Lines);
public sealed record NewOrderRequest(string BranchId, string Fulfilment, string? DeliveryAddress, IReadOnlyList<OrderLineRequest>? Lines, string? PrescriptionId);
public sealed record QuoteDto(string Currency, long SubtotalMinor, long DeliveryFeeMinor, long ServiceFeeMinor, long RxHandlingFeeMinor, long TotalMinor, bool RequiresPrescription);
public sealed record OrderLineDto(string ProductId, string Name, long UnitPriceMinor, int Quantity, bool RequiresPrescription, long LineTotalMinor);
public sealed record OrderDto(string Id, string BranchId, string Fulfilment, string? DeliveryAddress, string Status, IReadOnlyList<OrderLineDto> Lines,
    string Currency, long SubtotalMinor, long DeliveryFeeMinor, long ServiceFeeMinor, long RxHandlingFeeMinor, long TotalMinor,
    DateTimeOffset HoldExpiresAt, DateTimeOffset CreatedAt, DateTimeOffset? PaidAt, string? CardBrand, string? CardLast4,
    long? RefundMinor, string? RejectionReason, string? PrescriptionId, bool CanCancel, long RefundIfCancelledMinor);
/// <summary>A provider payment-method token (Stripe <c>pm_…</c>); never a card number.</summary>
public sealed record PayRequest(string PaymentMethodId);
public sealed record NewPrescriptionRequest(string PatientName, string PrescriberName, string? PrescriberLicense, string ImageReference, string? Notes, DateOnly IssuedOn);
public sealed record PrescriptionDto(string Id, string PatientName, string PrescriberName, string? PrescriberLicense, string ImageReference, string? Notes, DateOnly IssuedOn, DateTimeOffset SubmittedAt, bool IsWithinValidity);

// ------------------------------------------------------------------ Pharmacist
public sealed record InteractionAlertDto(string DrugA, string DrugB, string Severity, string Effect, string Management);
public sealed record VerificationItemDto(OrderDto Order, string CustomerName, string? CustomerAllergies, int CustomerAge, PrescriptionDto? Prescription, IReadOnlyList<InteractionAlertDto> Alerts, IReadOnlyList<string> RecentMedicines);
public sealed record VerifyRequest(bool Approve, string? Reason);
public sealed record AdvanceRequest(string To);

// ------------------------------------------------------------------ Care services (appointments)
public sealed record CareServiceDto(string Id, string BranchId, string Name, string Category, string Description, int DurationMinutes, long PriceMinor, string Currency, int MinimumAge, string ImageUrl);
public sealed record SlotDto(string SlotId, DateTime Start, int SpotsLeft);
public sealed record NewAppointmentRequest(string SlotId, string? Notes);
public sealed record AppointmentDto(string Id, string ServiceId, string ServiceName, string BranchId, DateTime Start, string Status, long PriceMinor, string? Notes, bool CanCancel);

// ------------------------------------------------------------------ Admin
public sealed record DashboardDto(long SalesTodayMinor, long Sales7dMinor, int OrdersToday, int PendingVerification, int LowStockItems, int ExpiringWithin60Days, IReadOnlyDictionary<string, int> OrdersByStatus);
public sealed record InventoryRowDto(string BranchId, string ProductId, string ProductName, int OnHand, int Reserved, int Available, int ReorderLevel, DateOnly? NearestExpiry, bool IsLow);
public sealed record RestockRequest(int Quantity, DateOnly? BatchExpiry);
public sealed record RepriceRequest(long PriceMinor);

// ------------------------------------------------------------------ AI (staff only)
public sealed record CopilotRequest(string Question, string? OrderId);
public sealed record CitationDto(string SourceId, string Title, string Quote, string Provenance);
public sealed record SourceDto(string Id, string Title);
public sealed record CopilotResponse(string Answer, IReadOnlyList<CitationDto> Citations, IReadOnlyList<InteractionAlertDto> Alerts, IReadOnlyList<SourceDto> Sources, bool AiAvailable);
public sealed record ChatTurn(string Role, string Content);
public sealed record AssistantRequest(IReadOnlyList<ChatTurn>? Messages);
public sealed record ToolCallDto(string Name, string Input);
public sealed record AssistantResponse(string Answer, IReadOnlyList<ToolCallDto> ToolCalls, bool AiAvailable);
