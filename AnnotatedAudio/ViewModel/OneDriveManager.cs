//  ---------------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// 
//  The MIT License (MIT)
// 
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
// 
//  The above copyright notice and this permission notice shall be included in
//  all copies or substantial portions of the Software.
// 
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//  THE SOFTWARE.
//  ---------------------------------------------------------------------------------

using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Web.Http;

namespace AnnotatedAudio.ViewModel
{
    public class OneDriveManager 
    {
        public string Token { get; set; }

        public async Task<bool> DownloadFile(string fileName)
        {
            HttpResponseMessage httpResponse;

            // make a URI with the download address of a file in the "AnnotatedAudio" folder
            Uri targetLocation = new Uri($"https://api.onedrive.com/v1.0/drive/root:/AnnotatedAudio/{fileName}:/content");

            using (var client = new HttpClient())
            {
                // the request headers belong to the HttpClient object
                client.DefaultRequestHeaders.Add("Authorization", $"bearer {Token}");

                // HTTP GET - this checks whether said file is available for download at said location.
                httpResponse = await client.GetAsync(targetLocation);
            }
            // handle http response
            if (httpResponse.StatusCode == HttpStatusCode.Found || httpResponse.IsSuccessStatusCode)
            {
                // the download location was confirmed: proceed with the download
                string downloadString = httpResponse.Content.Headers["Content-Location"];
                Uri downloadUri = new Uri(downloadString);

                using (var client = new HttpClient())
                {
                    // an auth token is not needed here.

                    // HTTP GET 
                    httpResponse = await client.GetAsync(downloadUri);
                }

                IBuffer fileBuffer = await httpResponse.Content.ReadAsBufferAsync();
                StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                

                // write the bytes to the newly created file
                await Task.Run(() => {
                    System.IO.File.WriteAllBytes(file.Path, fileBuffer.ToArray());
                });
                this.LogMessage(ToString(), "file downloaded successfully");

            }
            else
            {
                // not able to download anything from the given URI
                this.LogMessage(ToString(), "download target failed: "+httpResponse.StatusCode);
            }

            return httpResponse.IsSuccessStatusCode;
        }
        public async Task<bool> DeleteFile(string fileName)
        {
            HttpResponseMessage httpResponse;

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", $"bearer {Token}");
                httpResponse = await client.DeleteAsync(new Uri($"https://api.onedrive.com/v1.0/drive/root:/AnnotatedAudio/{fileName}"));
            }

