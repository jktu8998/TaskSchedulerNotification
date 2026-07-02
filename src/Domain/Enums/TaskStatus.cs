namespace Domain.Enums;

public enum TaskStatus
{
    Created = 0,
    Scheduled = 1,
    Queued = 2,
    Executing = 3,
    Completed = 4,
    Failed = 5,
    Dead = 6,
    Paused = 7,
    Cancelled = 8
}