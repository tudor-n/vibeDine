namespace RestaurantApi.DTOs.Requests;

public class JoinPartyRequest
{
    // The phone's stored guest id, if it has one. Null/empty on the first scan.
    public Guid? GuestId { get; set; }

    // The phone's persisted device id (client-generated UUID), if it has one.
    // Null/empty/oversized (>64 chars) is treated as absent — no device session
    // is resolved and the response's `session` field is null (back-compat for
    // clients that don't send it).
    public string? DeviceId { get; set; }
}

public class MergePartyRequest
{
    // The guest whose draft becomes the single combined bill.
    public Guid InitiatorGuestId { get; set; }
}
