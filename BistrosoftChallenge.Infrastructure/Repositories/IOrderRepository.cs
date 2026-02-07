using BistrosoftChallenge.Domain.Entities;

namespace BistrosoftChallenge.Infrastructure.Repositories
{
    public interface IOrderRepository
    {
        Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<Order>> GetByCustomerAsync(Guid customerId, CancellationToken ct = default);
    }
}
