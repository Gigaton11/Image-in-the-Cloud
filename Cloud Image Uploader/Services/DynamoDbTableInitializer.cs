using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Cloud_Image_Uploader.Services;

//
// Creates any DynamoDB tables that don't yet exist when AWS:AutoCreateTables is true.
// Intended for local/dev environments only; production tables are managed by Terraform.
//
public class DynamoDbTableInitializer
{
    private readonly IAmazonDynamoDB _dynamoDbClient;
    private readonly ILogger<DynamoDbTableInitializer> _logger;

    public DynamoDbTableInitializer(IAmazonDynamoDB dynamoDbClient, ILogger<DynamoDbTableInitializer> logger)
    {
        _dynamoDbClient = dynamoDbClient;
        _logger = logger;
    }

    // Creates FileMetadata, DownloadRecords, UserAccounts, and PasswordResetTokens tables
    // if they are absent. Tables are provisioned with on-demand billing.
    public async Task EnsureTablesExistAsync()
    {
        await EnsureTableExistsAsync("FileMetadata", "FileId");
        await EnsureTableExistsAsync("DownloadRecords", "FileId");
        await EnsureTableExistsAsync("UserAccounts", "UserId");
        await EnsureTableExistsAsync("PasswordResetTokens", "TokenId");
    }

    private async Task EnsureTableExistsAsync(string tableName, string hashKeyName)
    {
        try
        {
            var table = await _dynamoDbClient.DescribeTableAsync(tableName);
            _logger.LogInformation("DynamoDB table available: {TableName} ({Status})", tableName, table.Table.TableStatus);
        }
        catch (ResourceNotFoundException)
        {
            _logger.LogWarning("DynamoDB table missing. Creating {TableName}", tableName);

            await _dynamoDbClient.CreateTableAsync(new CreateTableRequest
            {
                TableName = tableName,
                BillingMode = BillingMode.PAY_PER_REQUEST,
                AttributeDefinitions = new List<AttributeDefinition>
                {
                    new(hashKeyName, ScalarAttributeType.S)
                },
                KeySchema = new List<KeySchemaElement>
                {
                    new(hashKeyName, KeyType.HASH)
                }
            });

            await WaitForActiveTableAsync(tableName);
        }
    }

    // Polls DescribeTable every second until the table reaches ACTIVE status.
    private async Task WaitForActiveTableAsync(string tableName)
    {
        while (true)
        {
            var response = await _dynamoDbClient.DescribeTableAsync(tableName);
            if (response.Table.TableStatus == TableStatus.ACTIVE)
            {
                _logger.LogInformation("DynamoDB table ready: {TableName}", tableName);
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
}