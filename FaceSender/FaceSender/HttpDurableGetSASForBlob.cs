using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace FaceSender
{
    public static class HttpDurableGetSASForBlob
    {
        [FunctionName("HttpDurableGetSASForBlob")]
        public static async Task<string> RunOrchestrator2(
            [OrchestrationTrigger] DurableOrchestrationContext context)
        {
            var resizedPicturesNames = context.GetInput<List<string>>();

            var tasks = new Task<string>[resizedPicturesNames.Count];
            for (int i = 0; i < resizedPicturesNames.Count; i++)
            {
                tasks[i] = context.CallActivityAsync<string>(
                "HttpDurableGetSASForBlob_GetSAS",
                resizedPicturesNames[i]);
            }

            await Task.WhenAll(tasks);

            string uri = "";
            for (int i = 0; i < tasks.Length; i++)
            {
                uri += tasks[i].Result + " ";
            }
            return uri;
        }

        [FunctionName("HttpDurableGetSASForBlob_GetSAS")]
        public static async Task<string> HttpGetSharedAccessSignatureForBlobAsync([ActivityTrigger] string fileName,
            [Blob("doneorders", FileAccess.Read, Connection = "StorageConnection")]CloudBlobContainer photosContainer, TraceWriter log)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return String.Empty;

            var photoBlob = await photosContainer.GetBlobReferenceFromServerAsync(fileName);
            return GetBlobSasUri(photoBlob);
        }

        [FunctionName("HttpDurableGetSASForBlob_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")]HttpRequestMessage req,
            [OrchestrationClient]DurableOrchestrationClient starter,
            TraceWriter log)
        {
            var content = req.Content;
            string jsonContent = content.ReadAsStringAsync().Result;
            dynamic resizedPicturesNames = JsonConvert.DeserializeObject<List<string>>(jsonContent);

            string instanceId = await starter.StartNewAsync("HttpDurableGetSASForBlob", resizedPicturesNames);
                       
            return starter.CreateCheckStatusResponse(req, instanceId);
        }

        static string GetBlobSasUri(ICloudBlob cloudBlob)
        {
            SharedAccessBlobPolicy sasConstraints = new SharedAccessBlobPolicy();
            sasConstraints.SharedAccessStartTime = DateTimeOffset.UtcNow.AddHours(-1);
            sasConstraints.SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddHours(24);
            sasConstraints.Permissions = SharedAccessBlobPermissions.List | SharedAccessBlobPermissions.Read;

            string sasToken = cloudBlob.GetSharedAccessSignature(sasConstraints);

            return cloudBlob.Uri + sasToken;
        }
    }
}