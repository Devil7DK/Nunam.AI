using Nunam.Core.Entities;

namespace Nunam.Core.Jobs
{
    public static class IndexJobRetry
    {
        /// <summary>Back to Pending without using up the attempt: the failure wasn't the job's.</summary>
        public static void ReturnToQueue(this IndexJob job)
        {
            job.Status = IndexJobStatus.Pending;
            job.Attempts = Math.Max(0, job.Attempts - 1);
        }
    }
}
