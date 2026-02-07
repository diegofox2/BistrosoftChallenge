using Microsoft.EntityFrameworkCore;
using BistrosoftChallenge.Domain.Entities;

namespace BistrosoftChallenge.Infrastructure.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly AppDbContext _db;

        public ProductRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            return await _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        }

        // No write operations: product updates happen via sagas directly with DbContext.
    }
}
