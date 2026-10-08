namespace BackEnd.Models;

public sealed class CatalogItem
{
    public int Id { get; set; }
    public int SponsorId { get; set; }
    public string ItemId { get; set; } = "";
}
