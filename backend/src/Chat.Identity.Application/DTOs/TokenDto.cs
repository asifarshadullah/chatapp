namespace Chat.Identity.Application.DTOs;

public record TokenDto(string AccessToken, DateTime ExpiresAt, Guid UserId);
