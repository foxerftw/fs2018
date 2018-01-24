using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace FaceSender
{
    public static class HttpDurableOrchestration
    {
        [FunctionName("HttpDurableOrchestration")]
        public static async Task<string> RunOrchestrator(
            [OrchestrationTrigger] DurableOrchestrationContext context)
        {
            var pictureResizeRequests = context.GetInput<List<PictureResizeRequest>>();

            var tasks = new Task<string>[pictureResizeRequests.Count];
            for (int i = 0; i < pictureResizeRequests.Count; i++)
            {
                tasks[i] = context.CallActivityAsync<string>(
                "HttpDurableOrchestration_ResizePicture",
                pictureResizeRequests[i]);
            }
            
            await Task.WhenAll(tasks);

            string uri = tasks.ToString();
            return uri;
        }

        [FunctionName("HttpDurableOrchestration_ResizePicture")]
        public static async Task<string> ResizePicture([ActivityTrigger] PictureResizeRequest pictureResizeRequest,
            [Blob("photos", FileAccess.Read, Connection = "StorageConnection")]CloudBlobContainer photosContainer,
            [Blob("doneorders/{rand-guid}", FileAccess.ReadWrite, Connection = "StorageConnection")]ICloudBlob resizedPhotoCloudBlob,
            TraceWriter log)
        {
            var photoStream = await GetSourcePhotoStream(photosContainer, pictureResizeRequest.FileName);
            SetAttachmentAsContentDisposition(resizedPhotoCloudBlob, pictureResizeRequest);

            var image = Image.Load(photoStream);
            image.Mutate(e => e.Resize(pictureResizeRequest.RequiredWidth, pictureResizeRequest.RequiredHeight));

            var resizedPhotoStream = new MemoryStream();
            image.Save(resizedPhotoStream, new JpegEncoder());
            resizedPhotoStream.Seek(0, SeekOrigin.Begin);

            await resizedPhotoCloudBlob.UploadFromStreamAsync(resizedPhotoStream);

            return resizedPhotoCloudBlob.Name;
        }

        [FunctionName("HttpDurableOrchestration_HttpGetSharedAccessSignatureForBlob")]
        public static async Task<string> HttpGetSharedAccessSignatureForBlobAsync([ActivityTrigger] string fileName,
            [Blob("doneorders", FileAccess.Read, Connection = "StorageConnection")]CloudBlobContainer photosContainer, TraceWriter log)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return String.Empty;

            var photoBlob = await photosContainer.GetBlobReferenceFromServerAsync(fileName);
            return GetBlobSasUri(photoBlob);
        }       
        

        [FunctionName("HttpDurableOrchestration_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")]HttpRequestMessage req,
            [OrchestrationClient]DurableOrchestrationClient starter,
            TraceWriter log)
        {
            var content = req.Content;
            string jsonContent = content.ReadAsStringAsync().Result;
            dynamic pictureResizeRequests = JsonConvert.DeserializeObject<List<PictureResizeRequest>>(jsonContent);

            string instanceId = await starter.StartNewAsync("HttpDurableOrchestration", pictureResizeRequests);

            return starter.CreateCheckStatusResponse(req, instanceId);
        }

        private static void SetAttachmentAsContentDisposition(ICloudBlob resizedPhotoCloudBlob,
            PictureResizeRequest pictureResizeRequest)
        {
            resizedPhotoCloudBlob.Properties.ContentDisposition =
                $"attachment; filename={pictureResizeRequest.RequiredWidth}x{pictureResizeRequest.RequiredHeight}.jpeg";
        }

        private static async Task<Stream> GetSourcePhotoStream(CloudBlobContainer photosContainer,
            string fileName)
        {
            var photoBlob = await photosContainer.GetBlobReferenceFromServerAsync(fileName);
            var photoStream = await photoBlob.OpenReadAsync(AccessCondition.GenerateEmptyCondition(),
                new BlobRequestOptions(), new OperationContext());
            return photoStream;
        }

        private static string GetBlobSasUri(ICloudBlob cloudBlob)
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