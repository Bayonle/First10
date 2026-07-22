using Microsoft.AspNetCore.SignalR;

namespace First10.Api.Hubs;

/// <summary>
/// Push channel for the dispatcher console. Server → client only in M0:
/// "ticketChanged" (ticketId) tells the SPA to invalidate its TanStack Query caches.
/// </summary>
public class ConsoleHub : Hub;
