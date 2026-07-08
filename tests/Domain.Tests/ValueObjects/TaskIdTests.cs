using System;
using Domain.ValueObjects;
using Xunit;

namespace Domain.Tests.ValueObjects;

public class TaskIdTests
{
    [Fact]
    public void New_GeneratesNonEmptyGuid()
    {
        var taskId = TaskId.New();
        Assert.NotEqual(Guid.Empty, taskId.Value);
    }

    [Fact]
    public void From_GivenGuid_ReturnsTaskIdWithThatGuid()
    {
        var guid = Guid.NewGuid();
        var taskId = TaskId.From(guid);
        Assert.Equal(guid, taskId.Value);
    }

    [Fact]
    public void TwoTaskIds_WithSameGuid_AreEqual()
    {
        var guid = Guid.NewGuid();
        var id1 = TaskId.From(guid);
        var id2 = TaskId.From(guid);
        Assert.Equal(id1, id2);
        Assert.True(id1 == id2);
    }

    [Fact]
    public void TwoTaskIds_WithDifferentGuids_AreNotEqual()
    {
        var id1 = TaskId.New();
        var id2 = TaskId.New();
        Assert.NotEqual(id1, id2);
        Assert.False(id1 == id2);
    }

    [Fact]
    public void ToString_ReturnsGuidString()
    {
        var guid = Guid.NewGuid();
        var taskId = TaskId.From(guid);
        Assert.Equal(guid.ToString(), taskId.ToString());
    }
}