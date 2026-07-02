using System.Collections.Generic;
using System.Threading.Tasks;
using Domain.Entities;
using Domain.ValueObjects;

namespace Domain.Interfaces;

public interface ITaskLogRepository
{
    Task AddAsync(TaskLog log);
    Task<IReadOnlyList<TaskLog>> GetByTaskIdAsync(TaskId taskId);
}