using Microsoft.EntityFrameworkCore;
using BistrosoftChallenge.Domain.Entities;

namespace BistrosoftChallenge.Infrastructure.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly AppDbContext _db;

        public OrderRepository(AppDbContext db)
        {
            _db = db;
        }
        public async Task<IReadOnlyList<Order>> GetByCustomerAsync(Guid customerId, CancellationToken ct = default)
        {
            return await _db.Orders.Where(o => o.CustomerId == customerId).Include(o => o.OrderItems).ToListAsync(ct);
        }
    }
}
