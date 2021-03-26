using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Providers;

namespace SubmissionEvaluation.Providers.ProcessProvider
{
    public class ProcessProvider : IProcessProvider
    {
        static ProcessProvider()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public ProcessProvider()
        {
        }

        public async Task<ProcessResult> Execute(string path, string[] arguments, string input = null, string workingDir = null, int timeout = 60000,
            string encoding = null, List<FileDefinition> inputFiles = null, IDictionary<string, string> env = null, FolderMapping[] folderMappings = null,
            ISyncLock syncLock = null)
        {
            const int maxOutputCharacter = 500_000;
            var outputBuilder = new StringBuilder(maxOutputCharacter, maxOutputCharacter);
            var task = Task.Factory.StartNew(() =>
            {
                workingDir ??= Path.GetDirectoryName(path);

                var lockObject = new object();
                var result = new ProcessResult {Arguments = arguments, Filename = path, WorkingDir = workingDir};
                using var outputWaitHandle = new AutoResetEvent(false);
                using var errorWaitHandle = new AutoResetEvent(false);
                using var process = new Process
                {
                    StartInfo =
                    {
                        WorkingDirectory = workingDir ?? string.Empty,
                        FileName = path,
                        Arguments = string.Join(" ", EnquoteIfNecessary(arguments)),
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        RedirectStandardInput = true,
                        UseShellExecute = false
                    }
                };

                if (env != null)
                {
                    foreach (var (key, value) in env)
                    {
                        process.StartInfo.EnvironmentVariables[key] = value;
                    }
                }

                if (!string.IsNullOrWhiteSpace(encoding))
                {
                    process.StartInfo.StandardOutputEncoding = encoding switch
                    {
                        "UTF7" => Encoding.UTF7,
                        "UTF8" => Encoding.UTF8,
                        "UTF32" => Encoding.UTF32,
                        _ => Encoding.GetEncoding(int.Parse(encoding))
                    };
                }

                try
                {
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data == null)
                        {
                            if (!outputWaitHandle.SafeWaitHandle.IsClosed)
                            {
                                outputWaitHandle.Set();
                            }
                        }
                        else
                        {
                            lock (lockObject)
                            {
                                if (outputBuilder.Length + e.Data.Length + Environment.NewLine.Length < maxOutputCharacter)
                                {
                                    outputBuilder.AppendLine(e.Data);
                                }
                            }
                        }
                    };
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (e.Data == null)
                        {
                            if (!errorWaitHandle.SafeWaitHandle.IsClosed)
                            {
                                errorWaitHandle.Set();
                            }
                        }
                        else
                        {
                            lock (lockObject)
                            {
                                if (outputBuilder.Length + e.Data.Length + Environment.NewLine.Length < maxOutputCharacter)
                                {
                                    outputBuilder.AppendLine(e.Data);
                                }
                            }
                        }
                    };

                    var watch = Stopwatch.StartNew();
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (input != null)
                    {
                        if (encoding == "UTF8")
                        {
                            using var utf8Writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false));
                            utf8Writer.Write(input);
                        }
                        else
                        {
                            process.StandardInput.Write(input);
                            process.StandardInput.Close();
                        }
                    }

                    result.ExecutionDuration = (int) watch.ElapsedMilliseconds;

                    if (process.WaitForExit(timeout))
                    {
                        result.ExitCode = process.ExitCode;
                    }
                    else
                    {
                        result.Timeout = true;
                        process.Kill();
                    }

                    watch.Stop();
                    result.ExecutionDuration = (int) watch.ElapsedMilliseconds;

                    outputWaitHandle.WaitOne(timeout);
                    errorWaitHandle.WaitOne(timeout);

                    lock (lockObject)
                    {
                        result.Output = outputBuilder.ToString();
                    }
                }
                catch (Exception ex)
                {
                    result.ExitCode = -1;
                    result.Exception = true;
                    result.Output = ex.Message;
                }

                return result;
            });

            return await Task.WhenAny(task, Task.Delay(timeout * 2)) == task
                ? task.Result
                : new ProcessResult {Timeout = true, Filename = path, Output = outputBuilder.ToString(), Arguments = arguments};
        }

        public ISyncLock GetLock(FolderMapping[] folders = null, InteresstedFileChanges[] changes = null)
        {
            return new SyncLock();
        }

        public void ReleaseLock(ISyncLock lockObject)
        {
        }

        private IEnumerable<string> EnquoteIfNecessary(IEnumerable<string> arguments)
        {
            return arguments.Select(x => x.StartsWith("\"") || !x.Contains(" ") ? x : "\"" + x + "\"");
        }
    }
}
