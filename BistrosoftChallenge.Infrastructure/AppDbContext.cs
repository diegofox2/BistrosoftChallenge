using Microsoft.EntityFrameworkCore;
using BistrosoftChallenge.Domain.Entities;
using MassTransit;
using BistrosoftChallenge.Infrastructure.SagaStates;

namespace BistrosoftChallenge.Infrastructure
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Customer> Customers { get; set; } = null!;
        public DbSet<Product> Products { get; set; } = null!;
        public DbSet<Order> Orders { get; set; } = null!;
        public DbSet<OrderItem> OrderItems { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.AddInboxStateEntity();
            modelBuilder.AddOutboxMessageEntity();
            modelBuilder.AddOutboxStateEntity();

            modelBuilder.Entity<CreateCustomerState>().HasKey(x => x.CorrelationId);
            modelBuilder.Entity<CreateOrderState>().HasKey(x => x.CorrelationId);
            modelBuilder.Entity<ChangeOrderStatusState>().HasKey(x => x.CorrelationId);

            modelBuilder.Entity<Customer>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Name).IsRequired();
                b.Property(x => x.Email).IsRequired();
                b.HasIndex(x => x.Email).IsUnique();
                b.HasMany(x => x.Orders).WithOne(x => x.Customer).HasForeignKey(x => x.CustomerId);
            });

            modelBuilder.Entity<Product>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Name).IsRequired();
                b.Property(x => x.Price).IsRequired();
                b.Property(x => x.StockQuantity).IsRequired();
            });

            modelBuilder.Entity<Order>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.TotalAmount).IsRequired();
                b.Property(x => x.CreatedAt).IsRequired();
                b.Property(x => x.Status).IsRequired();
                b.HasMany(x => x.OrderItems).WithOne(x => x.Order).HasForeignKey(x => x.OrderId);
            });

            modelBuilder.Entity<OrderItem>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Quantity).IsRequired();
                b.Property(x => x.UnitPrice).IsRequired();
            });
        }
    }
}
