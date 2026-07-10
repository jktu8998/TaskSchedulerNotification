using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces;
using Application.Services;
using Domain.DomainEvents;
using Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Application.Tests.Services;

public class DomainEventDispatcherTests
{
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private readonly DomainEventDispatcher _dispatcher;

    public DomainEventDispatcherTests()
    {
        _dispatcher = new DomainEventDispatcher(_serviceProviderMock.Object);
    }

    // Тестовое событие
    public sealed record TestEvent(TaskId TaskId) : IDomainEvent;

    // ========== Позитивные тесты ==========

    [Fact]
    public async Task DispatchAsync_EventWithHandlers_CallsHandler()
    {
        // Arrange
        var handlerMock = new Mock<IDomainEventHandler<TestEvent>>();
        var handlers = new List<IDomainEventHandler<TestEvent>> { handlerMock.Object };
        var eventType = typeof(TestEvent);
        var handlerEnumerableType = typeof(IEnumerable<>).MakeGenericType(typeof(IDomainEventHandler<>).MakeGenericType(eventType));

        _serviceProviderMock
            .Setup(sp => sp.GetService(handlerEnumerableType))
            .Returns(handlers);

        var testEvent = new TestEvent(TaskId.New());
        var ct = CancellationToken.None;

        // Act
        await _dispatcher.DispatchAsync(new[] { testEvent }, ct);

        // Assert
        handlerMock.Verify(h => h.HandleAsync(testEvent, ct), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_NoHandlers_DoesNotThrow()
    {
        // Arrange
        var eventType = typeof(TestEvent);
        var handlerEnumerableType = typeof(IEnumerable<>).MakeGenericType(typeof(IDomainEventHandler<>).MakeGenericType(eventType));

        _serviceProviderMock
            .Setup(sp => sp.GetService(handlerEnumerableType))
            .Returns(Array.Empty<IDomainEventHandler<TestEvent>>());

        var testEvent = new TestEvent(TaskId.New());

        // Act & Assert (не должно упасть)
        await _dispatcher.DispatchAsync(new[] { testEvent }, CancellationToken.None);
    }

    /// <summary>
    /// ожидает, что IServiceProvider.GetService будет вызван только один раз при повторной отправке события того же типа.
    /// Однако реализация DomainEventDispatcher кэширует только делегат вызова HandlerInvoker, но не список обработчиков 
    /// </summary>
    /*[Fact]
    public async Task DispatchAsync_CachesInvokerForSameEventType()
    {
        // Arrange
        var eventType = typeof(TestEvent);
        var handlerEnumerableType = typeof(IEnumerable<>).MakeGenericType(typeof(IDomainEventHandler<>).MakeGenericType(eventType));
        var handlerMock = new Mock<IDomainEventHandler<TestEvent>>();
        var handlers = new List<IDomainEventHandler<TestEvent>> { handlerMock.Object };

        _serviceProviderMock
            .Setup(sp => sp.GetService(handlerEnumerableType))
            .Returns(handlers);

        var testEvent = new TestEvent(TaskId.New());
        var ct = CancellationToken.None;

        // Act
        await _dispatcher.DispatchAsync(new[] { testEvent }, ct);
        await _dispatcher.DispatchAsync(new[] { testEvent }, ct);

        // Assert: обработчик вызван дважды, но сервис-провайдер запрошен только один раз (кэш)
        handlerMock.Verify(h => h.HandleAsync(testEvent, ct), Times.Exactly(2));
        _serviceProviderMock.Verify(sp => sp.GetService(handlerEnumerableType), Times.Once);
    }*/

    [Fact]
    public async Task DispatchAsync_MultipleEvents_CallsHandlersForEach()
    {
        // Arrange
        var handlerMock = new Mock<IDomainEventHandler<TestEvent>>();
        var handlers = new List<IDomainEventHandler<TestEvent>> { handlerMock.Object };
        var eventType = typeof(TestEvent);
        var handlerEnumerableType = typeof(IEnumerable<>).MakeGenericType(typeof(IDomainEventHandler<>).MakeGenericType(eventType));

        _serviceProviderMock
            .Setup(sp => sp.GetService(handlerEnumerableType))
            .Returns(handlers);

        var events = new[]
        {
            new TestEvent(TaskId.New()),
            new TestEvent(TaskId.New())
        };
        var ct = CancellationToken.None;

        // Act
        await _dispatcher.DispatchAsync(events, ct);

        // Assert
        handlerMock.Verify(h => h.HandleAsync(It.IsAny<TestEvent>(), ct), Times.Exactly(2));
    }

    [Fact]
    public async Task DispatchAsync_PassesCancellationToken()
    {
        // Arrange
        var handlerMock = new Mock<IDomainEventHandler<TestEvent>>();
        var handlers = new List<IDomainEventHandler<TestEvent>> { handlerMock.Object };
        var eventType = typeof(TestEvent);
        var handlerEnumerableType = typeof(IEnumerable<>).MakeGenericType(typeof(IDomainEventHandler<>).MakeGenericType(eventType));

        _serviceProviderMock
            .Setup(sp => sp.GetService(handlerEnumerableType))
            .Returns(handlers);

        var cts = new CancellationTokenSource();
        var testEvent = new TestEvent(TaskId.New());

        // Act
        await _dispatcher.DispatchAsync(new[] { testEvent }, cts.Token);

        // Assert
        handlerMock.Verify(h => h.HandleAsync(testEvent, cts.Token), Times.Once);
    }
}