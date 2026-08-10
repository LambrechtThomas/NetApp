using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Rise.Client;
using Rise.Client.Identity;
using Rise.Client.Products;
using Rise.Shared.Products;
// test

try
{
    var builder = WebAssemblyHostBuilder.CreateDefault(args);

    builder.RootComponents.Add<App>("#app");
    builder.RootComponents.Add<HeadOutlet>("head::after");


    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.BrowserConsole(outputTemplate: "[{Timestamp:HH:mm:ss}{Level:u3}]{Message:lj} {NewLine}{Exception}")
        .CreateLogger();

    Log.Information("Starting web application");

    // register the cookie handler
    builder.Services.AddTransient<CookieHandler>();

    // set up authorization
    builder.Services.AddAuthorizationCore();

    // register the custom state provider
    builder.Services.AddScoped<AuthenticationStateProvider, CookieAuthenticationStateProvider>();
    // register the account management interface
    builder.Services.AddScoped(sp => (IAccountManager)sp.GetRequiredService<AuthenticationStateProvider>());

    // This is a hosted Blazor WASM app - Rise.Server serves both the API and
    // these WASM files, so the backend is always same-origin as wherever the
    // browser loaded this app from. HostEnvironment.BaseAddress reflects that
    // automatically (dev, local Vagrant, cloud - no per-environment config
    // needed). `BackendUrl`, if explicitly set, still overrides it for the
    // rare case the backend genuinely lives elsewhere.
    var backendUrl = builder.Configuration["BackendUrl"] ?? builder.HostEnvironment.BaseAddress;

    // configure client for auth interactions
    builder.Services.AddHttpClient("SecureApi", opt => opt.BaseAddress = new Uri(backendUrl))
        .AddHttpMessageHandler<CookieHandler>();

    builder.Services.AddHttpClient<IProductService, ProductService>(client =>
    {
        client.BaseAddress = new Uri(backendUrl);
    });

    await builder.Build().RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "An exception occurred while creating the WASM host");
    throw;
}
