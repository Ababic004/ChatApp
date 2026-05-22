using Microsoft.AspNetCore.Identity;

namespace ChatApp.Models;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
}
