using Medora.Application.Features.Ai;
using Medora.Domain.Care;
using Medora.Domain.Identity;
using Medora.Domain.Knowledge;
using Medora.Domain.Orders;

namespace Medora.UnitTests;

public class OrderLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static Order NewOrder(bool rx, Fulfilment fulfilment = Fulfilment.Pickup)
    {
        List<OrderLine> lines = [new("prd_a", "A", "ibuprofen", 899, 2, false)];
        if (rx) lines.Add(new OrderLine("prd_b", "B", "amoxicillin", 1199, 1, true));
        return Order.PlaceHold("usr_x", "brn_x", fulfilment, fulfilment == Fulfilment.Delivery ? "1 Main St" : null, "key-12345", lines, Pricing.Quote(lines, fulfilment, 499), rx ? "rx_1" : null, Now);
    }

    [Fact]
    public void Otc_order_goes_straight_to_preparing_when_paid()
    {
        var order = NewOrder(rx: false);
        order.MarkPaid("pi_1", "visa", "4242", Now);
        Assert.Equal(OrderStatus.Preparing, order.Status);
    }

    [Fact]
    public void Rx_order_waits_for_pharmacist_verification_when_paid()
    {
        var order = NewOrder(rx: true);
        order.MarkPaid("pi_1", "visa", "4242", Now);
        Assert.Equal(OrderStatus.AwaitingVerification, order.Status);
        order.Verify("usr_pharm", Now);
        Assert.Equal(OrderStatus.Preparing, order.Status);
    }

    [Fact]
    public void Hold_expires_after_fifteen_minutes()
    {
        var order = NewOrder(rx: false);
        Assert.True(order.IsHoldActive(Now.AddMinutes(14)));
        Assert.False(order.IsHoldActive(Now.AddMinutes(15)));
    }

    [Theory]
    [InlineData(OrderStatus.PendingPayment, OrderStatus.Completed)]
    [InlineData(OrderStatus.Completed, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.ReadyForPickup, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Rejected, OrderStatus.Preparing)]
    public void Illegal_transitions_are_rejected(OrderStatus from, OrderStatus to) =>
        Assert.False(OrderStateMachine.CanTransition(from, to));

    [Fact]
    public void Same_state_transition_is_an_idempotent_no_op()
    {
        var order = NewOrder(rx: false);
        order.TransitionTo(OrderStatus.PendingPayment, Now);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
    }

    [Fact]
    public void Illegal_transition_throws()
    {
        var order = NewOrder(rx: false);
        Assert.Throws<InvalidOperationException>(() => order.TransitionTo(OrderStatus.Completed, Now));
    }
}

public class PricingTests
{
    [Fact]
    public void Service_fee_is_five_percent_clamped_and_rx_lines_add_handling()
    {
        List<OrderLine> lines = [new("prd_a", "A", "x", 1000, 2, false), new("prd_b", "B", "y", 1199, 1, true)];
        var q = Pricing.Quote(lines, Fulfilment.Pickup, 499);
        Assert.Equal(3199, q.SubtotalMinor);
        Assert.Equal(160, q.ServiceFeeMinor);          // 5% of 3199 = 159.95 -> 160 (half away from zero)
        Assert.Equal(150, q.RxHandlingFeeMinor);
        Assert.Equal(0, q.DeliveryFeeMinor);           // pickup
        Assert.Equal(3199 + 160 + 150, q.TotalMinor);
    }

    [Fact]
    public void Delivery_is_free_above_threshold()
    {
        List<OrderLine> small = [new("prd_a", "A", "x", 1000, 1, false)];
        List<OrderLine> big = [new("prd_a", "A", "x", 2500, 2, false)];
        Assert.Equal(499, Pricing.Quote(small, Fulfilment.Delivery, 499).DeliveryFeeMinor);
        Assert.Equal(0, Pricing.Quote(big, Fulfilment.Delivery, 499).DeliveryFeeMinor);
    }

