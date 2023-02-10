using System;
using System.Collections.Generic;
using System.Text;

namespace PsiBot.Model.Models
{
    public class TokenResponse
    {
        public string token_type { get; set; }
        public int expires_in { get; set; }
        public int ext_expires_in { get; set; }
        public string access_token { get; set; }
    }

    public class TokenContent
    {
        public List<string> roles { get; set; }
    }

    public class TeamsUser
    {
        public string id { get; set; }
        public string aadObjectId { get; set; }
        public string name { get; set; }
        public string givenName { get; set; }
        public string surname { get; set; }
        public string email { get; set; }
        public string userPrincipalName { get; set; }
        public string tenantId { get; set; }
        public string userRole { get; set; }         
    }

    public class TeamsMeeting
    {
        public string role { get; set; }
        public bool inMeeting { get; set; }
    }

    public class TeamsConversation
    {
        public bool isGroup { get; set; }
        public string conversationType { get; set; }
        public string id { get; set; }
    }

    public class GetParticipantResponse
    {
        public TeamsUser user { get; set; }
        public TeamsMeeting meeting { get; set; }
        public TeamsConversation conversation { get; set; }
    }

    public class OnlineMeetingUser
    {
        public string id { get; set; }
    }

    public class OnlineMeetingIdentity
    {
        public OnlineMeetingUser user { get; set; }
    }

    public class OnlineMeetingOrganizer
    {
        public OnlineMeetingIdentity identity { get; set; }
    }

    public class OnlineMeetingParticipants
    {
        public OnlineMeetingOrganizer organizer { get; set; }
    }

    public class GetOnlineMeetingResponse
    {        
        public OnlineMeetingParticipants participants { get; set; }
        public string subject { get; set; }
    }

    public class GraphChat
    {
        public string topic { get; set; }
        public GraphOnlineMeetingInfo onlineMeetingInfo { get; set; }
    }

    public class GraphOnlineMeetingInfo
    {
        public string joinWebUrl { get; set; }
        public GraphOrganizer organizer { get; set; }
    }

    public class GraphOrganizer
    {
        public string id { get; set; }
    }
}
