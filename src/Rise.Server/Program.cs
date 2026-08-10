using Destructurama;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Rise.Persistence;
using Rise.Persistence.Triggers;
using Rise.Server.Identity;
using Rise.Server.Processors;
using Rise.Services;
using Rise.Services.Identity;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger(); // Initial log setup, will be overwritten by Serilog, but we need a logger before Dependency Injection is activated.

try
{
    Log.Information("Starting web application");
    var builder = WebApplication.CreateBuilder(args);

    builder.Services
        .AddSerilog((_, lc) => lc.ReadFrom.Configuration(builder.Configuration) // Configuration in AppSettings.json
            .Destructure.UsingAttributes()) // Sensitive data logging
        .AddIdentity<IdentityUser, IdentityRole>()
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .Services.AddDbContext<ApplicationDbContext>(o =>
        {
            var connectionString = builder.Configuration.GetConnectionString("DatabaseConnection") ??
                                   throw new InvalidOperationException("Connection string 'DatabaseConnection' not found.");
            if (builder.Environment.IsDevelopment())
            {
                o.UseSqlite(connectionString); // Local dev only - zero-setup file database.
            }
            else
            {
                o.UseSqlServer(connectionString); // Deployed environments (local Vagrant appserver, cloud) target SQL Server.
            }
            o.EnableDetailedErrors();
            if (builder.Environment.IsDevelopment())
            {
                o.EnableSensitiveDataLogging(); // only enabled in development.
            }
            o.UseTriggers(options => options.AddTrigger<EntityBeforeSaveTrigger>()); // Handles all UpdatedAt, CreatedAt stuff.
        })
        .ConfigureApplicationCookie(o =>
        {
            o.Events.OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };

            o.Events.OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        })
        .AddHttpContextAccessor()
        .AddScoped<ISessionContextProvider, HttpContextSessionProvider>() // Provides the current user from the HttpContext to the session provider.
        .AddApplicationServices() // You'll need to add your own services in this function call.
        .AddAuthorization()
        .AddFastEndpoints(o =>
        {
            o.IncludeAbstractValidators = true; // Include validators from abstract classes (see https://docs.fluentvalidation.net/en/latest/).
            o.Assemblies = [typeof(Rise.Shared.Products.ProductRequest).Assembly]; // Adds the validators from other assemblies
        })
        .SwaggerDocument(o =>
        {
            o.DocumentSettings = s =>
            {
                s.Title = "RISE API";
            };
        });

    var app = builder.Build();
    // apply Database migraticons on startup, not so wise in production (Use Generated SQL Scripts)
    // See: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying?tabs=dotnet-core-cli
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dbSeeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
        // dbContext.Database.EnsureDeleted(); // Delete the database if it exists to clean it up if needed.

        if (app.Environment.IsDevelopment())
        {
            dbContext.Database.EnsureCreated(); // SQLite has no versioned migration set - just create the schema from the current model.
        }
        else
        {
            dbContext.Database.Migrate(); // SQL Server - applies the versioned migrations. Creates the database if it doesn't exist.
        }
        await dbSeeder.SeedAsync(); // Seeds the database with some test data. Idempotent - safe to run on every startup.
    }
    // Theses middlewares are strict in order of calling!
    if (!app.Environment.IsDevelopment())
    {
        // Deployed environments sit behind nginx, which terminates TLS and
        // proxies plain HTTP to Kestrel on loopback. Without this, Kestrel
        // sees every request as HTTP and UseHttpsRedirection() below would
        // redirect right back to itself. nginx runs on this same host, so
        // trusting its X-Forwarded-* headers unconditionally is safe here.
        var forwardedHeadersOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };
        forwardedHeadersOptions.KnownNetworks.Clear();
        forwardedHeadersOptions.KnownProxies.Clear();
        app.UseForwardedHeaders(forwardedHeadersOptions);
    }

    app.UseHttpsRedirection()
        .UseBlazorFrameworkFiles() // Blazor is also served from the API. 
        .UseStaticFiles()
        .UseDefaultExceptionHandler()
        .UseAuthentication()
        .UseAuthorization()
        .UseFastEndpoints(o =>
        {
            o.Endpoints.Configurator = ep =>
            {
                ep.DontAutoSendResponse();
                ep.PreProcessor<GlobalRequestLogger>(Order.Before);
                ep.PostProcessor<GlobalResponseSender>(Order.Before);
                ep.PostProcessor<GlobalResponseLogger>(Order.Before);
            };
        })
        .UseSwaggerGen();
    app.MapFallbackToFile("index.html"); // Serves the Blazor app from the API, when no routes match.
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "An unhandled exception occured during bootstrapping");
}
finally
{
    Log.CloseAndFlush();
}


