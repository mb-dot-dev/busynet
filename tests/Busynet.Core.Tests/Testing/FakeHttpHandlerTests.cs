using System.Net;
using System.Text;
using Busynet.Testing;

namespace Busynet.Core.Tests.Testing;

public sealed class FakeHttpHandlerTests
{
    [Fact]
    public async Task Unregistered_route_fails_the_test()
    {
        using var client = new HttpClient(new FakeHttpHandler());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync("http://bar/api/nope", TestContext.Current.CancellationToken));

        Assert.Contains("GET", ex.Message);
        Assert.Contains("/api/nope", ex.Message);
    }

    [Fact]
    public async Task Records_body_and_headers()
    {
        var fake = new FakeHttpHandler();
        fake.Add(HttpMethod.Post, "/api/x", HttpStatusCode.OK, "{}");
        using var client = new HttpClient(fake);

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://h/api/x?q=1")
        {
            Content = new StringContent("{\"a\":1}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-API-Token", "t");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        var call = Assert.Single(fake.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("{\"a\":1}", call.Body);
        Assert.Equal("t", call.Headers["X-API-Token"]);
        Assert.StartsWith("application/json", call.Headers["Content-Type"]);
        Assert.Equal("{}", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Sequential_registrations_respond_in_order()
    {
        var fake = new FakeHttpHandler();
        fake.Add(HttpMethod.Get, "/s", HttpStatusCode.OK, "1");
        fake.Add(HttpMethod.Get, "/s", HttpStatusCode.OK, "2");
        using var client = new HttpClient(fake);

        var bodies = new List<string>();
        for (var i = 0; i < 3; i++)
            bodies.Add(await client.GetStringAsync("http://h/s", TestContext.Current.CancellationToken));

        Assert.Equal(["1", "2", "2"], bodies);
    }
}