            return httpResponse.IsSuccessStatusCode;
        }
        public async Task<bool> UploadFile(StorageFile file, Action<float> uploadProgress)
        {
            // process file
            if (file == null) return false;

            string fileName = file.Name;
            HttpResponseMessage httpResponse;
            byte[] byteArray;
            ulong streamSize;

            Uri uploadSessionRequestLocation = new Uri($"https://api.onedrive.com/v1.0/drive/root:/AnnotatedAudio/{fileName}:/upload.createSession");

            // request upload session:
            httpResponse = await requestUploadSessionAsync(Token, fileName, uploadSessionRequestLocation);

            String httpResponseOutput = "";
            if (httpResponse.IsSuccessStatusCode)
            {
                //operation was successful: print http status code
                httpResponseOutput += $"http response: {(int)httpResponse.StatusCode}: {httpResponse.StatusCode}";
                this.LogMessage(ToString(), httpResponseOutput);

                // begin uploading fragments:

                // get JSON response
                JObject jsonResponse = JObject.Parse(httpResponse.Content.ReadAsStringAsync().GetResults());

                // values to pull from JSON:
                Uri uploadSessionUri = null;
                DateTime expirationDT = new DateTime();

                if (jsonResponse["uploadUrl"] != null && jsonResponse["expirationDateTime"] != null)
                {
                    uploadSessionUri = new Uri(jsonResponse["uploadUrl"].Value<string>());
                    expirationDT = DateTime.Parse(jsonResponse["expirationDateTime"].Value<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind);
                }
                else
                {
                    this.LogMessage(ToString(), "upload failed: missing JSON data");
                    return false;
                }

                // make the PUT request
                using (var client = new HttpClient())
                {
                    // the request headers belong to the HttpClient object
                    client.DefaultRequestHeaders.Add("Authorization", $"bearer {Token}");

                    // write the file contents to a byte array
                    using (IRandomAccessStreamWithContentType stream = await file.OpenReadAsync())
                    {
                        streamSize = stream.Size;
                        byteArray = new byte[streamSize];
                        using (DataReader reader = new DataReader(stream))
                        {
                            await reader.LoadAsync((uint)streamSize);
                            reader.ReadBytes(byteArray);
                        }
                    }

                    // need to identify the content type for the HTTP PUT request
                    string contentTypeString;
                    switch (Path.GetExtension(fileName))
                    {
                        case ".mp4":
                            contentTypeString = "video/mp4";
                            break;
                        case ".json":
                            contentTypeString = "application/json";
                            break;
                        case ".gif":
                            contentTypeString = "image/gif";
                            break;
                        case ".zip":
                            contentTypeString = "application/zip";
                            break;
                        default:
                            contentTypeString = "application/json";
                            break;
                    }

                    // upload all bytes in the byte array, possibly in several discrete "chunks"
                    IHttpContent putContent;
                    int bytesRemaining = (int)streamSize;
                    int chunkSize;
                    byte[] byteArraySection;
                    int startIndex = 0;
                    int chunkNumber = 1;
                    const int chunkLimit = 62000000;

                    // as long as there is more data to upload:
                    while (bytesRemaining > 0)
                    {
                        // determine which section of entire byte array to upload:
                        chunkSize = Math.Min(chunkLimit, bytesRemaining);
                        byteArraySection = new byte[chunkSize];
                        Array.Copy(byteArray, startIndex, byteArraySection, 0, chunkSize);

                        // create an httpContent with a byte buffer
                        putContent = new HttpBufferContent(byteArraySection.AsBuffer());
                        // add headers that belong to the putContent (they describe the type and amount of data):
                        putContent.Headers["Content-Range"] = $"bytes {startIndex}-{startIndex + chunkSize - 1}/{streamSize}";
                        putContent.Headers["Content-Length"] = chunkSize.ToString();
                        putContent.Headers["Content-Type"] = contentTypeString;

                        // HTTP PUT 
                        System.Diagnostics.Debug.WriteLine($"uploading bytes {startIndex} to {startIndex + chunkSize - 1}");
                        httpResponse = await client.PutAsync(uploadSessionUri, putContent);
                        this.LogMessage(ToString(), $"http response: {(int)httpResponse.StatusCode}: {httpResponse.StatusCode} (chunk {chunkNumber})");

                        // if successful, then update placeholders before continuing
                        startIndex += chunkSize;
                        bytesRemaining -= chunkSize;
                        chunkNumber++;
                    }
                }
                return true;
            }
            else
            {
                //http request returned failure: print http status code
                httpResponseOutput += $"http response: {(int)httpResponse.StatusCode}: {httpResponse.StatusCode}";

                // get JSON response
                JObject jsonResponse = JObject.Parse(httpResponse.Content.ReadAsStringAsync().GetResults());

                // check for an "error" property
                JToken errorToken = jsonResponse["error"];
                if (errorToken != null)
                {
                    // check for an error "message" property
                    JToken causeToken = errorToken["message"];
                    if (causeToken != null)
                    {
                        // add in the extra information
                        httpResponseOutput += $"\n{causeToken.ToString()}";
                    }
                }
                // write error details
                this.LogMessage(ToString(), httpResponseOutput);
                return false;
            }
        }

        // Make an HTTP POST that requests to open an upload session, for uploading a file.
        private async Task<HttpResponseMessage> requestUploadSessionAsync(string token, string fileName, Uri uploadSessionLocation)
        {

            using (var client = new HttpClient())
            {
                // the request headers belong to the HttpClient
                client.DefaultRequestHeaders.Add("Authorization", $"bearer {token}");

                // the post content string
                string jsonstr =
                "{" +
                    "\"item\": {" +
                        "\"@name.conflictBehavior\": \"replace\"," +
                        $"\"name\": \"{fileName}\"" +
                    "}" +
                "}";

                var postContent = new HttpStringContent(jsonstr);

                // other headers belong to the post content
                postContent.Headers["Content-Type"] = "application/json";

                // HTTP POST 
                return await client.PostAsync(uploadSessionLocation, postContent);
            }
        }
    }
}
