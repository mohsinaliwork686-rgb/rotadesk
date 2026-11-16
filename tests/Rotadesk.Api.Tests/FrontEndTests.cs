using System.Net;

namespace Rotadesk.Api.Tests;

/// <summary>The API serves the built React app from the same origin, so the session cookie needs no CORS.</summary>
public sealed class FrontEndTests : IDisposable
{
    private const string Page = "<!doctype html><title>Rotadesk</title><div id=\"root\"></div>";

    private readonly ApiFixture _fixture;
    private readonly string _webRoot = Directory.CreateTempSubdirectory("rotadesk-web-").FullName;
    private readonly RotadeskFactory _factory;

    public FrontEndTests(ApiFixture fixture)
    {
        _fixture = fixture;
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), Page);
        File.WriteAllText(Path.Combine(_webRoot, "assets", "index-3f9a1c.js"), "console.log('rotadesk');");
        _factory = new RotadeskFactory(fixture.ConnectionString, demo: false, _webRoot);
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_webRoot, recursive: true);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/rota")]
    [InlineData("/rota/2026-11-16")]
    [InlineData("/availability")]
    public async Task A_page_of_the_app_gets_index_html_checked_on_every_load(string path)
    {
        var response = await _fixture.NewClient(_factory).GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Page, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(response.Headers.CacheControl?.NoCache);
    }

    [Fact]
    public async Task Hashed_assets_are_cached_for_good()
    {
        var response = await _fixture.NewClient(_factory).GetAsync("/assets/index-3f9a1c.js", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task A_missing_file_is_a_404_not_the_app()
    {
        var response = await _fixture.NewClient(_factory).GetAsync("/assets/gone-1234.js", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/nothing-here")]
    [InlineData("POST", "/api/weeks")]
    [InlineData("GET", "/api")]
    public async Task An_unknown_api_path_is_a_problem_404_not_the_app(string method, string path)
    {
        var response = await _fixture.NewClient(_factory).SendAsync(new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_api_still_answers_its_own_paths()
    {
        var response = await _fixture.NewClient(_factory).GetAsync("/api/auth/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
