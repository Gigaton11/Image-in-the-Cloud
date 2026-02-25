using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Runtime;
using Amazon.S3;
//using Amazon.SecretsManager;
using Cloud_Image_Uploader.Services;

var builder = WebApplication.CreateBuilder(args);

// Load user secrets in development
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

// Add services to the container.
builder.Services.AddControllersWithViews();

// Configure AWS credentials from configuration (includes user secrets)
var awsAccessKey = builder.Configuration["AWS:AccessKey"];
var awsSecretKey = builder.Configuration["AWS:SecretKey"];
var awsRegion = builder.Configuration["AWS:Region"] ?? "eu-north-1";

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
//builder.Services.AddAWSService<IAmazonSecretsManager>();
//builder.Services.AddScoped<AwsSecretsService>();

// Application services.
builder.Services.AddSingleton<S3Service>();
builder.Services.AddScoped<DynamoDbService>();

builder.Services.AddSingleton<IDynamoDBContext>(provider =>
{
    // DynamoDBContext is thread-safe and can be reused application-wide.
    var client = provider.GetRequiredService<IAmazonDynamoDB>();
    return new DynamoDBContext(client);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
