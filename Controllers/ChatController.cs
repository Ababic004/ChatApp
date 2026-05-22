using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using ChatApp.Models;
using ChatApp.Data;
using ChatApp.Hubs;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Controllers;

[Authorize]
public class ChatController : Controller
{
    private readonly UserManager<AppUser> _userManager;
    private readonly AppDbContext _db;

    public ChatController(UserManager<AppUser> userManager, AppDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var currentUser = await _userManager.GetUserAsync(User);
        var allUsers = await _userManager.Users
            .Where(u => u.Id != currentUser!.Id)
            .ToListAsync();
        var groups = await _db.ChatGroups.ToListAsync();
        var onlineIds = ChatHub.GetOnlineUserIds();

        ViewBag.CurrentUser = currentUser;
        ViewBag.OnlineIds = onlineIds;
        ViewBag.Groups = groups;
        return View(allUsers);
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        var currentUser = await _userManager.GetUserAsync(User);
        var onlineIds = ChatHub.GetOnlineUserIds();
        var users = await _userManager.Users
            .Where(u => u.Id != currentUser!.Id)
            .Select(u => new { u.Id, u.DisplayName, u.UserName, isOnline = onlineIds.Contains(u.Id) })
            .ToListAsync();
        return Json(users);
    }

    [HttpGet]
    public async Task<IActionResult> GetGroups()
    {
        var groups = await _db.ChatGroups
            .Select(g => new { g.Id, g.Name })
            .ToListAsync();
        return Json(groups);
    }
}
