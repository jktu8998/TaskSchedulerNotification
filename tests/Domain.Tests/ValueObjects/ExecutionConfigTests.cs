using System;
using System.Collections.Generic;
using Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Domain.Tests.ValueObjects;

public class ExecutionConfigTests
{
    [Fact]
    public void Constructor_ConvertsMethodToUppercase()
    {
        // Arrange & Act
        var config = new ExecutionConfig("get", "https://api.example.com");

        // Assert
        config.Method.Should().Be("GET");
    }

    [Fact]
    public void Constructor_WhenHeadersIsNull_InitializesEmptyDictionary()
    {
        // Arrange & Act
        var config = new ExecutionConfig("POST", "https://api.example.com", headers: null);

        // Assert
        config.Headers.Should().NotBeNull();
        config.Headers.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WhenBodyIsNull_SetsNull()
    {
        // Arrange & Act
        var config = new ExecutionConfig("DELETE", "https://api.example.com", body: null);

        // Assert
        config.Body.Should().BeNull();
    }

    [Fact]
    public void Constructor_WhenBodyIsProvided_SetsBody()
    {
        // Arrange & Act
        var body = "{\"key\":\"value\"}";
        var config = new ExecutionConfig("PUT", "https://api.example.com", body: body);

        // Assert
        config.Body.Should().Be(body);
    }

    [Fact]
    public void Constructor_WithHeaders_PreservesHeadersCase()
    {
        // Arrange & Act
        var headers = new Dictionary<string, string> { { "X-Custom", "value" } };
        var config = new ExecutionConfig("GET", "https://api.example.com", headers);

        // Assert
        config.Headers.Should().ContainKey("X-Custom");
    }
}