    [Theory]
    [InlineData(100, 50)]       // floor
    [InlineData(100_000, 500)]  // cap
    public void Service_fee_is_clamped(long subtotal, long expected)
    {
        List<OrderLine> lines = [new("prd_a", "A", "x", subtotal, 1, false)];
        Assert.Equal(expected, Pricing.Quote(lines, Fulfilment.Pickup, 0).ServiceFeeMinor);
    }
}

public class RefundPolicyTests
{
    private static Order Paid(bool rx)
    {
        List<OrderLine> lines = [new("prd_a", "A", "x", 2000, 1, rx)];
        var o = Order.PlaceHold("usr_x", "brn_x", Fulfilment.Pickup, null, "key-12345", lines, Pricing.Quote(lines, Fulfilment.Pickup, 0), rx ? "rx_1" : null, DateTimeOffset.UnixEpoch);
        o.MarkPaid("pi", "visa", "4242", DateTimeOffset.UnixEpoch);
        return o;
    }

    [Fact]
    public void Awaiting_verification_refunds_in_full() =>
        Assert.Equal(Paid(rx: true).TotalMinor, RefundPolicy.RefundFor(Paid(rx: true), OrderStatus.AwaitingVerification));

    [Fact]
    public void Preparing_keeps_the_service_fee()
    {
        var o = Paid(rx: false);
        Assert.Equal(o.TotalMinor - o.ServiceFeeMinor, RefundPolicy.RefundFor(o, OrderStatus.Preparing));
    }

    [Theory]
    [InlineData(OrderStatus.ReadyForPickup)]
    [InlineData(OrderStatus.OutForDelivery)]
    [InlineData(OrderStatus.Completed)]
    public void Handed_over_orders_cannot_be_cancelled_by_customer(OrderStatus status) =>
        Assert.False(RefundPolicy.CustomerCanCancel(status));
}

public class InteractionScreenTests
{
    private static readonly DrugInteraction[] Table =
    [
        new("warfarin", "ibuprofen", InteractionSeverity.Major, "bleeding", "avoid"),
        new("simvastatin", "clarithromycin", InteractionSeverity.Contraindicated, "rhabdo", "suspend"),
        new("lisinopril", "ibuprofen", InteractionSeverity.Moderate, "AKI", "paracetamol"),
    ];

    [Fact]
    public void Pairs_are_stored_in_canonical_order() =>
        Assert.Equal(("ibuprofen", "warfarin"), (Table[0].DrugA, Table[0].DrugB));

    [Fact]
    public void Finds_every_pair_regardless_of_input_order_and_case_sorted_by_severity()
    {
        var found = InteractionScreen.Find(["Clarithromycin", "IBUPROFEN", "warfarin", "simvastatin"], Table);
        Assert.Equal(2, found.Count);
        Assert.Equal(InteractionSeverity.Contraindicated, found[0].Severity);
        Assert.Equal(InteractionSeverity.Major, found[1].Severity);
    }

    [Fact]
    public void Single_drug_has_no_interactions() =>
        Assert.Empty(InteractionScreen.Find(["warfarin"], Table));

    [Fact]
    public void History_vs_history_pairs_are_not_reported_but_order_vs_history_pairs_are()
    {
        // Patient already takes warfarin + ibuprofen (history pair); the new order contains clarithromycin.
        var found = InteractionScreen.Find(["clarithromycin"], ["warfarin", "ibuprofen", "simvastatin"], Table);
        Assert.Single(found);
        Assert.Equal(("clarithromycin", "simvastatin"), (found[0].DrugA, found[0].DrugB));
    }

    [Fact]
    public void Allergy_screen_maps_declared_allergy_to_drug_class()
    {
        var alerts = AllergyScreen.Find("Penicillin (rash)", ["amoxicillin", "ibuprofen"]);
        var alert = Assert.Single(alerts);
        Assert.Equal(("penicillins", "amoxicillin"), (alert.DrugClass, alert.GenericName));
        Assert.Empty(AllergyScreen.Find(null, ["amoxicillin"]));
        Assert.Empty(AllergyScreen.Find("hay fever", ["amoxicillin"]));
    }
}

