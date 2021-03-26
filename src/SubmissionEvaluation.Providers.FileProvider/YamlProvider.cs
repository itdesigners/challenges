using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Polly;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Exceptions;
using SubmissionEvaluation.Contracts.Providers;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SubmissionEvaluation.Providers.FileProvider
{
    public class YamlProvider
    {
        private readonly ILog log;

        protected YamlProvider(ILog log)
        {
            this.log = log;
        }

        public virtual IEnumerable<TestParameters> DeserializeTestProperties(string pathToFile)
        {
            try
            {
                return DeserializeTests(pathToFile);
            }
            catch (Exception ex)
            {
                throw new DeserializationException("Deserialisierung fehlgeschlagen. " + ex.Message, ex);
            }
        }

        public void SerializeTestProperties(string pathToFile, IEnumerable<TestParameters> tests)
        {
            try
            {
                SerializeTests(pathToFile, tests);
            }
            catch (Exception ex)
            {
                throw new SerializationException("Serialisierung fehlgeschlagen. " + ex.Message, ex);
            }
        }

        public void SerializeErrorDetails(string pathToFile, List<HintCategory> categories)
        {
            var serializer = new SerializerBuilder().WithNamingConvention(new CamelCaseNamingConvention()).Build();
            var output = new StringBuilder();
            var writer = new StringWriter(output);

            var first = true;
            categories ??= new List<HintCategory>();
            foreach (var category in categories)
            {
                if (!first)
                {
                    writer.WriteLine("---");
                }

                serializer.Serialize(writer, category);
                first = false;
            }

            writer.Flush();
            WriteTextToFile(pathToFile, output.ToString());
        }

        public List<HintCategory> DeserializeErrorDetails(string pathToFile)
        {
            var desCategories = new List<HintCategory>();
            var rawText = ReadTextFromFile(pathToFile);

            var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().WithNamingConvention(new CamelCaseNamingConvention()).Build();
            var text = RemoveNewlinesInQuotedText(rawText);
            var reader = new Parser(new StringReader(text));
            reader.Expect<StreamStart>();
            while (reader.Accept<DocumentStart>())
            {
                var categories = deserializer.Deserialize<HintCategory>(reader);
                if (categories != null)
                {
                    desCategories.Add(categories);
                }
            }

            return desCategories;
        }

        private ISerializer BuildSerializer(bool emitDefaults, bool referenceDuplicates)
        {
            var serializerBuilder = new SerializerBuilder().WithNamingConvention(new CamelCaseNamingConvention());
            if (emitDefaults)
            {
                serializerBuilder = serializerBuilder.EmitDefaults();
            }

            if (!referenceDuplicates)
            {
                serializerBuilder = serializerBuilder.DisableAliases();
            }

            return serializerBuilder.Build();
        }

        public virtual void Serialize<T>(string pathToFile, T obj, bool emitDefaults = true, bool referenceDuplicates = true)
        {
            var text = SerializeToText(obj, emitDefaults, referenceDuplicates);
            WriteTextToFile(pathToFile, text);
        }

        private void WriteTextToFile(string pathToFile, string text)
        {
            RetryOnFileLocked(() =>
            {
                using var file = new FileStream(pathToFile, FileMode.Create, FileAccess.Write, FileShare.None);
                using var myWriter = new StreamWriter(file, Encoding.UTF8, 1024, true);
                myWriter.Write(text);
                myWriter.Flush();
                file.Flush(true);
            }, () => log.Warning("Wiederhole da {file} blockiert", pathToFile));
        }

        private string ReadTextFromFile(string pathToFile)
        {
            if (!File.Exists(pathToFile))
            {
                throw new DeserializationException($"Deserialisierung fehlgeschlagen. {pathToFile} nicht vorhanden.", null);
            }

            for (var i = 0; i < 5; i++)
            {
                try
                {
                    return File.ReadAllText(pathToFile);
                }
                catch (IOException e)
                {
                    if (i == 0)
                    {
                        log.Warning("Wiederhole da {file} blockiert. {message}", pathToFile, e.Message);
                    }

                    Thread.Sleep(2500);
                }
            }

            throw new DeserializationException($"Could not read file {pathToFile}. It may be blocked by another process");
        }

        private string SerializeToText<T>(T obj, bool emitDefaults = true, bool referenceDuplicates = true)
        {
            var serializer = BuildSerializer(emitDefaults, referenceDuplicates);
            var myWriter = new StringWriter();
            serializer.Serialize(myWriter, obj);
            return myWriter.ToString();
        }

        private void RetryOnFileLocked(Action action, Action onRetry)
        {
            Policy.Handle<IOException>().WaitAndRetry(10, x =>
            {
                if (x == 1)
                {
                    onRetry();
                }

                return TimeSpan.FromMilliseconds(x * 200);
            }).Execute(action);
        }

        public T DeserializeFromText<T>(string text, HandleMode mode = HandleMode.ThrowException) where T : class, new()
        {
            try
            {
                var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().WithNamingConvention(new CamelCaseNamingConvention()).Build();
                var reader = new Parser(new StringReader(text));
                reader.Expect<StreamStart>();
                var result = deserializer.Deserialize<T>(reader);
                if (result != null)
                {
                    return result;
                }

                switch (mode)
                {
                    case HandleMode.CreateDefaultObject:
                    case HandleMode.CreateDefaultObjectAndDelete:
                        log.Warning("Parsen der Textdaten fehlgeschlagen, da null.");
                        return new T();
                    case HandleMode.ThrowException:
                    case HandleMode.ThrowExceptionAndDelete:
                        throw new DeserializationException("Parsen der Textdaten fehlgeschlagen, da null.", null);
                    case HandleMode.ReturnNull: return null;
                }
            }
            catch (Exception ex)
            {
                log.Warning("Parsen fehlgeschlagen.", ex.Message);
                if (mode == HandleMode.ThrowException || mode == HandleMode.ThrowExceptionAndDelete)
                {
                    throw new DeserializationException("Deserialisierung fehlgeschlagen. " + ex.Message, ex);
                }

                log.Warning("Mach mit Defaultdaten weiter");
            }

            return mode == HandleMode.ReturnNull ? null : new T();
        }

        public virtual T Deserialize<T>(string pathToFile, HandleMode mode = HandleMode.ThrowException, bool force = false) where T : class, new()
        {
            try
            {
                var text = ReadTextFromFile(pathToFile);
                var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().WithNamingConvention(new CamelCaseNamingConvention()).Build();
                var reader = new Parser(new StringReader(text));
                reader.Expect<StreamStart>();
                var result = deserializer.Deserialize<T>(reader);
                if (result != null)
                {
                    return result;
                }

                log.Warning("Laden von Datei {file} fehlgeschlagen, da null.", pathToFile);
                if (mode == HandleMode.ThrowExceptionAndDelete || mode == HandleMode.CreateDefaultObjectAndDelete)
                {
                    try
                    {
                        log.Warning("Lösche Datei {file}", pathToFile);
                        File.Delete(pathToFile);
                    }
                    catch (Exception)
                    {
                        // ignored, as we only try to delete the file, if we fails we will retry next time
                    }
                }

                switch (mode)
                {
                    case HandleMode.CreateDefaultObject:
                    case HandleMode.CreateDefaultObjectAndDelete:
                        result = new T();
                        break;
                    case HandleMode.ThrowException:
                    case HandleMode.ThrowExceptionAndDelete:
                        throw new DeserializationException($"Deserialisierung fehlgeschlagen. {pathToFile} konnte nicht gelesen werden.", null);
                    case HandleMode.ReturnNull: return null;
                    default: throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
                }

                return result;
            }
            catch (Exception ex)
            {
                log.Warning("Laden von Datei {file} fehlgeschlagen: {message}", pathToFile, ex.Message);
                if (mode == HandleMode.ThrowExceptionAndDelete || mode == HandleMode.CreateDefaultObjectAndDelete)
                {
                    try
                    {
                        log.Warning("Lösche Datei {file}", pathToFile);
                        File.Delete(pathToFile);
                    }
                    catch (Exception)
                    {
                        // ignored, when deletion fails, we will retry on next call
                    }
                }

                if (mode == HandleMode.ThrowException || mode == HandleMode.ThrowExceptionAndDelete)
                {
                    var exception = new DeserializationException("Deserialisierung fehlgeschlagen. " + ex.Message, ex);
                    log.Warning(exception, exception.Message, null);
                    throw exception;
                }

                log.Warning("Mach mit Defaultdaten weiter");
            }

            if (mode == HandleMode.ReturnNull)
            {
                return null;
            }

            return new T();
        }

        private IEnumerable<TestParameters> DeserializeTests(string pathToFile)
        {
            var dir = Path.GetDirectoryName(pathToFile);
            var rawText = ReadTextFromFile(pathToFile);
            return DeserializeTestsFromText(dir, rawText);
        }

        public IEnumerable<TestParameters> DeserializeTestsFromText(string dir, string rawtext)
        {
            var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().WithNamingConvention(new CamelCaseNamingConvention()).Build();
            var text = RemoveNewlinesInQuotedText(rawtext);
            var reader = new Parser(new StringReader(text));
            reader.Expect<StreamStart>();
            while (reader.Accept<DocumentStart>())
            {
                var testParameters = deserializer.Deserialize<TestParameters>(reader);
                if (testParameters != null)
                {
                    if (testParameters.InputFiles != null)
                    {
                        foreach (var inputFile in testParameters.InputFiles.Where(inputFile => inputFile?.ContentFile != null))
                        {
                            inputFile.ContentFilePath = Path.Combine(dir, inputFile.ContentFile);
                        }
                    }

                    yield return testParameters;
                }
            }
        }

        private void SerializeTests(string pathToFile, IEnumerable<TestParameters> tests)
        {
            var serializer = new SerializerBuilder().WithNamingConvention(new CamelCaseNamingConvention()).Build();
            var output = new StringBuilder();
            var writer = new StringWriter(output);

            var first = true;
            foreach (var test in tests)
            {
                if (!first)
                {
                    writer.WriteLine("---");
                }

                ClearTestStrings(test);

                serializer.Serialize(writer, test);
                first = false;
            }

            writer.Flush();
            WriteTextToFile(pathToFile, output.ToString());
        }

        private static void ClearTestStrings(TestParameters test)
        {
            static string ClearTestString(string str)
            {
                return Regex.Replace(str, @"\r\n?|\n", "\n");
            }

            if (!string.IsNullOrEmpty(test.Input?.Content))
            {
                test.Input.Content = ClearTestString(test.Input.Content);
            }

            if (!string.IsNullOrEmpty(test.ExpectedOutput?.Content))
            {
                test.ExpectedOutput.Content = ClearTestString(test.ExpectedOutput.Content);
            }

            if (test.ExpectedOutput?.Alternatives?.Count > 0)
            {
                test.ExpectedOutput.Alternatives = test.ExpectedOutput.Alternatives.Select(ClearTestString).ToList();
            }

            if (!string.IsNullOrEmpty(test.ExpectedOutputFile?.Content))
            {
                test.ExpectedOutputFile.Content = ClearTestString(test.ExpectedOutputFile.Content);
            }
        }

        private static string RemoveNewlinesInQuotedText(string rawtext)
        {
            var sb = new StringBuilder();
            var lastchr = '\0';
            var seekText = "";
            var insideQuoted = false;
            foreach (var chr in rawtext)
            {
                if (!insideQuoted)
                {
                    seekText += chr;
                    if (Regex.IsMatch(seekText, "^\\s*\\w+:\\s*\""))
                    {
                        insideQuoted = true;
                        sb.Append('"');
                        continue;
                    }

                    if (chr == '\n')
                    {
                        seekText = "";
                    }
                }

                if (chr == '"' && insideQuoted && lastchr != '\\')
                {
                    insideQuoted = false;
                    seekText = "";
                }

                if (!insideQuoted || chr != '\n' && chr != '\r')
                {
                    sb.Append(chr);
                }

                lastchr = chr;
            }

            var text = sb.ToString();
            return text;
        }

        public virtual T DeserializeWithDescription<T>(string file, bool forceLoad = false) where T : class, IWithDescription, new()
        {
            var fileContent = ReadTextFromFile(file);
            return DeserializeFromTextWithDescription<T>(fileContent);
        }

        public virtual T DeserializeFromTextWithDescription<T>(string fileContent) where T : class, IWithDescription, new()
        {
            var obj = DeserializeFromText<T>(fileContent);
            obj.Description = GetYamlDescription(fileContent);
            return obj;
        }

        private static string GetYamlDescription(string fileContent)
        {
            const string descriptionSeparator = "---";
            return fileContent[
                (fileContent.IndexOf(descriptionSeparator, fileContent.IndexOf(descriptionSeparator, StringComparison.InvariantCulture) + 1,
                    StringComparison.InvariantCulture) + descriptionSeparator.Length)..].Trim();
        }

        public virtual void SerializeWithDescription<T>(string path, T obj) where T : IWithDescription
        {
            var serializedChallenge = SerializeToText(obj);
            var content = BuildMarkdownContent(serializedChallenge, obj.Description);
            WriteTextToFile(path, content);
        }

        private static string BuildMarkdownContent(string headerProperties, string description)
        {
            const string beginningEnding = "---";
            var informationBuilder = new StringBuilder();
            informationBuilder.AppendLine(beginningEnding);
            informationBuilder.AppendLine(headerProperties);
            informationBuilder.AppendLine(beginningEnding);
            informationBuilder.Append(description);
            return informationBuilder.ToString();
        }
    }
}
