using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using PerdeuAlgo;
using Claim = System.Security.Claims.Claim;

var builder = WebApplication.CreateBuilder(args);
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDirectory);
var protection = builder.Services.AddDataProtection().SetApplicationName("PerdeuAlgo")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));
if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
builder.Services.AddSingleton(sp => new Database(sp.GetRequiredService<IConfiguration>()["Database"] ?? $"Data Source={Path.Combine(dataDirectory, "perdeualgo.db")}"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Service>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "perdeualgo.session"; options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8); options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    try { await next(); }
    catch (RuleException ex) { context.Response.StatusCode = ex.Status; await context.Response.WriteAsJsonAsync(new { error = ex.Message }); }
    catch (AntiforgeryValidationException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "A sessão do formulário expirou. Recarregue a página." }); }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Erro ao processar a requisição");
        context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { error = "Não foi possível concluir a operação." });
    }
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
        await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
    await next();
});
static long UserId(HttpContext c) => long.Parse(c.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
static async Task SignIn(HttpContext c, UserView user)
{
    var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Name)], CookieAuthenticationDefaults.AuthenticationScheme);
    await c.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
}
app.MapGet("/api/session", (HttpContext c, IAntiforgery anti) => Results.Ok(new
{
    user = c.User.Identity?.IsAuthenticated == true ? new UserView(UserId(c), c.User.Identity.Name!) : null,
    csrfToken = anti.GetAndStoreTokens(c).RequestToken,
    categories = Rules.Categories
}));
app.MapPost("/api/register", async (RegisterInput input, Service service, HttpContext c) => { var user = service.Register(input); await SignIn(c, user); return Results.Ok(user); }).RequireRateLimiting("auth");
app.MapPost("/api/login", async (LoginInput input, Service service, HttpContext c) => { var user = service.Login(input); await SignIn(c, user); return Results.Ok(user); }).RequireRateLimiting("auth");
app.MapPost("/api/logout", async (HttpContext c) => { await c.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization();
app.MapGet("/api/items", (Service service) => service.Items());
app.MapPost("/api/items", (ItemInput input, Service service, HttpContext c) => Results.Ok(service.Create(UserId(c), input))).RequireAuthorization();
app.MapGet("/api/items/{id:long}/matches", (long id, Service service, HttpContext c) => service.Matches(UserId(c), id)).RequireAuthorization();
app.MapGet("/api/items/{id:long}/secret", (long id, Service service, HttpContext c) => Results.Ok(new { secret = service.Secret(UserId(c), id) })).RequireAuthorization();
app.MapPost("/api/items/{id:long}/claims", (long id, ClaimInput input, Service service, HttpContext c) => Results.Ok(new { id = service.Request(UserId(c), id, input) })).RequireAuthorization();
app.MapPost("/api/items/{id:long}/cancel", (long id, Service service, HttpContext c) => Results.Ok(new { message = service.Cancel(UserId(c), id) })).RequireAuthorization();
app.MapGet("/api/claims", (Service service, HttpContext c) => service.Claims(UserId(c))).RequireAuthorization();
app.MapPost("/api/claims/{id:long}/{action}", (long id, string action, Service service, HttpContext c) => Results.Ok(new { message = service.Decide(UserId(c), id, action) })).RequireAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
if (args.Contains("--seed"))
{
    var service = app.Services.GetRequiredService<Service>();
    if (service.Items().Count == 0)
    {
        var a = service.Register(new("Acervo de demonstração", "acervo@example.invalid", Guid.NewGuid().ToString()));
        var b = service.Register(new("Biblioteca de demonstração", "biblioteca@example.invalid", Guid.NewGuid().ToString()));
        var today = DateOnly.FromDateTime(DateTime.Now);
        service.Create(a.Id, new("found", "Um livro à espera do próximo capítulo", "Livros", "Biblioteca central", today.AddDays(-1), "Livro de capa verde encontrado na mesa de leitura. Está guardado na recepção da biblioteca.", "Há uma dedicatória manuscrita na primeira página."));
        service.Create(b.Id, new("lost", "Meu companheiro de leitura", "Livros", "Biblioteca central", today.AddDays(-2), "Perdi um livro de capa verde depois de estudar na biblioteca. Tem um valor sentimental enorme.", ""));
        service.Create(a.Id, new("found", "Chaves com um pequeno universo", "Acessórios", "Pátio das mangueiras", today, "Penca de chaves com chaveiro de planeta, recolhido perto dos bancos do pátio.", "Uma das chaves tem a cabeça pintada de azul."));
        service.Create(b.Id, new("lost", "Óculos de armação caramelo", "Acessórios", "Café do campus", today, "Óculos de grau de armação arredondada, possivelmente esquecidos no balcão do café.", ""));
    }
}
app.Run();
public partial class Program { }
