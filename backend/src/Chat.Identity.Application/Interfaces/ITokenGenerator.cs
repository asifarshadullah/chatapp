using Chat.Identity.Application.DTOs;
using Chat.Identity.Domain.Entities;

namespace Chat.Identity.Application.Interfaces;

/// <summary>
/// Contract for producing a signed token from an AppUser.
/// Application layer defines the need; Infrastructure provides the JWT implementation.
/// </summary>
public interface ITokenGenerator
{
    TokenDto Generate(AppUser user);
}
