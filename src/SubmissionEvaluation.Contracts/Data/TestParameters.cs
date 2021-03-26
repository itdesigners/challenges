using System.Collections.Generic;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace SubmissionEvaluation.Contracts.Data
{
    public class TestParameters : ITest
    {
        public const int DefaultTimeout = 5;

        private string[] parameters = new string[0];
        public bool ClearSandbox { get; set; }
        public bool IncludePreviousTests { get; set; }
        public int Id { get; set; }

        [YamlMember(ScalarStyle = ScalarStyle.DoubleQuoted)]
        public string Hint { get; set; }

        public int Timeout { get; set; } = DefaultTimeout;

        [YamlMember(ScalarStyle = ScalarStyle.DoubleQuoted)]
        public string[] Parameters { get => parameters; set => parameters = value ?? new string[0]; }

        public InputDefinition Input { get; set; }
        public List<FileDefinition> InputFiles { get; set; }
        public OutputDefinition ExpectedOutput { get; set; }
        public OutputFileDefinition ExpectedOutputFile { get; set; }
        public CustomTestRunnerDefinition CustomTestRunner { get; set; }
    }
}
