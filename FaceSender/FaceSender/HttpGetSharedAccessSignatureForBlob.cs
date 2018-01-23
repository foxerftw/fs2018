
using System;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Blob;

namespace FaceSender
{
    public static class HttpGetSharedAccessSignatureForBlob
    {
        [FunctionName("HttpGetSharedAccessSignatureForBlob")]
        public static async System.Threading.Tasks.Task<IActionResult> RunAsync([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)]HttpRequest req,
            [Blob("doneorders", FileAccess.Read, Connection = "StorageConnection")]CloudBlobContainer photosContainer,
            TraceWriter log)
        {
            string fileName = req.Query["fileName"];
            if (string.IsNullOrWhiteSpace(fileName))
                return new BadRequestResult();

            var photoBlob = await photosContainer.GetBlobReferenceFromServerAsync(fileName);
            var photoUri = GetBlobSasUri(photoBlob);
            return new JsonResult(new { PhotoUri = photoUri });
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