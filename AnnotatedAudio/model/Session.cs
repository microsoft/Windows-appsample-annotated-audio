
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
using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;

namespace AnnotatedAudio.Model
{
    public class Session
    {
        public string Name { get; set; }
        public Guid Id { get; set; }
        public int Segments { get; set; }
        public DateTime Created { get; set; }

        public async Task<StorageFolder> GetFolder() => await ApplicationData.Current.LocalFolder.GetFolderAsync(Id.ToString());

        public async Task CompressSession(bool deleteSourceFolder)
        {
			// The folder where a Session is stored.
            var folder = await Windows.Storage.ApplicationData.Current.LocalFolder.GetFolderAsync(Id.ToString());
            var files = await folder.GetFilesAsync();
            using (var memoryStream = new MemoryStream())
            {
                // Create zip archive stream.
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create))
                {
                    // For each file in the Session folder.
                    foreach (var sessionFile in files)
                    {
                        // Store the contents of the file into a byte array.
                        byte[] buffer = WindowsRuntimeBufferExtensions.ToArray(await FileIO.ReadBufferAsync(sessionFile));
                        // Create an entry in the archive. 
                        var entry = archive.CreateEntry(sessionFile.Name);
                        // Open a stream to the new entry to write the buffer.
                        using (var entryStream = entry.Open())
                        {
                            await entryStream.WriteAsync(buffer, 0, buffer.Length);
                        }
                    }
                }
                // Attempt to find the zip file and delete if it exists.
                var item = await ApplicationData.Current.LocalFolder.TryGetItemAsync($"{Id.ToString()}.zip");
                if (item != null)
                {
                    await item.DeleteAsync();
                }
				// Create the new zip file.
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync($"{Id.ToString()}.zip");
                using (var fileStream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    // Write compressed data from memory to file
                    using (var zipStream = fileStream.AsStreamForWrite())
                    {
                        byte[] buffer = memoryStream.ToArray();
                        zipStream.Write(buffer, 0, buffer.Length);
                        zipStream.Flush();
                    }
                }
            }
			// Delete the source folder after it's been compressed.
            if (deleteSourceFolder)
            {
                await folder.DeleteAsync();
            }
        }

        public async Task DecompressSession(bool deleteSourceZip)
        {
            var zipfile = await Windows.Storage.ApplicationData.Current.LocalFolder.GetFileAsync($"{Id.ToString()}.zip");
            using (var stream = await zipfile.OpenAsync(FileAccessMode.Read))
            {
                using (var archive = new ZipArchive(stream.AsStreamForRead()))
                {
                    StorageFolder extractfolder;
                    try
                    {
                        extractfolder = await Windows.Storage.ApplicationData.Current.LocalFolder.GetFolderAsync(Id.ToString());
                    }
                    catch (FileNotFoundException)
                    {
                        extractfolder = await Windows.Storage.ApplicationData.Current.LocalFolder.CreateFolderAsync(Id.ToString());
                    }
                    archive.ExtractToDirectory(extractfolder.Path);
                }
            }

            // Delete the ZIP file after it's been decompressed.
            if (deleteSourceZip)
            {
                await zipfile.DeleteAsync();
            }

        }
    }
}