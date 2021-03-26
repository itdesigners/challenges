using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace SubmissionEvaluation.Contracts.Data
{
    public class GameProperties : IGame, ICompilerProperties, IWithDescription
    {
        private string lastEditor;

        public int DelayBetweenRuns { get; set; } = 24 * 60;

        [YamlIgnore] [JsonIgnore] public bool IsAvailable => !(HasError || IsDraft);

        [YamlMember(Alias = "Author")] public string AuthorID { get; set; }

        [YamlMember(Alias = "LastEditor")]
        public string LastEditorID { get => string.IsNullOrEmpty(lastEditor) ? AuthorID : lastEditor; set => lastEditor = value; }

        [YamlIgnore] [JsonIgnore] public string Name { get; set; }

        public string Title { get; set; }

        public bool IsDraft { get; set; } = true;

        public DateTime Date { get; set; }

        public List<string> Languages { get; set; }

        [YamlIgnore] [JsonIgnore] public string Description { get; set; }

        public bool HasError { get; set; } = false;

        public string ErrorDescription { get; set; }

        public RunnerPropeties MatchRunner { get; set; } = new RunnerPropeties();

        public RunnerPropeties Verifier { get; set; } = new RunnerPropeties();

        public List<int> PossiblePlayersPerMatch { get; set; } = new List<int> {2};

        public int MatchTimeout { get; set; } = 60000;

        public List<List<string>> Starterpacks { get; set; }

        public string StarterpacksIntroduction { get; set; }

        [YamlIgnore] [JsonIgnore] public List<string> Files { get; set; }
    }
}
