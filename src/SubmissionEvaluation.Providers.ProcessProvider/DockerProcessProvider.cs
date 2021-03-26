using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using SharpCompress.Archives;
using SharpCompress.Archives.Tar;
using SharpCompress.Common;
using SharpCompress.Writers;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Providers;
using static System.IO.Path;

namespace SubmissionEvaluation.Providers
{
    public class DockerProcessProvider : IProcessProvider
    {
        private readonly ILog log;
        private readonly ProcessProvider.ProcessProvider processProvider;
        private string dockerUrl;

        public DockerProcessProvider(ILog log)
        {
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            dockerUrl = isWindows ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock";

            this.log = log;
            processProvider = new ProcessProvider.ProcessProvider();
        }

        public async Task<ProcessResult> Execute(string path, string[] arguments, string input = null, string workingDir = null, int timeout = 60000,
            string encoding = null, List<FileDefinition> inputFiles = null, IDictionary<string, string> env = null, FolderMapping[] folderMappings = null,
            ISyncLock syncLock = null)
        {
            if (folderMappings == null)
            {
                folderMappings = new FolderMapping[0];
            }

            var lockCreated = false;
            try
            {
                if (syncLock == null)
                {
                    lockCreated = true;
                    syncLock = GetLock();
                }


                using var client = new DockerClientConfiguration(new Uri(dockerUrl)).CreateClient();
                var dlock = (DockerLock) syncLock;
                dlock.IsDirty = true;

                foreach (var folder in folderMappings)
                {
                    CopyToDockerImage(client, dlock.DockerId, folder);
                }

                var createdInputfiles = new List<string>();
                if (inputFiles != null)
                {
                    foreach (var inputFile in inputFiles)
                    {
                        var targetFilename = inputFile.Name.StartsWith("/") ? inputFile.Name : $"{workingDir}/{inputFile.Name}";
                        var lastModified = inputFile.LastModified;
                        CopyToDockerImage(client, dlock.DockerId, inputFile.ContentFilePath, targetFilename, lastModified);
                        createdInputfiles.Add(targetFilename);
                    }
                }

                var parameters = new List<string> {"exec", "-i"};

                if (!string.IsNullOrWhiteSpace(workingDir))
                {
                    parameters.Add("-w");
                    parameters.Add(workingDir);
                }

                if (env != null)
                {
                    foreach (var envPar in env)
                    {
                        parameters.Add("--env");
                        parameters.Add(envPar.Key + "=" + envPar.Value);
                    }
                }

                parameters.Add(dlock.DockerId);
                parameters.Add(path);

                if (arguments != null)
                {
                    parameters.AddRange(arguments);
                }

                var containers = await client.Containers.ListContainersAsync(new ContainersListParameters {All = false});


                var result = await processProvider.Execute("docker", parameters.ToArray(), input, timeout: timeout, encoding: encoding);


                foreach (var folder in dlock.Folders.Concat(folderMappings))
                {
                    CopyFromDockerImage(client, dlock.DockerId, folder);
                }

                if (dlock.Changes.Length > 0)
                {
                    var changesTask = client.Containers.InspectChangesAsync(dlock.DockerId);
                    var changes = changesTask.Result;
                    var changedFiles = changes.Select(x => x.Path);

                    var modifiedFiles = new List<ModifiedFile>();
                    foreach (var changedFile in changedFiles.Where(x =>
                        dlock.Changes.Any(y => x.EndsWith(y.Filename, StringComparison.InvariantCultureIgnoreCase))))
                    {
                        var data = CopyFromDockerImage(client, dlock.DockerId, changedFile);
                        var modifiedFile = new ModifiedFile
                        {
                            Filename = changedFile.StartsWith(workingDir ?? "", StringComparison.InvariantCultureIgnoreCase)
                                ? changedFile[$"{workingDir}/".Length..]
                                : changedFile,
                            Realfilename = changedFile,
                            Date = data.Item1,
                            Data = data.Item2
                        };
                        var oldFile = modifiedFiles.FirstOrDefault(x => string.Equals(x.Filename,
                            modifiedFile.Filename, StringComparison.InvariantCultureIgnoreCase));
                        if (oldFile != null)
                        {
                            log?.Warning("Doppelte Datei gefunden (unterschiedliche Groß/Kleinschreibung) {file}. Nehme neuere", modifiedFile.Filename);
                            if (oldFile.Date < modifiedFile.Date)
                            {
                                modifiedFiles.Remove(oldFile);
                            }
                            else
                            {
                                continue;
                            }
                        }

                        modifiedFiles.Add(modifiedFile);
                    }

                    result.ModifiedFiles = modifiedFiles.ToArray();
                }

                result.Filename = path;
                result.Arguments = arguments;
                return result;
            }
            finally
            {
                if (lockCreated && syncLock != null)
                {
                    ReleaseLock(syncLock);
                }
            }
        }

