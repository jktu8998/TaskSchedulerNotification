using System.Threading.Tasks;
using Domain.Entities;
using Domain.ValueObjects;

namespace  Domain.Interfaces;

public interface IPollingStateRepository
{
    Task<PollingState?> GetByTaskIdAsync(TaskId taskId);
    Task UpsertAsync(PollingState state);
}