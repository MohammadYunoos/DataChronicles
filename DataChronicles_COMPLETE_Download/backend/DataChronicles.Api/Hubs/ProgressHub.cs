using Microsoft.AspNetCore.SignalR;

namespace DataChronicles.Api.Hubs;

/// <summary>SignalR hub that streams categorization progress (0-100%) to the UI.</summary>
public class ProgressHub : Hub { }
