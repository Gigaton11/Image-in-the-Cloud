// Services/AwsSecretsService.cs
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
namespace Cloud_Image_Uploader.Services
{
    

    public class AwsSecretsService
    {
        private readonly IAmazonSecretsManager _secretsManager;

        // Constructor injection (preferred for testability)
        public AwsSecretsService(IAmazonSecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public async Task<string> GetSecretAsync(string secretName)
        {
            try
            {
                // AWSCURRENT ensures we read the active version unless caller needs staging labels.
                var request = new GetSecretValueRequest
                {
                    SecretId = secretName,
                    VersionStage = "AWSCURRENT"
                };

                var response = await _secretsManager.GetSecretValueAsync(request);
                return response.SecretString;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to fetch secret '{secretName}'.", ex);
            }
        }
    }
}
