using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace PerdeuAlgo.Tests;

public sealed class AppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Database"] = "Data Source=:memory:" }));
}
public class ApiTests
{
    private static async Task Token(HttpClient client)
    {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/session");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
    }
    private static async Task Register(HttpClient client, string email)
    {
        await Token(client);
        var response = await client.PostAsJsonAsync("/api/register", new { name = "Pessoa Teste", email, password = "SenhaDeTeste123!" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); await Token(client);
    }
    private static async Task<long> Create(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/items", new { kind = "found", title = "Livro verde", category = "Livros", location = "Biblioteca", date = DateOnly.FromDateTime(DateTime.Now), description = "Livro verde encontrado sobre a mesa.", secret = "Dedicatória privada para Ana" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
    }
    [Fact] public async Task Public_home_has_brand_and_security_headers()
    {
        using var factory = new AppFactory(); using var client = factory.CreateClient();
        var result = await client.GetAsync("/"); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("Perdeu Algo?", await result.Content.ReadAsStringAsync()); Assert.True(result.Headers.Contains("Content-Security-Policy"));
    }
    [Fact] public async Task Missing_csrf_is_rejected()
    {
        using var factory = new AppFactory(); using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/register", new { name = "Pessoa", email = "person@example.org", password = "SenhaDeTeste123!" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact] public async Task Anonymous_cannot_create()
    {
        using var factory = new AppFactory(); using var client = factory.CreateClient(); await Token(client);
        var response = await client.PostAsJsonAsync("/api/items", new { }); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [Fact] public async Task Secrets_and_emails_never_leak_on_public_list()
    {
        using var factory = new AppFactory(); using var owner = factory.CreateClient(); await Register(owner, "private@example.org"); await Create(owner);
        using var guest = factory.CreateClient(); var json = await guest.GetStringAsync("/api/items");
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("Dedicatória", json); Assert.DoesNotContain("private@example.org", json);
    }
    [Fact] public async Task Full_two_user_return_flow_and_logout()
    {
        using var factory = new AppFactory(); using var owner = factory.CreateClient(); using var claimant = factory.CreateClient();
        await Register(owner, "owner@example.org"); await Register(claimant, "claimant@example.org"); var id = await Create(owner);
        Assert.Equal(HttpStatusCode.Forbidden, (await claimant.GetAsync($"/api/items/{id}/secret")).StatusCode);
        var claimResponse = await claimant.PostAsJsonAsync($"/api/items/{id}/claims", new { proof = "Tem uma dedicatória para Ana." }); Assert.Equal(HttpStatusCode.OK, claimResponse.StatusCode);
        var claimId = (await claimResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Forbidden, (await claimant.PostAsync($"/api/claims/{claimId}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/claims/{claimId}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/claims/{claimId}/deliver", null)).StatusCode);
        var items = await owner.GetFromJsonAsync<JsonElement>("/api/items"); Assert.Equal("returned", items[0].GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync("/api/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/api/claims")).StatusCode);
    }
    [Fact] public async Task Claims_are_private_to_participants()
    {
        using var factory = new AppFactory(); using var owner = factory.CreateClient(); using var claimant = factory.CreateClient(); using var third = factory.CreateClient();
        await Register(owner, "owner@example.org"); await Register(claimant, "claimant@example.org"); await Register(third, "third@example.org");
        var id = await Create(owner); await claimant.PostAsJsonAsync($"/api/items/{id}/claims", new { proof = "Um detalhe muito privado." });
        Assert.Empty((await third.GetFromJsonAsync<JsonElement>("/api/claims")).EnumerateArray());
    }
}
