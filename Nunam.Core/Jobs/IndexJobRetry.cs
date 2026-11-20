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

        /// <summary>
        /// Back to Pending while attempts remain; otherwise Failed with <paramref name="error"/>.
        /// Returns whether the job will be tried again.
        /// </summary>
        public static bool RetryOrFail(this IndexJob job, int maxAttempts, string error)
        {
            if (job.Attempts < maxAttempts)
            {
                job.Status = IndexJobStatus.Pending;
                return true;
            }

            job.Status = IndexJobStatus.Failed;
            job.Error = error.Length <= 2000 ? error : error[..2000];
            return false;
        }
    }
}
