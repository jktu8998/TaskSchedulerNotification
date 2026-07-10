using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Application.Dto;
using Application.Handlers;
using Application.Interfaces;
using Application.Queries;
using Domain.Entities;
using Domain.Interfaces;
using Domain.ValueObjects;
using Moq;
using Xunit;

namespace Application.Tests.Handlers;

public class GetDlqEntriesQueryHandlerTests
{
    private readonly Mock<IDeadLetterRepository> _dlqRepoMock = new();
    private readonly Mock<IRequestContext> _requestContextMock = new();
    private readonly GetDlqEntriesQueryHandler _handler;
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly DateTime _utcNow = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    public GetDlqEntriesQueryHandlerTests()
    {
        _handler = new GetDlqEntriesQueryHandler(_dlqRepoMock.Object, _requestContextMock.Object);
    }

    private static DeadLetterEntry CreateEntry(long id, Guid taskId, string senderId, string snapshot = "{}", string? error = null)
    {
        var entry = new DeadLetterEntry(TaskId.From(taskId), senderId, snapshot, error, DateTime.UtcNow);
        typeof(DeadLetterEntry).GetProperty("Id")?.SetValue(entry, id);
        return entry;
    }

    [Fact]
    public async Task HandleAsync_AsRegularUser_ReturnsOnlyOwnEntries()
    {
        // Arrange
        _requestContextMock.Setup(r => r.IsAdmin).Returns(false);
        _requestContextMock.Setup(r => r.SenderId).Returns("user-1");

        var entries = new List<DeadLetterEntry>
        {
            CreateEntry(1, Guid.NewGuid(), "user-1"),
            CreateEntry(2, Guid.NewGuid(), "user-1")
        };
        _dlqRepoMock.Setup(r => r.GetBySenderIdAsync("user-1", 0, 10, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(entries);

        var query = new GetDlqEntriesQuery(0, 10);

        // Act
        var result = await _handler.HandleAsync(query, _ct);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, dto => Assert.Equal("user-1", dto.SenderId));
        _dlqRepoMock.Verify(r => r.GetAllAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_AsAdmin_ReturnsAllEntries()
    {
        // Arrange
        _requestContextMock.Setup(r => r.IsAdmin).Returns(true);
        _requestContextMock.Setup(r => r.SenderId).Returns("admin"); // не важно

        var entries = new List<DeadLetterEntry>
        {
            CreateEntry(1, Guid.NewGuid(), "sender-a"),
            CreateEntry(2, Guid.NewGuid(), "sender-b")
        };
        _dlqRepoMock.Setup(r => r.GetAllAsync(0, 10, It.Is<CancellationToken>(ct => ct == _ct)))
            .ReturnsAsync(entries);

        var query = new GetDlqEntriesQuery(0, 10);

        // Act
        var result = await _handler.HandleAsync(query, _ct);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, dto => dto.SenderId == "sender-a");
        Assert.Contains(result, dto => dto.SenderId == "sender-b");
        _dlqRepoMock.Verify(r => r.GetBySenderIdAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MapsAllFieldsCorrectly()
    {
        // Arrange
        _requestContextMock.Setup(r => r.IsAdmin).Returns(false);
        _requestContextMock.Setup(r => r.SenderId).Returns("user-1");

        var taskId = Guid.NewGuid();
        var movedAt = new DateTime(2026, 7, 10, 15, 30, 0, DateTimeKind.Utc);
        var entry = new DeadLetterEntry(
            TaskId.From(taskId),
            "user-1",
            "{\"original\":\"snapshot\"}",
            "Timeout error",
            movedAt);
        typeof(DeadLetterEntry).GetProperty("Id")?.SetValue(entry, 100L);

        _dlqRepoMock.Setup(r => r.GetBySenderIdAsync("user-1", 0, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { entry });

        var query = new GetDlqEntriesQuery(0, 5);

        // Act
        var result = await _handler.HandleAsync(query, _ct);

        // Assert
        var dto = Assert.Single(result);
        Assert.Equal(100L, dto.Id);
        Assert.Equal(taskId, dto.TaskId);
        Assert.Equal("user-1", dto.SenderId);
        Assert.Equal("{\"original\":\"snapshot\"}", dto.OriginalTaskSnapshot);
        Assert.Equal("Timeout error", dto.ErrorDetails);
        Assert.Equal(new DateTimeOffset(movedAt, TimeSpan.Zero), dto.MovedAt);
    }

    [Fact]
    public async Task HandleAsync_Pagination_PassedCorrectly()
    {
        // Arrange
        _requestContextMock.Setup(r => r.IsAdmin).Returns(false);
        _requestContextMock.Setup(r => r.SenderId).Returns("user-1");
        _dlqRepoMock.Setup(r => r.GetBySenderIdAsync("user-1", 10, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeadLetterEntry>());

        var query = new GetDlqEntriesQuery(10, 20);

        // Act
        await _handler.HandleAsync(query, _ct);

        // Assert
        _dlqRepoMock.Verify(r => r.GetBySenderIdAsync("user-1", 10, 20, It.IsAny<CancellationToken>()), Times.Once);
    }
}