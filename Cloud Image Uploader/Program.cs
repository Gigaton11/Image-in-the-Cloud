using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.SimpleEmailV2;
//using Amazon.SecretsManager;
using Cloud_Image_Uploader.Models;
using Cloud_Image_Uploader.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

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

// Configure AWS credentials from configuration (includes user secrets)
var awsAccessKey = builder.Configuration["AWS:AccessKey"];
var awsSecretKey = builder.Configuration["AWS:SecretKey"];
var awsRegion = builder.Configuration["AWS:Region"] ?? "eu-north-1";
var autoCreateTables = builder.Configuration.GetValue<bool>("AWS:AutoCreateTables");

if (string.IsNullOrEmpty(awsAccessKey) || string.IsNullOrEmpty(awsSecretKey))
{
    throw new InvalidOperationException("AWS credentials (AWS:AccessKey and AWS:SecretKey) are not configured. Please set them using 'dotnet user-secrets set'.");
}

var awsCredentials = new BasicAWSCredentials(awsAccessKey, awsSecretKey);
var awsRegionEndpoint = RegionEndpoint.GetBySystemName(awsRegion);

builder.Services.AddSingleton<AWSCredentials>(awsCredentials);
builder.Services.AddSingleton<IAmazonS3>(provider => 
    new AmazonS3Client(awsCredentials, new AmazonS3Config { RegionEndpoint = awsRegionEndpoint }));
builder.Services.AddSingleton<IAmazonDynamoDB>(provider =>
    new AmazonDynamoDBClient(awsCredentials, new AmazonDynamoDBConfig { RegionEndpoint = awsRegionEndpoint }));
builder.Services.AddSingleton<IAmazonSimpleEmailServiceV2>(provider =>
    new AmazonSimpleEmailServiceV2Client(awsCredentials, new AmazonSimpleEmailServiceV2Config { RegionEndpoint = awsRegionEndpoint }));
//builder.Services.AddAWSService<IAmazonSecretsManager>();
//builder.Services.AddScoped<AwsSecretsService>();

// Application services.
builder.Services.AddSingleton<S3Service>();
builder.Services.AddScoped<DynamoDbService>();
builder.Services.AddScoped<ImageProcessingService>();
builder.Services.AddScoped<UserAccountService>();
builder.Services.AddScoped<PasswordResetEmailService>();
builder.Services.AddSingleton<DynamoDbTableInitializer>();
builder.Services.AddSingleton<FileDeletionSchedulerService>();
builder.Services.AddHostedService<ExpiredFileCleanupService>();

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

if (autoCreateTables)
{
    using var scope = app.Services.CreateScope();
    var tableInitializer = scope.ServiceProvider.GetRequiredService<DynamoDbTableInitializer>();
    await tableInitializer.EnsureTablesExistAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
