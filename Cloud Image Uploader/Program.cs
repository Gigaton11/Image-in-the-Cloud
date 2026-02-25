using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.S3;
//using Amazon.SecretsManager;
using Cloud_Image_Uploader.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Use AWS SDK configuration from appsettings/environment.
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddAWSService<IAmazonDynamoDB>();
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
