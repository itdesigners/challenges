using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;

namespace SubmissionEvaluation.Server.Classes
{
    public class ArchiveHelper
    {
        public static byte[] ConvertToZip(byte[] data, string filename)
        {
            var extractPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            var ms = new MemoryStream();
            using (var convertedArchive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                try
                {
                    switch (Path.GetExtension(filename)?.ToLowerInvariant())
                    {
                        case ".zip": return data;
                        case ".7z":
                            using (var stream = new MemoryStream(data))
                            {
                                using (var archiveFile = SevenZipArchive.Open(stream))
                                {
                                    foreach (var entry in archiveFile.Entries.Where(entry => !entry.IsDirectory))
                                    {
                                        entry.WriteToDirectory(extractPath, new ExtractionOptions {ExtractFullPath = true, Overwrite = true});
                                        var fullPath = Path.Combine(extractPath, entry.Key);
                                        convertedArchive.CreateEntryFromFile(fullPath, entry.Key);
                                    }
                                }
                            }

                            break;
                        case ".rar":
                            using (var stream = new MemoryStream(data))
                            {
                                using (var archiveFile = RarArchive.Open(stream))
                                {
                                    foreach (var entry in archiveFile.Entries.Where(entry => !entry.IsDirectory))
                                    {
                                        entry.WriteToDirectory(extractPath, new ExtractionOptions {ExtractFullPath = true, Overwrite = true});
                                        var fullPath = Path.Combine(extractPath, entry.Key);
                                        convertedArchive.CreateEntryFromFile(fullPath, entry.Key);
                                    }
                                }
                            }

                            break;
                    }
                }
                catch (Exception exception)
                {
                    throw new IOException($"Following error occured while converting the {filename} file:\n\n" + exception.Message, exception);
                }
            }

            ms.Flush();
            return ms.ToArray();
        }
    }
}
