using System.Text.Json.Serialization;

namespace OrderService.Models
{
    public class OrderItem
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }

        public int OrderId { get; set; }

        // JSON ignore to avoid circular reference
        [JsonIgnore]
        public Order Order { get; set; }
    }
}
