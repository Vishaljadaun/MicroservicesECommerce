using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using OrderService.Data;
using OrderService.Models;
using OrderService.DTOs;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// 🧠 JSON options to ignore cycles (safe default)
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    options.SerializerOptions.MaxDepth = 64;
});

// 🧩 Register EF Core
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 🧩 Register HttpClient to call ProductService
builder.Services.AddHttpClient("productClient", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ProductService:BaseUrl"]!);
});

// 🧩 Swagger setup
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 🧩 Ensure database created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    db.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => "Order Service is running ✅");

// ✅ Get all orders
app.MapGet("/api/orders", async (OrderDbContext db) =>
{
    var orders = await db.Orders.Include(o => o.Items).ToListAsync();

    // Map to DTOs
    var result = orders.Select(o => new OrderDto
    {
        Id = o.Id,
        CustomerName = o.CustomerName,
        OrderDate = o.OrderDate,
        TotalAmount = o.TotalAmount,
        Items = o.Items.Select(i => new OrderItemDto
        {
            ProductId = i.ProductId,
            Quantity = i.Quantity,
            Price = i.Price
        }).ToList()
    });

    return Results.Ok(result);
});

// ✅ Get single order by ID
app.MapGet("/api/orders/{id:int}", async (int id, OrderDbContext db) =>
{
    var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id);
    if (order is null) return Results.NotFound();

    var dto = new OrderDto
    {
        Id = order.Id,
        CustomerName = order.CustomerName,
        OrderDate = order.OrderDate,
        TotalAmount = order.TotalAmount,
        Items = order.Items.Select(i => new OrderItemDto
        {
            ProductId = i.ProductId,
            Quantity = i.Quantity,
            Price = i.Price
        }).ToList()
    };

    return Results.Ok(dto);
});

// ✅ Create new order (call ProductService for price)
app.MapPost("/api/orders", async (OrderDto dto, OrderDbContext db, IHttpClientFactory httpFactory) =>
{
    var client = httpFactory.CreateClient("productClient");
    decimal total = 0;

    // Verify products from ProductService
    foreach (var item in dto.Items)
    {
        var response = await client.GetAsync($"/api/products/{item.ProductId}");
        if (!response.IsSuccessStatusCode)
            return Results.BadRequest($"Product with ID {item.ProductId} not found.");

        var json = await response.Content.ReadAsStringAsync();
        dynamic? product = JsonConvert.DeserializeObject(json);

        // auto assign price from ProductService
        item.Price = (decimal)product.price;
        total += item.Price * item.Quantity;
    }

    var order = new Order
    {
        CustomerName = dto.CustomerName,
        OrderDate = DateTime.UtcNow,
        TotalAmount = total,
        Items = dto.Items.Select(i => new OrderItem
        {
            ProductId = i.ProductId,
            Quantity = i.Quantity,
            Price = i.Price
        }).ToList()
    };

    db.Orders.Add(order);
    await db.SaveChangesAsync();

    dto.Id = order.Id;
    dto.TotalAmount = order.TotalAmount;

    return Results.Created($"/api/orders/{order.Id}", dto);
});

// ✅ Delete order
app.MapDelete("/api/orders/{id:int}", async (int id, OrderDbContext db) =>
{
    var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id);
    if (order is null) return Results.NotFound();

    db.Orders.Remove(order);
    await db.SaveChangesAsync();

    return Results.Ok($"Order {id} deleted successfully");
});

app.Run();
