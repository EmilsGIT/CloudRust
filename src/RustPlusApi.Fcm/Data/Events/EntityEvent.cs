namespace RustPlusApi.Fcm.Data.Events;

public sealed record EntityEvent
{
    public int? EntityType { get; set; }
    public uint? EntityId { get; set; }
    public string? EntityName { get; set; }
}
