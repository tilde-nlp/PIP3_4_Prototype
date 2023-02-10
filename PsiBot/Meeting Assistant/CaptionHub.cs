using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Psibot.Service
{
    public class CaptionHubState
    {
        //SignalR does not by default offer any way to tell if a group has any members
        public Dictionary<string, HashSet<string>> groupMembership = new Dictionary<string, HashSet<string>>();

        public bool hasUsers(string group)
        {
            return groupMembership.ContainsKey(group) && groupMembership[group].Any();
        }
    }

    public class CaptionHub : Hub<CaptionHub>
    {
        IServiceScopeFactory services;
        CaptionHubState state;
        

        public CaptionHub(IServiceScopeFactory _services, CaptionHubState _state)
        {
            services = _services;
            state = _state;
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            if (Context.Items.ContainsKey("group"))
                removeFromGroup(Context.ConnectionId, Context.Items["group"] as string);
            Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Signal R disconnect {Context.ConnectionId}");
            return base.OnDisconnectedAsync(exception);
        }

        public override async Task OnConnectedAsync()
        {
            
        }
        
        public async Task addToGroup(string connectionId, string group)
        {
            try
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Signal R add to group {connectionId} to {group}");
                if (!state.groupMembership.ContainsKey(group))
                    state.groupMembership.Add(group, new HashSet<string>());
                state.groupMembership[group].Add(connectionId);
                //Used to just be an await, but it threw a lot of indexoutofrange exceptions
                lock (Groups)
                {
                    Groups.AddToGroupAsync(connectionId, group).Wait();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Signal R error adding {connectionId} to group {group} : {e.Message} {e.InnerException?.Message ?? ""}");
                throw;
            }
        }

        public async Task removeFromGroup(string connectionId, string group)
        {
            try
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Signal R remove from group {connectionId} from {group}");
                if (state.groupMembership.ContainsKey(group) && state.groupMembership[group].Contains(connectionId))
                {
                    state.groupMembership[group].Remove(connectionId);
                }
                await Groups.RemoveFromGroupAsync(connectionId, group);
            }
            catch (Exception e)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Signal R error emoving {connectionId} from group {group} : {e.Message} {e.InnerException?.Message ?? ""}");
                throw;
            }
        }

        public async Task Subscribe(string thread, string lang = "")
        {
            Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Signal R subscribe {Context.ConnectionId} to thread {thread} with lang {lang}");
            if (Context.Items.ContainsKey("group"))
            {
                await removeFromGroup(Context.ConnectionId, Context.Items["group"] as string);
                Context.Items.Remove("group");
            }
            
            Context.Items.Add("group", thread + lang);
            await addToGroup(Context.ConnectionId, thread+lang);
        }
    }
}