using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using ValveSpecsViewer.Data;
using ValveSpecsViewer.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<ValveSpecsDb>(opts =>
    opts.UseSqlServer(builder.Configuration.GetConnectionString("ValveSpecs")));

builder.Services.AddScoped<ValveSpecsService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<GrammarService>();
builder.Services.AddScoped<SpecValueService>();
builder.Services.AddScoped<ResolverService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/login";
        o.ExpireTimeSpan = TimeSpan.FromDays(14);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapPost("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapGet("/documents/{id:int}/download", async (int id, ValveSpecsDb db) =>
{
    var doc = await db.DocumentFiles.FindAsync(id);
    if (doc?.FileData is null) return Results.NotFound();

    return Results.File(doc.FileData, doc.FileMimeType, doc.FileName ?? $"document-{id}");
}).RequireAuthorization();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();
