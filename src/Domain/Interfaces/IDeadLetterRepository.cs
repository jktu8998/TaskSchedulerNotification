using System.Collections.Generic;
using System.Threading.Tasks;
using  Domain.Entities;

namespace  Domain.Interfaces;

public interface IDeadLetterRepository
{
    Task AddAsync(DeadLetterEntry entry);
    Task<DeadLetterEntry?> GetByIdAsync(long id);
    Task<IReadOnlyList<DeadLetterEntry>> GetAllAsync(int skip, int take);
    Task RemoveAsync(long id);
}