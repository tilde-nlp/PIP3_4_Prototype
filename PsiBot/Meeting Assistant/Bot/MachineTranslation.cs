using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using PsiBot.Service.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace PsiBot.Services.Bot
{
    public class MachineTranslation
    {
        private class MTSystem
        {
            public string ID { get; set; }
            public string SourceLanguage { get; set; }
            public string TargetLanguage { get; set; }
            public string Domain { get; set; }
        }

        private class MTLanguageDTO
        {
            public string Code { get; set; }
        }

        private class MTSystemDTO
        {
            public string ID { get; set; }
            public MTLanguageDTO SourceLanguage { get; set; }
            public MTLanguageDTO TargetLanguage { get; set; }
            public List<Dictionary<string, string>> Metadata { get; set; }
            public string Domain { get; set; }
        }

        private class MTTranslateRequest
        {
            public string text { get; set; }
            public string appID { get; set; }
            public string systemID { get; set; }
            public string options { get; set; }
        }

        private class MTArrayTranslateRequest
        {
            public List<string> textArray { get; set; }
            public string appID { get; set; }
            public string systemID { get; set; }
            public string options { get; set; }
        }

        private class MTArrayTranslateDTO
        {
            public string translation { get; set; }
        }

        private class GetSystemListResponse
        {
            public List<MTSystemDTO> System { get; set; }
        }        
        
        List<MTSystem> systems;
        BotConfiguration config;
        HttpClient httpClient;
        string appId;

        public MachineTranslation(IOptions<BotConfiguration> _config)
        {
            config = _config.Value;
            appId = config.Translation.AppId;
        }

        public async Task initialize()
        {
            try
            {
                httpClient = new HttpClient();
                httpClient.BaseAddress = new Uri(config.Translation.Url);
                httpClient.DefaultRequestHeaders.Add("client-id", config.Translation.ClientId);
                var res = await httpClient.GetAsync($"GetSystemList?appID={appId}");
                var resp = await res.Content.ReadAsAsync<GetSystemListResponse>();
                systems = resp.System.Where(x => x.Metadata.Where(y => y.ContainsValue("running")).ToList().Count() > 0).Select(x => new MTSystem { Domain = x.Domain, ID = x.ID, SourceLanguage = x.SourceLanguage.Code, TargetLanguage = x.TargetLanguage.Code }).ToList();
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Translation initialized " + String.Join(" ", systems.Select(x => x.SourceLanguage + "->" + x.TargetLanguage)));
            } catch (Exception e)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Error initializing translation: " + e.Message + e.InnerException?.Message);
            }
        }

        public string getSystemId(string from, string to)
        {
            return systems.FirstOrDefault(x => x.SourceLanguage == from && x.TargetLanguage == to)?.ID ?? null;
        }

        public List<string> mtLangs(string from)
        {            
            return systems.Where(x => x.SourceLanguage == from).Select(x => x.TargetLanguage).ToList();
        }

        public async Task<List<string>> translateArray(string system, List<string> text)
        {
            var req = new MTArrayTranslateRequest
            {
                appID = appId,
                systemID = system,
                textArray = text,
                options = ""
            };
            var resp = await httpClient.PostAsync("TranslateArrayEx", new StringContent(JsonConvert.SerializeObject(req), System.Text.Encoding.UTF8, "application/json"));
            if (resp.IsSuccessStatusCode)
            {
                var res = await resp.Content.ReadAsAsync<List<MTArrayTranslateDTO>>();
                return res.Select(x => x.translation).ToList();
            }
            return null;
        }

        public async Task<string> translate(string system, string text)
        {            
            var req = new MTTranslateRequest
            {
                appID = appId,
                systemID = system,
                text = text,
                options = ""
            };
            var start = DateTime.Now.Ticks;
            var resp = await httpClient.PostAsync("Translate", new StringContent(JsonConvert.SerializeObject(req), System.Text.Encoding.UTF8, "application/json"));
            var end = DateTime.Now.Ticks;
            Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Translate executed in {Math.Round((double)((end-start)/10000))}ms");
            if (resp.IsSuccessStatusCode)
            {
                return (await resp.Content.ReadAsStringAsync()).Trim('"');
            }
            return text;
        }
    }
}
