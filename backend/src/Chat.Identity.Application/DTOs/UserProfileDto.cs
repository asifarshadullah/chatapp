namespace Chat.Identity.Application.DTOs;

public record UserProfileDto(Guid UserId, string Email, string DisplayName, string UserType);
