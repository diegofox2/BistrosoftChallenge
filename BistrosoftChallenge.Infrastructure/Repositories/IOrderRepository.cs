using BistrosoftChallenge.Domain.Entities;

namespace BistrosoftChallenge.Infrastructure.Repositories
{
    public interface IOrderRepository
    {
        Task<IReadOnlyList<Order>> GetByCustomerAsync(Guid customerId, CancellationToken ct = default);
    }
}
