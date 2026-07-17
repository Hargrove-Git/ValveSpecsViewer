using Microsoft.EntityFrameworkCore;
using ValveSpecsViewer.Data;
using ValveSpecsViewer.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<ValveSpecsDb>(opts =>
    opts.UseSqlServer(builder.Configuration.GetConnectionString("ValveSpecs")));

builder.Services.AddScoped<ValveSpecsService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();
