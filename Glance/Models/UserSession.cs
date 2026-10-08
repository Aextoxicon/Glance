namespace Glance.Models;

public record UserSession(
    string PublicId,
    string Username,
    string Token
);