        public ISyncLock GetLock(FolderMapping[] folders = null, InteresstedFileChanges[] changes = null)
        {
            // TODO: Keep counting number of running containers, to prevent to many instances at same time!
            // TODO: Kill long running containers
            var finalFolders = folders ?? new FolderMapping[0];

            using var client = new DockerClientConfiguration(new Uri(dockerUrl)).CreateClient();
            CreateContainerResponse container = null;

            var dockerClient = client;
            try
            {
                var createTask = dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters
                {
                    Image = "test", Cmd = new[] {"timeout", "60m", "tail", "-f", "/dev/null"}, HostConfig = new HostConfig {AutoRemove = true}
                });
                container = createTask.Result; // TODO: Should add timeout handling!
            }
            catch (AggregateException exception)
            {
                var isRetrySuccess = false;
                log.Warning("Docker not responding.... Retrying ?");
                try
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        var _dockerurl = Environment.GetEnvironmentVariable("DOCKER_HOST");

                        if (_dockerurl != string.Empty)
                        {
                            dockerUrl = _dockerurl;
                            dockerClient.Dispose();
                            dockerClient = new DockerClientConfiguration(new Uri(dockerUrl)).CreateClient();

                            var createTask = dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters
                            {
                                Image = "test", Cmd = new[] {"timeout", "60m", "tail", "-f", "/dev/null"}, HostConfig = new HostConfig {AutoRemove = true}
                            });
                            container = createTask.Result; // TODO: Should add timeout handling!
                            isRetrySuccess = true;
                        }
                        else
                        {
                            throw new AggregateException("env DOCKER_HOST is Empty or null!", exception);
                        }
                    }
                    else
                    {
                        throw new AggregateException("Docker not responding after retry!", exception);
                    }
                }
                catch
                {
                    if (isRetrySuccess)
                    {
                        log.Warning("Docker responded on: " + dockerUrl);
                    }
                    else
                    {
                        throw new AggregateException("Docker not responding. Is Docker installed and running?", exception);
                    }
                }
            }

            if (container == null)
            {
                return null;
            }

            var startTask = dockerClient.Containers.StartContainerAsync(container.ID, new ContainerStartParameters());
            if (!startTask.Wait(60000))
            {
                throw new Exception("Container could not be started");
            }

            foreach (var folder in finalFolders)
            {
                CopyToDockerImage(dockerClient, container.ID, folder);
            }

            return new DockerLock(this)
            {
                LockActive = true, DockerId = container.ID, Folders = finalFolders.ToArray(), Changes = changes?.ToArray() ?? new InteresstedFileChanges[0]
            };
        }

        public void ReleaseLock(ISyncLock lockObject)
        {
            if (lockObject == null)
            {
                return;
            }

            var dockerlock = (DockerLock) lockObject;
            if (!dockerlock.LockActive)
            {
                return;
            }

            using (var client = new DockerClientConfiguration(new Uri(dockerUrl)).CreateClient())
            {
                client.Containers
                    .RemoveContainerAsync(dockerlock.DockerId, new ContainerRemoveParameters {Force = true, RemoveLinks = false, RemoveVolumes = false})
                    .Wait(TimeSpan.FromMinutes(10));
            }

            dockerlock.DockerId = null;
            dockerlock.LockActive = false;
        }

        private void CopyToDockerImage(IDockerClient client, string id, FolderMapping folder)
        {
            try
            {
                CopyToDockerImageViaApi(client, id, folder);
            }
            catch
            {
                // FIX: Api Method seems to have issues with larger files, try using
                // Commandline version. Not the best way, but could not solve the issue yet
                CopyToDockerImageViaCmd(id, folder);
            }
        }

        private static void CopyToDockerImageViaCmd(string id, FolderMapping folder)
        {
            var startinfo = new ProcessStartInfo("docker", $"cp {folder.Source}{DirectorySeparatorChar}. {id}:{folder.Target}")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            };
            var process = Process.Start(startinfo);
            var readTask = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(60000) || process.ExitCode != 0)
            {
                throw new Exception("Docker copy failed: " + readTask.Result);
            }
        }

        private static void CopyToDockerImageViaApi(IDockerClient client, string id, FolderMapping folder)
        {
            using var archive = TarArchive.Create();
            using var ms = new MemoryStream();
            archive.AddAllFromDirectory(folder.Source);

            archive.SaveTo(ms,
                new WriterOptions(CompressionType.None)
                {
                    ArchiveEncoding = new ArchiveEncoding {Forced = Encoding.UTF8, Default = Encoding.UTF8, Password = Encoding.UTF8}
                });
            ms.Flush();
            ms.Seek(0, SeekOrigin.Begin);
            var folderTarget = folder.Target;
            if (!folderTarget.EndsWith("/"))
            {
                folderTarget += "/";
            }

            var result = client.Containers.ExtractArchiveToContainerAsync(id,
                new ContainerPathStatParameters {AllowOverwriteDirWithFile = true, Path = folderTarget}, ms);
            if (!result.Wait(60000))
            {
                throw new Exception("Docker copy failed!");
            }
        }

        private void CopyToDockerImage(IDockerClient client, string id, string inputFileContentFilePath, string targetFilename, DateTime? lastModified)
        {
            using var archive = TarArchive.Create();
            using var ms = new MemoryStream();
            var inputStream = File.OpenRead(inputFileContentFilePath);
            archive.AddEntry(targetFilename, inputStream, inputStream.Length, lastModified ?? DateTime.Now);
            archive.SaveTo(ms, CompressionType.None);
            ms.Flush();
            ms.Seek(0, SeekOrigin.Begin);
            var result = client.Containers.ExtractArchiveToContainerAsync(id, new ContainerPathStatParameters {AllowOverwriteDirWithFile = true, Path = "/"},
                ms);
            if (!result.Wait(60000))
            {
                throw new Exception("Docker copy failed!");
            }
        }

        public void CopyTextToDockerImage2(DockerLock dockerLock, string content, string path)
        {
            using var client = new DockerClientConfiguration(new Uri(dockerUrl)).CreateClient();
            CopyTextToDockerImage(client, dockerLock.DockerId, content, path);
        }


        private void CopyTextToDockerImage(IDockerClient client, string id, string content, string targetFilename, DateTime? lastModified = null)
        {
            using var archive = TarArchive.Create();
            using var ms = new MemoryStream();
            var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(content ?? ""));
            archive.AddEntry(targetFilename, inputStream, inputStream.Length, lastModified ?? DateTime.Now);
            archive.SaveTo(ms, CompressionType.None);
            ms.Flush();
            ms.Seek(0, SeekOrigin.Begin);
            var result = client.Containers.ExtractArchiveToContainerAsync(id, new ContainerPathStatParameters {AllowOverwriteDirWithFile = true, Path = "/"},
                ms);
            if (!result.Wait(60000))
            {
                throw new Exception("Docker copy failed!");
            }
        }

        private static void CopyFromDockerImage(IDockerClient client, string id, FolderMapping folder)
        {
            if (folder.ReadOnly)
            {
                return;
            }

            var getTask = client.Containers.GetArchiveFromContainerAsync(id, new GetArchiveFromContainerParameters {Path = folder.Target}, false);

            using var ms = new MemoryStream();
            getTask.Result.Stream.CopyTo(ms);
            ms.Seek(0, SeekOrigin.Begin);

            using var tarArchive = TarArchive.Open(ms);
            foreach (var entry in tarArchive.Entries)
            {
                if (entry.IsDirectory)
                {
                    continue;
                }

                var dirName = GetFileName(folder.Target) ?? string.Empty;
                var relativePath = entry.Key.StartsWith(dirName) ? entry.Key[dirName.Length..] : entry.Key;
                if (relativePath[0] == '/')
                {
                    relativePath = relativePath[1..];
                }

                var path = Combine(folder.Source, relativePath);
                Directory.CreateDirectory(GetDirectoryName(path) ?? string.Empty);
                entry.WriteToFile(path, new ExtractionOptions {Overwrite = true, PreserveFileTime = true});
            }
        }

        internal (DateTime? Date, byte[] Data, string Filename) GetFile(DockerLock dlock, string filename)
        {
            using var client = new DockerClientConfiguration(new Uri(dockerUrl)).CreateClient();
            var file = CopyFromDockerImage(client, dlock.DockerId, filename);
            return (file.Item1, file.Item2, filename);
        }

        private Tuple<DateTime?, byte[]> CopyFromDockerImage(IDockerClient client, string id, string from)
        {
            var getTask = client.Containers.GetArchiveFromContainerAsync(id, new GetArchiveFromContainerParameters {Path = from}, false);
            using var ms = new MemoryStream();
            getTask.Result.Stream.CopyTo(ms);
            ms.Seek(0, SeekOrigin.Begin);
            using var tarArchive = TarArchive.Open(ms);
            var entry = tarArchive.Entries.First();
            using var resultStream = entry.OpenEntryStream();
            using var copyStream = new MemoryStream();
            resultStream.CopyTo(copyStream);
            return new Tuple<DateTime?, byte[]>(entry.LastModifiedTime, copyStream.ToArray());
        }
    }
}
