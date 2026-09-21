using Microsoft.EntityFrameworkCore;
using ShopPay.Api.Domain;

namespace ShopPay.Api.Data;

public class ShopPayDbContext(DbContextOptions<ShopPayDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(product =>
        {
            product.HasKey(p => p.Id);
            product.Property(p => p.Name).IsRequired().HasMaxLength(200);
            product.Property(p => p.Description).HasMaxLength(1000);

            // Sample data, created together with the database.
            product.HasData(SeedData.Products);
        });

        modelBuilder.Entity<Order>(order =>
        {
            order.HasKey(o => o.Id);

            // Store "Pending" / "Paid" / "Cancelled" as readable text instead of 0 / 1 / 2.
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            order.Property(o => o.Currency).IsRequired().HasMaxLength(3);
            order.Property(o => o.StripeSessionId).HasMaxLength(255);
            order.Property(o => o.StripePaymentIntentId).HasMaxLength(255);

            // A Stripe session can belong to at most one order (NULLs are allowed many times).
            order.HasIndex(o => o.StripeSessionId).IsUnique();

            order.HasMany(o => o.Items)
                 .WithOne()
                 .HasForeignKey(i => i.OrderId)
                 .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(item =>
        {
            item.HasKey(i => i.Id);
            item.Property(i => i.ProductName).IsRequired().HasMaxLength(200);

            item.HasOne<Product>()
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
