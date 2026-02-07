using BistrosoftChallenge.Domain.Entities;

namespace BistrosoftChallenge.Infrastructure.Repositories
{
    public interface ICustomerRepository
    {
        Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    }
}
