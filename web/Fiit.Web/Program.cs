using Fiit.Web;
using Fiit.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// ApiBase пустой — API на том же origin (nginx проксирует /api); в разработке — http://localhost:5080/
var apiBase = builder.Configuration["ApiBase"];
var baseAddress = new Uri(string.IsNullOrEmpty(apiBase) ? builder.HostEnvironment.BaseAddress : apiBase);
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = baseAddress });
builder.Services.AddScoped<Session>();
builder.Services.AddScoped<Api>();
builder.Services.AddScoped<BotApi>();
builder.Services.AddScoped<BotEmulator>();
builder.Services.AddScoped<Prefs>();

var host = builder.Build();
await host.Services.GetRequiredService<Session>().LoadAsync();
await host.RunAsync();
