using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests;

public sealed class HealthEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetHealth_ReturnsOkStatus()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(payload);
        Assert.Equal("ok", payload.Status);
    }

    private sealed record HealthResponse(string Status);
}
