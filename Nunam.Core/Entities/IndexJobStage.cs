namespace Nunam.Core.Entities
{
    public enum IndexJobStage
    {
        None = 0,
        Detect = 1,
        Parse = 2,
        Describe = 3,
        Embed = 4,
        Upsert = 5,
    }
}
