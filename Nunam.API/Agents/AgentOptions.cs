namespace Nunam.API.Agents
{
    public class AgentOptions
    {
        public const string SectionName = "Agents";

        /// <summary>The token the AI worker signs in with; the AI worker endpoints stay closed without one.</summary>
        public string? WorkerToken { get; set; }

        /// <summary>How long a code from the UI's Add machine stays valid.</summary>
        public int EnrollmentMinutes { get; set; } = 60;

        /// <summary>
        /// Folder holding the indexer's packages (<c>.msi</c>, <c>.deb</c>, <c>.rpm</c>) for the install
        /// commands to download; the newest version of each kind is offered.
        /// </summary>
        public string? PackageFolder { get; set; }

        /// <summary>Finished jobs are deleted this many days after they finish.</summary>
        public int KeepDoneJobsDays { get; set; } = 7;

        /// <summary>Indexer log entries are deleted after this many days.</summary>
        public int KeepLogsDays { get; set; } = 7;
    }
}
