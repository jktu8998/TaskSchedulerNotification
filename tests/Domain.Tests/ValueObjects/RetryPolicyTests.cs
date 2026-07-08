using System;
using System.Collections.Generic;
using Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Domain.Tests.ValueObjects;

public class RetryPolicyTests
{
    [Fact]
    public void MaxAttempts_ReturnsLengthOfIntervals()
    {
        // Arrange
        var intervals = new[] { 10, 20, 30 };
        var policy = new RetryPolicy(intervals);

        // Act & Assert
        policy.MaxAttempts.Should().Be(3);
    }

    [Fact]
    public void Default_ContainsExactlyFiveAttemptsOf60Seconds()
    {
        // Arrange & Act
        var defaultPolicy = RetryPolicy.Default;

        // Assert
        defaultPolicy.MaxAttempts.Should().Be(5);
        defaultPolicy.IntervalsSeconds.Should().BeEquivalentTo(new[] { 60, 60, 60, 60, 60 });
    }

    [Fact]
    public void Constructor_WithEmptyList_MaxAttemptsIsZero()
    {
        // Arrange & Act
        var policy = new RetryPolicy(Array.Empty<int>());

        // Assert
        policy.MaxAttempts.Should().Be(0);
    }
}