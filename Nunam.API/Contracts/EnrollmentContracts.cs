namespace Nunam.API.Contracts
{
    /// <param name="MachineId">The machine to reconnect; null adds a new one.</param>
    /// <param name="Name">Name for a new machine; the hostname when blank.</param>
    public record CreateEnrollmentRequest(Guid? MachineId, string? Name);

    /// <param name="Packages">The package kinds the server can hand out (<c>msi</c>, <c>deb</c>, <c>rpm</c>).</param>
    public record EnrollmentCreated(Guid Id, string Code, DateTimeOffset ExpiresAt, List<string> Packages);

    public record EnrollmentState(Guid Id, DateTimeOffset ExpiresAt, DateTimeOffset? UsedAt, Guid? MachineId);
}
