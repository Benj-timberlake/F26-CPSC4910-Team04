using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BackEnd.Models;

[Table("cart")]
public sealed class CartItem
{
    [Column("id")]
    public uint Id { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [Column("name"), MaxLength(255)]
    public string Name { get; set; } = null!;

    [Column("price", TypeName = "decimal(10,2)")]
    public decimal Price { get; set; }

    [Column("description", TypeName = "text")]
    public string? Description { get; set; }
}
