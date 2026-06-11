namespace RestaurantApi.DTOs.Requests;

public class JoinPartyRequest
{
    // The phone's stored guest id, if it has one. Null/empty on the first scan.
    public Guid? GuestId { get; set; }
}

public class MergePartyRequest
{
    // The guest whose draft becomes the single combined bill.
    public Guid InitiatorGuestId { get; set; }
}
