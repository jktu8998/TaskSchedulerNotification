using System;
   using System.Collections.Generic;
   using System.Threading;
   using System.Threading.Tasks;
   using Application.Commands;
   using Application.Dto;
   using Application.Interfaces;
   using Domain.Entities;
   using Domain.Enums;
   using Domain.Interfaces;
   using Domain.ValueObjects;
   
   namespace Application.Handlers;
   
   /// <summary>
   /// Обработчик команды создания нового задания.
   /// Отвечает за маппинг DTO в доменную модель, сохранение и диспетчеризацию событий.
   /// </summary>
   public sealed class CreateTaskCommandHandler : ICommandHandler<CreateTaskCommand, Guid>
   {
       private readonly ITaskRepository _taskRepo;
       private readonly IEncryptionService _encryption;
       private readonly IDateTimeProvider _dateTime;
       private readonly IRequestContext _requestContext;
       private readonly IUnitOfWork _unitOfWork;
       private readonly IDomainEventDispatcher _dispatcher;
   
       public CreateTaskCommandHandler(
           ITaskRepository taskRepo,
           IEncryptionService encryption,
           IDateTimeProvider dateTime,
           IRequestContext requestContext,
           IUnitOfWork unitOfWork,
           IDomainEventDispatcher dispatcher)
       {
           _taskRepo = taskRepo;
           _encryption = encryption;
           _dateTime = dateTime;
           _requestContext = requestContext;
           _unitOfWork = unitOfWork;
           _dispatcher = dispatcher;
       }
   
       public async Task<Guid> HandleAsync(CreateTaskCommand command, CancellationToken cancellationToken = default)
       {
           var req = command.Request;
           var utcNow = _dateTime.UtcNow;
   
           // 1. Маппинг Schedule (валидация взаимоисключающих полей)
           var schedule = MapSchedule(req.Schedule);
   
           // 2. Маппинг ExecutionConfig
           var execution = new ExecutionConfig(
               req.Execution.Method,
               req.Execution.Url,
               req.Execution.Headers,
               req.Execution.Body);
   
           // 3. Маппинг ResultDeliveryConfig (опционально)
           ResultDeliveryConfig? resultDelivery = null;
           if (req.ResultDelivery != null)
           {
               var mode = Enum.Parse<ResultDeliveryMode>(req.ResultDelivery.Mode, ignoreCase: true);
               resultDelivery = new ResultDeliveryConfig(
                   mode,
                   req.ResultDelivery.Url,
                   req.ResultDelivery.Method,
                   req.ResultDelivery.Params);
           }
   
           // 4. Маппинг PollingConfig (опционально)
           PollingConfig? pollingConfig = null;
           if (req.PollingConfig != null)
           {
               pollingConfig = new PollingConfig(
                   req.PollingConfig.Field,
                   req.PollingConfig.Condition,
                   req.PollingConfig.Value,
                   req.PollingConfig.IntervalSeconds,
                   req.PollingConfig.VerboseLogging);
           }
   
           // 5. Маппинг RetryPolicy
           RetryPolicy retryPolicy;
           if (req.Retry?.IntervalsSeconds is { Length: > 0 } intervals)
               retryPolicy = new RetryPolicy(intervals);
           else
               retryPolicy = RetryPolicy.Default;
   
           // 6. Шифрование sensitive data
           string? encrypted = null;
           if (!string.IsNullOrWhiteSpace(req.SensitiveData))
               encrypted = _encryption.Encrypt(req.SensitiveData);
   
           // 7. Создание агрегата
           var task = new ScheduledTask(
               TaskId.New(),
               _requestContext.SenderId,
               Enum.Parse<TaskType>(req.Type, ignoreCase: true),
               schedule,
               execution,
               resultDelivery,
               pollingConfig,
               retryPolicy,
               encrypted,
               utcNow);
   
           // 8. Переход Created -> Scheduled
           task.ScheduleTask(utcNow);
   
           // 9. Сохранение в транзакции и диспетчеризация событий
           await _unitOfWork.BeginTransactionAsync(cancellationToken);
           try
           {
               await _taskRepo.AddAsync(task);
               await _dispatcher.DispatchAsync(task.DomainEvents, cancellationToken);
               await _unitOfWork.CommitAsync(cancellationToken);
           }
           catch
           {
               await _unitOfWork.RollbackAsync(cancellationToken);
               throw;
           }
   
           task.ClearDomainEvents();
           return task.Id.Value;
       }
   
       private static Schedule MapSchedule(ScheduleDto dto)
       {
           if (!string.IsNullOrWhiteSpace(dto.ExecuteAt))
               return Schedule.FromAbsolute(DateTimeOffset.Parse(dto.ExecuteAt));
           if (!string.IsNullOrWhiteSpace(dto.Offset))
               return Schedule.FromOffset(ParseOffset(dto.Offset));
           if (!string.IsNullOrWhiteSpace(dto.Cron))
               return Schedule.FromCron(dto.Cron, dto.Timezone ?? "UTC");
           throw new ArgumentException("Одно из полей ExecuteAt, Offset или Cron должно быть заполнено.");
       }
   
       private static TimeSpan ParseOffset(string offset)
       {
           var unit = offset[^1];
           var value = int.Parse(offset[..^1]);
           return unit switch
           {
               's' => TimeSpan.FromSeconds(value),
               'm' => TimeSpan.FromMinutes(value),
               'h' => TimeSpan.FromHours(value),
               'd' => TimeSpan.FromDays(value),
               _ => throw new ArgumentException($"Неизвестная единица смещения: {unit}")
           };
       }
   }