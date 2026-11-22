using Microsoft.EntityFrameworkCore;

namespace AiSample.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
}

public sealed class User
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

public sealed class Order
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public List<OrderLine> Lines { get; set; } = [];
}

public sealed class OrderLine
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public decimal Amount { get; set; }
}