public class KnowledgeIndexTests
{
    private static readonly KnowledgeIndex Index = new(
    [
        new("kb_warfarin_interactions", "warfarin", "Warfarin - Interactions", "NSAIDs such as ibuprofen increase bleeding risk. Clarithromycin raises the INR.", "demo"),
        new("kb_warfarin_counselling", "warfarin", "Warfarin - Counselling", "Keep a record of INR results. Use paracetamol for pain.", "demo"),
        new("kb_cetirizine_dosing", "cetirizine", "Cetirizine - Indications & dosing", "Allergic rhinitis: 10 mg once daily.", "demo"),
        new("kb_metformin_dosing", "metformin", "Metformin - Indications & dosing", "Start 500 mg with meals; review when eGFR below 45.", "demo"),
    ]);

    [Fact]
    public void Ranks_the_section_that_matches_the_question_first()
    {
        var hits = Index.Search("Does clarithromycin interact with warfarin?", []);
        Assert.Equal("kb_warfarin_interactions", hits[0].Id);
    }

    [Fact]
    public void Plural_and_case_insensitive_terms_match() =>
        Assert.Contains(Index.Search("WARFARIN INTERACTIONS", []), h => h.Id == "kb_warfarin_interactions");

    [Fact]
    public void Case_drugs_are_boosted_even_without_lexical_overlap()
    {
        var hits = Index.Search("what should I tell the patient?", ["metformin"]);
        Assert.Equal("metformin", hits[0].GenericName);
    }

    [Fact]
    public void Unrelated_question_returns_nothing() =>
        Assert.Empty(Index.Search("zzzz qqqq", []));
}

public class SlotScheduleTests
{
    [Fact]
    public void Generates_slots_inside_opening_hours_and_none_on_sunday()
    {
        var monday = new DateOnly(2026, 11, 16);
        var slots = SlotSchedule.SlotsOn(monday, 30).ToList();
        Assert.Equal(16, slots.Count); // 09:00..16:30
        Assert.Equal(new DateTime(2026, 11, 16, 9, 0, 0), slots[0]);
        Assert.Empty(SlotSchedule.SlotsOn(new DateOnly(2026, 11, 15), 30));
    }

    [Fact]
    public void Slot_ids_round_trip_and_off_grid_times_are_not_scheduled()
    {
        var start = new DateTime(2026, 11, 16, 9, 30, 0);
        var id = SlotSchedule.SlotId("svc_flu", start);
        Assert.Equal("svc_flu~2026-11-16~0930", id);
        Assert.True(SlotSchedule.TryParse(id, out var service, out var parsed));
        Assert.Equal(("svc_flu", start), (service, parsed));
        Assert.False(SlotSchedule.IsScheduled(new DateTime(2026, 11, 16, 9, 7, 0), 15));
    }

    [Fact]
    public void Free_cancellation_ends_two_hours_before()
    {
        var a = new Appointment("svc_flu", "brn_x", "usr_x", new DateTime(2026, 11, 16, 12, 0, 0), "id", 0, null, DateTimeOffset.UnixEpoch);
        Assert.True(a.CanCustomerCancel(new DateTime(2026, 11, 16, 10, 0, 0)));
        Assert.False(a.CanCustomerCancel(new DateTime(2026, 11, 16, 10, 1, 0)));
    }
}

public class IdentityRuleTests
{
    [Theory]
    [InlineData("short1", false)]
    [InlineData("onlyletters", false)]
    [InlineData("dana-secret-9", false)] // contains the email local part
    [InlineData("Correct horse 7", true)]
    public void Password_policy(string password, bool ok) =>
        Assert.Equal(ok, PasswordPolicy.IsAcceptable(password, "dana@example.com", "Dana"));

    [Fact]
    public void Five_failed_logins_lock_the_account_for_fifteen_minutes()
    {
        var user = User.Register("A", "B", "a@b.co", new DateOnly(1990, 1, 1), DateTimeOffset.UnixEpoch);
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < User.MaxFailedLogins; i++) user.RegisterFailedLogin(now);
        Assert.True(user.IsLockedOut(now.AddMinutes(14)));
        Assert.False(user.IsLockedOut(now.AddMinutes(15)));
    }
}
