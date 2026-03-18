using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.SimpleEmailV2;
using Cloud_Image_Uploader.Models;
using Cloud_Image_Uploader.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
// Force UTC timestamps in logs so expiration/debug timings match server-side checks.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff 'UTC' ";
    options.UseUtcTimestamp = true;
});

// Load user secrets in development
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
var runningOnCloudRun = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("K_SERVICE"));
var useForwardedHeaders = builder.Configuration.GetValue<bool?>("UseForwardedHeaders") ?? runningOnCloudRun;

if (useForwardedHeaders)
{
    // Cloud hosts typically terminate TLS and forward client details via proxy headers.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

// Configure AWS credentials from configuration (includes user secrets)
var awsAccessKey = builder.Configuration["AWS:AccessKey"];
var awsSecretKey = builder.Configuration["AWS:SecretKey"];
var awsRegion = builder.Configuration["AWS:Region"] ?? "eu-north-1";
var autoCreateTables = builder.Configuration.GetValue<bool>("AWS:AutoCreateTables");

if (string.IsNullOrWhiteSpace(awsAccessKey) ^ string.IsNullOrWhiteSpace(awsSecretKey))
{
    throw new InvalidOperationException("AWS:AccessKey and AWS:SecretKey must be set together when using static credentials.");
}

var awsOptions = builder.Configuration.GetAWSOptions();
awsOptions.Region = RegionEndpoint.GetBySystemName(awsRegion);

if (!string.IsNullOrWhiteSpace(awsAccessKey) && !string.IsNullOrWhiteSpace(awsSecretKey))
{
    awsOptions.Credentials = new BasicAWSCredentials(awsAccessKey, awsSecretKey);
}

builder.Services.AddDefaultAWSOptions(awsOptions);
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddAWSService<IAmazonDynamoDB>();
builder.Services.AddAWSService<IAmazonSimpleEmailServiceV2>();

// Application services.
builder.Services.AddSingleton<S3Service>();
builder.Services.AddScoped<DynamoDbService>();
builder.Services.AddScoped<ImageProcessingService>();
builder.Services.AddScoped<UserAccountService>();
builder.Services.AddScoped<PasswordResetEmailService>();
builder.Services.AddSingleton<DynamoDbTableInitializer>();
builder.Services.AddSingleton<FileDeletionSchedulerService>();
builder.Services.AddSingleton<ExpiredFileCleanupService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ExpiredFileCleanupService>());

builder.Services.AddSingleton<IDynamoDBContext>(provider =>
{
    // DynamoDBContext is thread-safe and can be reused application-wide.
    var client = provider.GetRequiredService<IAmazonDynamoDB>();
    return new DynamoDBContext(client, new DynamoDBContextConfig
    {
        // Prevent implicit DescribeTable calls, which are often blocked in least-privilege IAM policies.
        DisableFetchingTableMetadata = true
    });
});

var app = builder.Build();

if (autoCreateTables && !app.Environment.IsDevelopment())
{
    app.Logger.LogWarning("AWS:AutoCreateTables is enabled outside Development and will be ignored.");
}

if (autoCreateTables && app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var tableInitializer = scope.ServiceProvider.GetRequiredService<DynamoDbTableInitializer>();
    await tableInitializer.EnsureTablesExistAsync();
}

// Configure the HTTP request pipeline.
var useHttps = app.Configuration.GetValue<bool>("UseHttps", defaultValue: true);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    if (useHttps)
    {
        // The default HSTS value is 30 days.
        app.UseHsts();
    }
}

if (useForwardedHeaders)
{
    app.UseForwardedHeaders();
}

if (useHttps)
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
