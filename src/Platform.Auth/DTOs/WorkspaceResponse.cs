namespace Platform.Auth.DTOs;

public sealed record WorkspaceResponse(Guid Id, string Name, DateTimeOffset CreatedAt);
