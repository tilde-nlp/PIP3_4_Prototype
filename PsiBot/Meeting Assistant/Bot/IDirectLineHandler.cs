
using Microsoft.Bot.Connector.DirectLine;
using System.Threading.Tasks;

namespace PsiBot.Services.Bot
{
    public abstract class IDirectLineHandler
    {
        public class DirectLineToken
        {
            public string conversationId { get; set; }
            public string token { get; set; }
            public int expires_in { get; set; }
        }

        private string secret { get; set; }
        private string userName { get; set; }
        private string userId { get; set; }
        private DirectLineClient directLineClient { get; set; }
        private Conversation conversation { get; set; }
        public delegate void MessageCallback(string message);
        public MessageCallback messageCallback;
        private string thread { get; set; }
        private string locale { get; set; }

        public abstract Task connect(string thread, string locale);        

        string watermark = null;
        public abstract Task send(string text);

        public abstract Task sendToChat(string text, string url);        
    }
}
