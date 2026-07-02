using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using  Domain.Entities;
using  Domain.Enums;
using  Domain.ValueObjects;
using TaskStatus = Domain.Enums.TaskStatus;

namespace  Domain.Interfaces;

public interface ITaskRepository
{
    Task AddAsync(ScheduledTask task);
    Task<ScheduledTask?> GetByIdAsync(TaskId id);
    Task<IReadOnlyList<ScheduledTask>> GetBySenderIdAsync(string senderId, int skip, int take, TaskStatus? status = null, TaskType? type = null);
    Task<IReadOnlyList<ScheduledTask>> GetScheduledBeforeAsync(DateTime cutoff);
    Task<IReadOnlyList<ScheduledTask>> GetExecutingOlderThanAsync(TimeSpan timeout);
    Task UpdateAsync(ScheduledTask task);
    Task<ScheduledTask?> AcquireNextQueuedAsync(); // для Executor (SELECT FOR UPDATE SKIP LOCKED)
}