using PsiBot.Model.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PsiBot.Services.Logic
{
    public class GraphLogic
    {
        public static async Task<GetOnlineMeetingResponse> getMeetingInfo(string token, string thread, string organizerId)
        {
            var infoClient = new HttpClient();
            infoClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
            var threadDec = Encoding.UTF8.GetString(Convert.FromBase64String(thread));
            var regex = new Regex("#(.*)#");
            var match = regex.Match(threadDec);
            var threadId = match.Groups[1].Value;
            var graphThread = Convert.ToBase64String(Encoding.UTF8.GetBytes($"1*{organizerId}*0**{threadId}"));
            var url = $"https://graph.microsoft.com/v1.0/me/onlinemeetings/{graphThread}";
            var res = await(infoClient.GetAsync(url));
            var content = await res.Content.ReadAsAsync<GetOnlineMeetingResponse>();            
            return content;
        }

        public static async Task<GraphChat> getChatInfo(string token, string thread)
        {
            var infoClient = new HttpClient();
            infoClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
            var threadDec = Encoding.UTF8.GetString(Convert.FromBase64String(thread));
            var regex = new Regex("#(.*)#");
            var match = regex.Match(threadDec);
            var threadId = match.Groups[1].Value;            
            var url = $"https://graph.microsoft.com/v1.0/me/chats/{threadId}";
            var res = await (infoClient.GetAsync(url));
            var content = await res.Content.ReadAsAsync<GraphChat>();
            return content;
        }
    }
}
