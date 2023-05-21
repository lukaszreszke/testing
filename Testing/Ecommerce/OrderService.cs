using System.Globalization;

namespace Ecommerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;


public class OrderService
{
    private readonly EcommerceDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly bool _isAdminOrder;

    public OrderService(EcommerceDbContext dbContext, IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;

        _isAdminOrder = IsAdminOrder();
    }

    public void PlaceOrder(int orderId)
    {
        var order = _dbContext.Orders.Find(orderId);

        if (order == null)
        {
            throw new ArgumentException("Order not found", nameof(orderId));
        }

        if (order.Status != OrderStatus.Draft)
        {
            throw new InvalidOperationException("Order must be in Draft status to place the order");
        }

        if (order.Items.Count == 0)
        {
            throw new InvalidOperationException("Order must have at least one item");
        }

        var userId = _httpContextAccessor.HttpContext.User.Identity.Name;
        if (!_isAdminOrder && order.CustomerId != userId)
        {
            throw new InvalidOperationException("Order can only be placed by the same customer or an administrator");
        }

        Money totalValue = Money.Zero;
        foreach (var item in order.Items)
        {
            Money itemValue = item.Price * item.Quantity;
            totalValue += itemValue;
        }

        if (order.IsVipCustomer)
        {
            Money discount = totalValue * 0.1m;
            totalValue -= discount;
        }

        order.TotalValue = totalValue;
        order.Status = OrderStatus.Placed;

        _dbContext.SaveChanges();

        var mailService = new MailService();
        mailService.SendOrderConfirmation(order);

        MessageBus.Publish(new OrderPlacedMessage(order.OrderId));
    }

    private bool IsAdminOrder()
    {
        var user = _dbContext.Users.Find(_httpContextAccessor.HttpContext.User.Identity.Name);
        return user.IsInRole("Administrator");
    }
}

public class Money
{
    public static Money Zero => new Money(0);

    public decimal Amount { get; }

    public Money(string amount)
    {
        if (!decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture,out var parsedAmount))
            throw new ArgumentException("Amount must be a valid decimal number", nameof(amount));

        if (parsedAmount < 0)
            throw new ArgumentException("Amount cannot be negative", nameof(amount));

        this.Amount = parsedAmount;
    }

    public Money(decimal amount)
    {
        Amount = amount;
    }

    public Money Add(Money other)
    {
        decimal result = Amount + other.Amount;
        return new Money(result);
    }

    public static Money operator *(Money money, int multiplier)
    {
        decimal result = money.Amount * multiplier;
        return new Money(result);
    }

    public static Money operator *(int multiplier, Money money)
    {
        return money * multiplier;
    }

    public static Money operator +(Money money1, Money money2)
    {
        decimal result = money1.Amount + money2.Amount;
        return new Money(result);
    }

    public static Money operator *(Money money, decimal multiplier)
    {
        decimal result = money.Amount * multiplier;
        return new Money(result);
    }

    public static Money operator *(decimal multiplier, Money money)
    {
        return money * multiplier;
    }

    public static Money operator -(Money money1, Money money2)
    {
        decimal result = money1.Amount - money2.Amount;
        return new Money(result);
    }
}


public class MailService
{
    public void SendOrderConfirmation(Order order)
    {
        Console.WriteLine($"Sending order confirmation e-mail for order with ID {order.OrderId}");
    }
}

public static class MessageBus
{
    public static void Publish(object message)
    {
        Console.WriteLine($"Publishing message: {message}");
    }
}

public class OrderPlacedMessage
{
    public int OrderId { get; }

    public OrderPlacedMessage(int orderId)
    {
        OrderId = orderId;
    }
}

public class EcommerceDbContext : DbContext
{
    public DbSet<Order> Orders { get; set; }
    public DbSet<User> Users { get; set; }

    public EcommerceDbContext(DbContextOptions<EcommerceDbContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseInMemoryDatabase("ecommerce");
    }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>()
             .HasKey(o => o.OrderId);

        modelBuilder.Entity<Order>()
            .Property(o => o.OrderId)
            .ValueGeneratedOnAdd();

        modelBuilder.Entity<Order>()
            .Property(o => o.CustomerId)
            .IsRequired();

        modelBuilder.Entity<Order>()
            .Property(o => o.Status)
            .IsRequired();

        modelBuilder.Entity<Order>()
            .Property(o => o.TotalValue)
            .IsRequired()
            .HasConversion(m => m.Amount, a => new Money(a));

        modelBuilder.Entity<OrderItem>()
            .HasKey(oi => oi.OrderItemId);

        modelBuilder.Entity<OrderItem>()
            .Property(oi => oi.OrderItemId)
            .ValueGeneratedOnAdd();

        modelBuilder.Entity<OrderItem>()
            .Property(oi => oi.ProductId)
            .IsRequired();

        modelBuilder.Entity<OrderItem>()
            .Property(oi => oi.Price)
            .IsRequired()
            .HasConversion(m => m.Amount, a => new Money(a));
    }
}

public class User
{
    public Guid Id { get; set; }
    public List<string> Roles { get; set; }

    public bool IsInRole(string role)
    {
        return Roles.Contains(role);
    }
}

public class Order
{
    public int OrderId { get; set; }
    public string CustomerId { get; set; }
    public OrderStatus Status { get; set; }
    public Money TotalValue { get; set; }
    public bool IsVipCustomer { get; set; }
    public List<OrderItem> Items { get; set; }
}

public enum OrderStatus
{
    Draft,
    Placed,
    Shipped,
    Delivered
}

public class OrderItem
{
    public int OrderItemId { get; set; }
    public int ProductId { get; set; }
    public Money Price { get; set; }
    public int Quantity { get; set; }
}

