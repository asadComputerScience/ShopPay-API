using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShopPay.Api.Configuration;
using ShopPay.Api.Data;
using ShopPay.Api.Infrastructure;
using ShopPay.Api.Payments;
using ShopPay.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration -------------------------------------------------------------------------
// "Stripe" section: placeholders live in appsettings.json, the REAL test keys come from
// user-secrets (Development) or environment variables (Stripe__SecretKey, Stripe__WebhookSecret).
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection(StripeSettings.SectionName));

// ---- Services ------------------------------------------------------------------------------
builder.Services.AddDbContext<ShopPayDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ShopPay") ?? "Data Source=shoppay.db"));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<StripeWebhookHandler>();

builder.Services.AddControllers();

// Consistent JSON error bodies (RFC 7807) for validation errors, exceptions and plain 404s.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Show the /// XML comments from the controllers in the Swagger UI.
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

// ---- Database ------------------------------------------------------------------------------
// Creates shoppay.db and inserts the sample products on first run. (A production app would use
// EF Core migrations instead of EnsureCreated.)
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<ShopPayDbContext>().Database.EnsureCreated();
}

var stripeSettings = app.Services.GetRequiredService<IOptions<StripeSettings>>().Value;
if (!stripeSettings.HasSecretKey || !stripeSettings.HasWebhookSecret)
{
    app.Logger.LogWarning(
        "Stripe keys are not configured yet. GET /products works, but POST /checkout and the webhook will " +
        "answer 503 until you run `dotnet user-secrets set` for Stripe:SecretKey and Stripe:WebhookSecret (see README).");
}

// ---- HTTP pipeline -------------------------------------------------------------------------
app.UseExceptionHandler();   // must come first so it can catch errors from everything below
app.UseStatusCodePages();    // turns empty 404/405/... responses into problem-details JSON

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
}

// No UseHttpsRedirection() on purpose: the Stripe CLI forwards webhooks to plain http://localhost,
// and a redirect would make those POST requests fail.

app.MapControllers();

app.Run();
