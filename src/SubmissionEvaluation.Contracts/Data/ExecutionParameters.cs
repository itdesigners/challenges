using System.Collections.Generic;

namespace SubmissionEvaluation.Contracts.Data
{
    public class ExecutionParameters
    {
        private string[] arguments = new string[0];
        private string[] preludingTestParameters = new string[0];

        public string Path { get; set; }
        public string TestRunnerPath { get; set; }

        public string[] Arguments { get => arguments; set => arguments = value ?? new string[0]; }

        public string[] PreludingTestParameters { get => preludingTestParameters; set => preludingTestParameters = value ?? new string[0]; }

        public string Language { get; set; }
        public string CompilerVersion { get; set; }
        public string WorkingDirectory { get; set; }
        public string OutputEncoding { get; set; }
        public int TimeoutBonus { get; set; }
        public IDictionary<string, string> Env { get; set; }
        public List<string> OutputFilter { get; set; }
        public string PutStdinToFile { get; set; }
    }
}
