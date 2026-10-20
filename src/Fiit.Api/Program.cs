using System.Security.Claims;
using System.Text.Json.Serialization;
using Fiit.Api;
using Fiit.Api.Auth;
using Fiit.Api.Data;
using Fiit.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o => o
    .UseNpgsql(builder.Configuration.GetConnectionString("Db"))
    .UseSnakeCaseNamingConvention());

var jwt = builder.Configuration.GetRequiredSection(JwtOptions.Section).Get<JwtOptions>()!;
builder.Services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.Section)
    .Validate(o => o.Key.Length >= 32, "Jwt:Key must be at least 32 chars").ValidateOnStart();
builder.Services.AddOptions<BotOptions>().BindConfiguration(BotOptions.Section);
builder.Services.AddOptions<StorageOptions>().BindConfiguration(StorageOptions.Section);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<S3FileStorage>();
builder.Services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<S3FileStorage>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Access>();
builder.Services.AddScoped<StudentFactsLoader>();
builder.Services.AddScoped<Notifier>();
builder.Services.AddScoped<HomeworkStats>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = true; // sub -> ClaimTypes.NameIdentifier
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = TokenService.SigningKey(jwt),
            NameClaimType = ClaimTypes.NameIdentifier,
        };
        o.Events = new JwtBearerEvents { OnTokenValidated = SessionValidation.OnTokenValidated };
    })
    .AddScheme<BotKeyOptions, BotKeyAuthentication>(BotKeyAuthentication.SchemeName,
        o => o.Key = builder.Configuration[$"{BotOptions.Section}:Key"] ?? "");

// Всё закрыто по умолчанию: препод с JWT, сменивший временный пароль. Публичные эндпоинты — [AllowAnonymous].
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .RequireAssertion(c => !c.User.HasClaim(x => x.Type == Policies.MustChangePasswordClaim))
        .Build())
    .AddPolicy(Policies.AnyTeacher, p => p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser())
    .AddPolicy(Policies.Bot, p => p.AddAuthenticationSchemes(BotKeyAuthentication.SchemeName).RequireAuthenticatedUser());

builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = Limits.MaxPdfBytes + 1024 * 1024);

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("Content-Disposition")));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (Cli.IsCommand(args))
    return await Cli.RunAsync(app, args);

if (app.Configuration.GetValue<bool>("Db:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

await app.Services.GetRequiredService<S3FileStorage>().EnsureBucketAsync(CancellationToken.None);

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.MapOpenApi().AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();

await app.RunAsync();
return 0;

public partial class Program;
