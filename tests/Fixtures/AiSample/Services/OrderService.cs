using AiSample.Data;

namespace AiSample.Services;

public sealed class OrderService(AppDbContext db)
{
    private static int s_processed;

    public List<Order> GetOrdersWithLines(int userId)
    {
        var orders = db.Orders.Where(o => o.UserId == userId).ToList();
        foreach (var order in orders)
        {
            order.Lines = db.OrderLines.Where(l => l.OrderId == order.Id).ToList();
        }

        return orders;
    }

    public static decimal SumFirst(IReadOnlyList<OrderLine> lines, int count)
    {
        decimal total = 0;
        for (var i = 0; i <= count; i++)
        {
            total += lines[i].Amount;
        }

        return total;
    }

    public static int ProcessAll(IReadOnlyList<Order> orders)
    {
        Parallel.ForEach(orders, _ => s_processed++);
        return s_processed;
    }
}
