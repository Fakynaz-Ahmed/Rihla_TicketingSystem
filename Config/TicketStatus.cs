namespace Rihla.Config;

/// <summary>
/// HD Ticket status values — match ERPNext exactly (case-sensitive).
/// "Open" is the default status when a ticket is created in ERPNext.
/// </summary>
public static class TicketStatus
{
    /// <summary>Ticket created, waiting for support response. ERPNext default on creation.</summary>
    public const string Open     = "Open";

    /// <summary>Support agent has replied / is actively handling the ticket.</summary>
    public const string Replied  = "Replied";

    /// <summary>Issue has been resolved by support or specialist.</summary>
    public const string Resolved = "Resolved";

    /// <summary>Ticket fully closed (no further action needed).</summary>
    public const string Closed   = "Closed";

    /// <summary>All non-terminal statuses — used for "active ticket" counts.</summary>
    public static readonly IReadOnlyCollection<string> ActiveStatuses =
        [Open, Replied];
}
