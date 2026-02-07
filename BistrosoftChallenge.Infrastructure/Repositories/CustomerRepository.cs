using Microsoft.EntityFrameworkCore;
using BistrosoftChallenge.Domain.Entities;

namespace BistrosoftChallenge.Infrastructure.Repositories
{
    public class CustomerRepository : ICustomerRepository
    {
        private readonly AppDbContext _db;

        public CustomerRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            return await _db.Customers.Include(c => c.Orders).FirstOrDefaultAsync(c => c.Id == id, ct);
        }
        public async Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
        {
            return await _db.Customers.AnyAsync(c => c.Email == email, ct);
        }
    }
}
