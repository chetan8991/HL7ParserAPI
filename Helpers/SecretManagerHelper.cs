using Google.Cloud.SecretManager.V1;
using System;

namespace HL7ParserAPI.Helpers
{
    public static class SecretManagerHelper
    {
        // This method retrieves the latest version of a secret by its name
        public static string GetSecret(string secretId, string projectId = "spartan-acrobat-452620-t6")
        {
            try
            {
                SecretManagerServiceClient client = SecretManagerServiceClient.Create();
                SecretVersionName secretVersionName = new SecretVersionName(projectId, secretId, "latest");

                AccessSecretVersionResponse result = client.AccessSecretVersion(secretVersionName);

                // Return the payload as a string
                return result.Payload.Data.ToStringUtf8();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accessing secret '{secretId}': {ex.Message}");
                throw;
            }
        }
    }
}
