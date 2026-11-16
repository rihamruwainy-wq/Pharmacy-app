namespace Medora.Domain.Catalog;

/// <summary>A physical pharmacy (Airbnb "host"): stock, pickup and delivery are per branch.</summary>
public sealed class Branch
{
    private Branch() { }

    public Branch(string id, string name, string city, string address, double latitude, double longitude, string openingHours, long deliveryFeeMinor, bool open24h)
    {
        Id = id; Name = name; City = city; Address = address; Latitude = latitude; Longitude = longitude;
        OpeningHours = openingHours; DeliveryFeeMinor = deliveryFeeMinor; Open24h = open24h;
    }

    public string Id { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string City { get; private set; } = default!;
    public string Address { get; private set; } = default!;
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public string OpeningHours { get; private set; } = default!;
    public long DeliveryFeeMinor { get; private set; }
    public bool Open24h { get; private set; }
}

/// <summary>
/// A sellable product (Airbnb "listing"). Prescription-only medicines can be browsed but an order
/// containing one needs a prescription and pharmacist verification before it is prepared.
/// </summary>
public sealed class Product
{
    private Product() { }

    public Product(string id, string name, string genericName, string brand, string category, string form, string strength,
        long priceMinor, bool requiresPrescription, int maxQtyPerOrder, string imageUrl, string description,
        IReadOnlyList<string> activeIngredients, double rating, int reviewCount)
    {
        Id = id; Name = name; GenericName = genericName; Brand = brand; Category = category; Form = form; Strength = strength;
        PriceMinor = priceMinor; RequiresPrescription = requiresPrescription; MaxQtyPerOrder = maxQtyPerOrder; ImageUrl = imageUrl;
        Description = description; ActiveIngredients = [.. activeIngredients]; Rating = rating; ReviewCount = reviewCount;
        Currency = "USD";
    }

    public string Id { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    /// <summary>INN / generic name, lower-case (e.g. "ibuprofen") - joins to the knowledge base.</summary>
    public string GenericName { get; private set; } = default!;
    public string Brand { get; private set; } = default!;
    public string Category { get; private set; } = default!;
    public string Form { get; private set; } = default!;
    public string Strength { get; private set; } = default!;
    public long PriceMinor { get; private set; }
    public string Currency { get; private set; } = "USD";
    public bool RequiresPrescription { get; private set; }
    /// <summary>Legal/pack limit per order (e.g. paracetamol), enforced server-side.</summary>
    public int MaxQtyPerOrder { get; private set; }
    public string ImageUrl { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public List<string> ActiveIngredients { get; private set; } = [];
    public double Rating { get; private set; }
    public int ReviewCount { get; private set; }
    public bool IsActive { get; private set; } = true;
    public byte[] RowVersion { get; private set; } = [];

    public void Reprice(long priceMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(priceMinor);
        PriceMinor = priceMinor;
    }
}

/// <summary>
/// Stock of one product at one branch (Airbnb "availability calendar").
/// <c>Available = OnHand - Reserved</c>. Payment holds RESERVE stock; dispensing consumes it.
/// Reservations use a conditional, set-based UPDATE (<c>WHERE OnHand - Reserved &gt;= qty</c>) so two
/// customers can never both take the last pack, without holding locks across round trips.
/// </summary>
public sealed class InventoryItem
{
    private InventoryItem() { }

    public InventoryItem(string branchId, string productId, int onHand, int reorderLevel, DateOnly? nearestExpiry)
    {
        BranchId = branchId; ProductId = productId; OnHand = onHand; ReorderLevel = reorderLevel; NearestExpiry = nearestExpiry;
    }

    public string BranchId { get; private set; } = default!;
    public string ProductId { get; private set; } = default!;
    public int OnHand { get; private set; }
    public int Reserved { get; private set; }
    public int ReorderLevel { get; private set; }
    /// <summary>Earliest expiry among the batches on the shelf (FEFO dispensing; admin expiry report).</summary>
    public DateOnly? NearestExpiry { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public int Available => OnHand - Reserved;
    public bool IsLow => Available <= ReorderLevel;

    public void Restock(int quantity, DateOnly? batchExpiry)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        OnHand += quantity;
        if (batchExpiry is { } e && (NearestExpiry is null || e < NearestExpiry)) NearestExpiry = e;
    }
}
