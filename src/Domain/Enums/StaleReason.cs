namespace Domain.Enums;

/// <summary>
/// Причина, по которой задание считается "протухшим" (stale) — зависшим и требующим восстановления.
/// </summary>
public enum StaleReason
{
    /// <summary>Задание не протухло, состояние актуально.</summary>
    NotStale = 0,

    /// <summary>Задание выполнялось, но блокировка (LockedUntil) истекла.
    /// Воркер вероятно упал.
    /// </summary>
    LockExpired = 1

    // В будущем можно добавить, например:
    // HeartbeatLost = 2,
    // ExternalDependencyTimeout = 3
}