using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using ChatApp.Models;
using ChatApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly UserManager<AppUser> _userManager;
    private readonly AppDbContext _db;

    // connectionId -> userId
    private static readonly Dictionary<string, string> _connections = new();
    // userId -> connectionId
    private static readonly Dictionary<string, string> _userConnections = new();

    public ChatHub(UserManager<AppUser> userManager, AppDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    public override async Task OnConnectedAsync()
    {
        var user = await _userManager.GetUserAsync(Context.User!);
        if (user != null)
        {
            lock (_connections)
            {
                _connections[Context.ConnectionId] = user.Id;
                _userConnections[user.Id] = Context.ConnectionId;
            }

            // Regrupise sve grupe ciji je user clan 
            var groups = await _db.ChatGroups.ToListAsync();
            foreach (var group in groups)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, group.Id);
            }

            await Clients.All.SendAsync("UserOnline", user.Id, user.DisplayName);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        string? userId = null;
        lock (_connections)
        {
            if (_connections.TryGetValue(Context.ConnectionId, out userId))
            {
                _connections.Remove(Context.ConnectionId);
                _userConnections.Remove(userId);
            }
        }

        if (userId != null)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
                await Clients.All.SendAsync("UserOffline", userId, user.DisplayName);
        }

        await base.OnDisconnectedAsync(exception);
    }

    // Salje privatnu poruku specificnom useru
    public async Task SendPrivateMessage(string recipientUserId, string message)
    {
        var sender = await _userManager.GetUserAsync(Context.User!);
        if (sender == null || string.IsNullOrWhiteSpace(message)) return;

        // Pravim privatni kljuc za oba userID
        var roomId = GetPrivateRoomId(sender.Id, recipientUserId);

        await Clients.Group(roomId).SendAsync("ReceivePrivateMessage", new
        {
            senderId = sender.Id,
            senderName = sender.DisplayName,
            recipientId = recipientUserId,
            message = message.Trim(),
            timestamp = DateTime.UtcNow.ToString("HH:mm")
        });
    }

    // Join za privatne sobe
    public async Task JoinPrivateRoom(string otherUserId)
    {
        var me = await _userManager.GetUserAsync(Context.User!);
        if (me == null) return;

        var roomId = GetPrivateRoomId(me.Id, otherUserId);
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

        // Dodavanje drugog korisnika ako je online
        lock (_connections)
        {
            if (_userConnections.TryGetValue(otherUserId, out var otherConn))
            {
                Groups.AddToGroupAsync(otherConn, roomId);
            }
        }
    }

    // Slanje poruke u grupu
    public async Task SendGroupMessage(string groupId, string message)
    {
        var sender = await _userManager.GetUserAsync(Context.User!);
        if (sender == null || string.IsNullOrWhiteSpace(message)) return;

        var group = await _db.ChatGroups.FindAsync(groupId);
        if (group == null) return;

        await Clients.Group(groupId).SendAsync("ReceiveGroupMessage", new
        {
            senderId = sender.Id,
            senderName = sender.DisplayName,
            groupId = groupId,
            groupName = group.Name,
            message = message.Trim(),
            timestamp = DateTime.UtcNow.ToString("HH:mm")
        });
    }

    // Pravljenje nove grupe i dodavanje membera
    public async Task CreateGroup(string groupName, List<string> memberIds)
    {
        var creator = await _userManager.GetUserAsync(Context.User!);
        if (creator == null) return;

        var group = new ChatGroup { Name = groupName };
        _db.ChatGroups.Add(group);
        await _db.SaveChangesAsync();

        // Dodaje sve membere i kreatora u signalR grupu
        var allMemberIds = memberIds.Union(new[] { creator.Id }).Distinct().ToList();

        foreach (var userId in allMemberIds)
        {
            lock (_connections)
            {
                if (_userConnections.TryGetValue(userId, out var connId))
                {
                    Groups.AddToGroupAsync(connId, group.Id);
                }
            }
        }

        // Obavestava sve online korisnike
        foreach (var userId in allMemberIds)
        {
            lock (_connections)
            {
                if (_userConnections.TryGetValue(userId, out var connId))
                {
                    Clients.Client(connId).SendAsync("GroupCreated", new
                    {
                        groupId = group.Id,
                        groupName = group.Name
                    });
                }
            }
        }
    }

    public static HashSet<string> GetOnlineUserIds()
    {
        lock (new object())
        {
            return new HashSet<string>(_userConnections.Keys);
        }
    }

    private static string GetPrivateRoomId(string userId1, string userId2)
    {
        var sorted = new[] { userId1, userId2 }.OrderBy(x => x).ToArray();
        return $"private_{sorted[0]}_{sorted[1]}";
    }
}
