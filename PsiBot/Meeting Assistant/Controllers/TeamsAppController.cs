// <copyright file="DemoController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// </copyright>

using PsiBot.Model.Constants;
using PsiBot.Service.Settings;
using PsiBot.Services.Bot;
using PsiBot.Services.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Communications.Common.Telemetry;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using System.Linq;
using PsiBot.Model.Models;
using Microsoft.Bot.Builder.Teams;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;
using System.IO;
using PsiBot.Services.ViewModel;
using Newtonsoft.Json;
using Microsoft.Identity.Client;
using System.IdentityModel.Tokens.Jwt;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Localization;
using System.Web;
using PsiBot.Services.Authentication;

namespace PsiBot.Services.Controllers
{

    public class TeamsAppController : Controller
    {
        MeetingLogger logger { get; set; }
        BotConfiguration config { get; set; }
        ASRConfiguration asrConfig { get; set; }
        BotService botService { get; set; }
        MachineTranslation translation { get; set; }
        private readonly IStringLocalizer<TeamsAppController> localizer;
        readonly AuthenticationKeyService authenticationKeyService;

        public TeamsAppController(MeetingLogger _logger, IOptions<BotConfiguration> _config, IBotService _botService, IOptions<ASRConfiguration> _asrconfig, MachineTranslation _translation, IStringLocalizer<TeamsAppController> _localizer, AuthenticationKeyService _authenticationKeyService)
        {
            config = _config.Value;
            logger = _logger;
            botService = _botService as BotService; //should either throw out the interface or put all necessary methods in it
            asrConfig = _asrconfig.Value;
            translation = _translation;
            localizer = _localizer;
            authenticationKeyService = _authenticationKeyService;
        }

        [Route("/TeamsApp/Configure")]
        public ActionResult Configure()
        {
            ViewBag.AuthKeyLength = authenticationKeyService._keys.Select(x => x.Length).Min();
            ViewBag.MailTo = authenticationKeyService._mailTo;
            return View("Configure", new ConfigureViewModel { AppId = config.AadAppId });
        }

        [Route("/TeamsApp/About")]
        public ActionResult About()
        {
            return View("About");
        }

        [Route("/TeamsApp/SidePanel")]
        public ActionResult SidePanel()
        {
            return Redirect(config.BrowserRedirectUrl);
        }

        [Route("/TeamsApp/SidePanel/{oid}/{thread}/{tid}/{langId}/{mtlang}/{theme?}")]
        public ActionResult SidePanel(string oid, string thread, string tid, string langId, string mtlang, string theme = "default")
        {
            var result = new SidePanelDTO
            {
                LangId = (langId.Equals("---") ? "" : langId),
                TranslateId = (mtlang.Equals("---") ? "" : mtlang),
                Theme = theme,
                AppId = config.AadAppId
            };
            Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} SidePanel load in {thread} by {oid}");
            return View("SidePanel", result);
        }

        [Route("/TeamsApp/mtlangs/{threadId}")]
        public ActionResult AvailableTranslationLang(string threadId)
        {
            var activeAsrLang = "";
            var kvp = botService.CallHandlers.FirstOrDefault(x => x.Value.thread == threadId);
            if (kvp.Value != null)
                activeAsrLang = kvp.Value.getAsrLang();
            else
            {
                activeAsrLang = botService.GetOfflineBotLang(threadId);
                if (string.IsNullOrEmpty(activeAsrLang))
                {
                    activeAsrLang = asrConfig.Langs.First().Id;
                }
            }
            translation.mtLangs(activeAsrLang);
            var model = new OptionsViewModel();
            model.mtLangs = new List<string> { "" };
            var asrLangConfig = asrConfig.Langs.First(x => x.Id == activeAsrLang);
            model.mtLangs.AddRange(translation.mtLangs(string.IsNullOrEmpty(asrLangConfig.BaseLang) ? asrLangConfig.Id : asrLangConfig.BaseLang));

            model.langMap.Add("", localizer["DO_NOT_TRANSLATE"]);
            foreach (var lang in model.asrLangs.Union(model.mtLangs).Distinct())
            {
                if (lang != "")
                    model.langMap.Add(lang, localizer[lang].Value);
            }

            return PartialView("LangSelection", model);
        }

        [Route("/TeamsApp/Transcript")]
        public async Task<ActionResult> Transcript(string thread, string lang = "")
        {
            var transcript = await logger.getTranscriptModel(thread, lang);
            return PartialView("Transcript", transcript);
        }

        [Route("/TeamsApp/FullTranscript")]
        public async Task<ActionResult> FullTranscript(string thread, string lang = "")
        {
            if (!logger.has(thread))
                return Ok("");
            var transcript = await logger.getTranscriptModel(thread, lang);
            string res = string.Join("\n", transcript.Select(x => x.name + ": " + x.text));
            return Ok(res);
        }

        [Route("/TeamsApp/isorganizer/{oid}/{thread}/{tid}")]
        public async Task<IActionResult> IsOrganizer(string oid, string thread, string tid, [FromBody] string token)
        {
            try
            {
                return Ok(await GetIsOrganizer(oid, thread, tid, token));
            }
            catch (Exception e)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} IsOrganizer error: {e.Message} {e.InnerException?.Message ?? ""}");
                throw e;
            }
        }


        private async Task<bool> GetIsOrganizer(string oid, string thread, string tid, string token)
        {
            return true; //Everyone can invite the bot now; if actual role is needed, separate the invite logic
            //oid needs to be organizer id for this to work anyway
            try
            {
                var infoClient = new HttpClient();
                infoClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                var threadDec = Encoding.UTF8.GetString(Convert.FromBase64String(thread));
                var regex = new Regex("#(.*)#");
                var match = regex.Match(threadDec);
                var threadId = match.Groups[1].Value;
                var graphThread = Convert.ToBase64String(Encoding.UTF8.GetBytes($"1*{oid}*0**{threadId}"));
                var url = $"https://graph.microsoft.com/v1.0/me/onlinemeetings/{graphThread}";
                var res = await (infoClient.GetAsync(url));
                var content = await res.Content.ReadAsAsync<GetOnlineMeetingResponse>();
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} IsOrganizer  Check: {thread} {oid} {content?.participants?.organizer?.identity?.user?.id == oid}");
                return content?.participants?.organizer?.identity?.user?.id == oid;
                /*   var user = await GetMeetingContext(new OptionsBody() { Oid = oid, Thread = thread, Tid = tid });
                   return Ok(user?.meeting?.role == "Organizer");*/
            }
            catch (Exception e)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} IsOrganizer error: {e.Message} {e.InnerException?.Message ?? ""}");
                throw e;
            }
        }

        [Route("/TeamsApp/MeetingNotes")]
        public async Task<ActionResult> MeetingNotes(string thread, string instance = "")
        {
            if (instance != "")
            {
                if (System.IO.File.Exists($"{config.TranscriptFolder}/{thread}_{instance}"))
                {
                    var res = System.IO.File.ReadAllBytes($"{config.TranscriptFolder}/{thread}_{instance}");
                    return File(res, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{thread}_{instance}.docx");
                }
                else return BadRequest();
            }
            else
            {
                var files = new DirectoryInfo(config.TranscriptFolder).GetFiles($"{thread}*");
                if (files.Any())
                {
                    var file = files.Last();
                    var res = System.IO.File.ReadAllBytes(file.FullName);
                    return File(res, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{file.Name}.docx");
                }
                else
                    return BadRequest();
            }
        }

        static List<string> ppcols = new List<string> { "#6b5b95", "#d64161", "#ff7b25", "#86af49", "#878f99", "#405d27" };
        int hash(string parm, int digits)
        {
            var m = Math.Pow(10, digits + 1) - 1;
            var phi = Math.Pow(10, digits) / 2 - 1;
            var n = 0;
            for (var i = 0; i < parm.Length; i++)
            {
                n = (int)((n + phi * (int)Char.GetNumericValue(parm[i])) % m);
            }
            return n;
        }

        [Route("/TeamsApp/botjoined")]
        public ActionResult BotJoined(string thread)
        {
            return Json(botService.CallHandlers.Any(x => x.Value.thread == thread));
        }

        [Route("/TeamsApp/css")]
        public ContentResult CSS(string theme = "default")
        {
            if (theme == "default")
                return Content(System.IO.File.ReadAllText("CSS/Light.css"), "text/css", Encoding.UTF8);
            else
                return Content(System.IO.File.ReadAllText("CSS/Dark.css"), "text/css", Encoding.UTF8);
        }

        [Route("/TeamsApp/progress")]
        public ContentResult Progress()
        {
            try
            {
                return Content(System.IO.File.ReadAllText("CSS/progress.svg"), "image/svg+xml", Encoding.UTF8);
            }
            catch (Exception e)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Progress error: {e.Message} {e.InnerException?.Message ?? ""}");
                return null;
            }
        }        

        [Route("/TeamsApp/setasrlang")]
        public IActionResult setAsrLang(string thread, string lang)
        {
            var kvp = botService.CallHandlers.FirstOrDefault(x => x.Value.thread == thread);
            if (kvp.Value != null)
            {
                kvp.Value.setAsrLang(lang);
            }
            else
            {
                botService.SetOfflineBotLang(thread, lang);
            }
            return Ok();
        }

        [HttpPost]
        [Route("/TeamsApp/options")]
        public async Task<ActionResult> Options([FromBody] OptionsBody parms)
        {

            try
            {
                var model = new OptionsViewModel();
                model.isOrganizer = await GetIsOrganizer(parms.Oid, parms.Thread, parms.Tid, parms.Token);
                var activeAsrLang = "";
                var kvp = botService.CallHandlers.FirstOrDefault(x => x.Value.thread == parms.Thread);
                if (kvp.Value != null)
                    activeAsrLang = kvp.Value.getAsrLang();
                else
                {
                    activeAsrLang = botService.GetOfflineBotLang(parms.Thread);
                    if (string.IsNullOrEmpty(activeAsrLang))
                    {
                        activeAsrLang = asrConfig.Langs.First().Id;
                    }
                }

                var asrLangConfig = asrConfig.Langs.First(x => x.Id == activeAsrLang);
                model.mtLangs = new List<string> { "" };
                model.mtLangs.AddRange(translation.mtLangs(string.IsNullOrEmpty(asrLangConfig.BaseLang) ? asrLangConfig.Id : asrLangConfig.BaseLang));

                //  model.isOrganizer = content?.meeting?.role == "Organizer";
                model.activeAsrLang = activeAsrLang;
                model.asrLangs = asrConfig.Langs.Select(x => x.Id).ToList();
                model.langMap.Add("", localizer["DO_NOT_TRANSLATE"]);
                foreach (var lang in model.asrLangs.Union(model.mtLangs).Distinct())
                {
                    if (lang != "")
                        model.langMap.Add(lang, localizer[lang].Value);
                }
                return PartialView("Options", model);
            }
            catch (Exception e)
            {
                throw;
            }
        }

        [HttpGet]
        [Route("/TeamsApp/RemoveBot")]
        public async Task<IActionResult> RemoveBot(string thread)
        {
            try
            {
                var filename = await logger.getTranscript(thread).save();
                //TODO: get the actual handler
                if (botService.CallHandlers.Values.Count > 0)
                {
                    Task.Run(() => botService.CallHandlers.Values.First(x => x.thread == thread).BotMediaStream.directLine.sendToChat("", $"https://{config.ServiceCname}/TeamsApp/MeetingNotes?thread={HttpUtility.UrlEncode(filename)}"));
                    Task.Run(() => botService.CallHandlers.Values.First(x => x.thread == thread).BotMediaStream.directLine.send("Meeting-Ended remove bot"));
                    Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Leaving thread {thread} due to removebot call");
                }
                await botService.EndCallByThreadAsync(thread).ConfigureAwait(false);
                return Ok();
            }
            catch (Exception e)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Exception leaving thread {thread} {e.Message + e.StackTrace}");
                return StatusCode(500, e.ToString());
            }
        }

        [Route("/TeamsApp/Auth")]
        public async Task<ActionResult> Auth()
        {
            return View("Auth");
        }

        [Route("/TeamsApp/AdminPermissions")]
        public async Task<ActionResult> AdminPermissions(string tenant, string state, bool admin_consent, string scope, string error, string error_subcode)
        {
            if (admin_consent)
                return View("CloseThisPage", new CloseThisPageModel { success = admin_consent, bigText = "Admin consent granted!", smallText = "For security purposes please close this tab!" });
            else
                return View("CloseThisPage", new CloseThisPageModel { success = admin_consent, bigText = "Failed to grant admin consent", smallText = error + " - " + error_subcode });
        }

        [Route("/TeamsApp/AuthCallBack")]
        public async Task<ActionResult> AuthCallBack([FromBody] string body)
        {
            try
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} AuthCallBack called");
                var auth = Request.Headers["Authorization"];
                var token = auth.First().Split(' ')[1];
                string[] scopes = new string[] { "OnlineMeetings.Read", "Chat.ReadBasic" };

                var msal = ConfidentialClientApplicationBuilder.Create(config.AadAppId).WithClientSecret(config.AadAppSecret).Build();
                var usr = new UserAssertion(token, "urn:ietf:params:oauth:grant-type:jwt-bearer");
                var res = await msal.AcquireTokenOnBehalfOf(scopes, usr).ExecuteAsync();
                return Json(new { access_token = res?.AccessToken });
            }
            catch (Exception e)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Exception in authcallback {e.Message + e.StackTrace}");
                if (e.Message.StartsWith("AADSTS65001"))
                {
                    Response.StatusCode = 403;
                    return Json(new { error = "consent_required" });
                }
                else
                    return Json(new { error = e.Message });
            }
        }

        [Route("/TeamsApp/AdminApprovalLink")]
        public async Task<IActionResult> getAdminApprovalLink(string tenant, string baseurl)
        {
            string link = $"https://login.microsoftonline.com/{tenant}/v2.0/adminconsent" +
            $"?client_id={config.AadAppId}" +
            "&scope=https://graph.microsoft.com/.default" +
            $"&redirect_uri={baseurl}/teamsapp/adminpermissions" +
            "&state=12345";
            return PartialView("_adminApprovalLink", link);
        }

        [Route("/TeamsApp/AdminApprovalStatus/{tenant}")]
        public async Task<bool> getAdminApprovalStatus(string tenant)
        {
            try
            {
                var authClient = new HttpClient();
                var formContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("client_id", config.AadAppId),
                    new KeyValuePair<string, string>("client_secret", config.AadAppSecret),
                    new KeyValuePair<string, string>("scope", "https://graph.microsoft.com/.default"),
                });
                var authResp = await authClient.PostAsync($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token", formContent);
                var tokenObj = await authResp.Content.ReadAsAsync<TokenResponse>();

                var tcont = new JwtSecurityTokenHandler().ReadJwtToken(tokenObj.access_token);
                if (!tcont.Payload.ContainsKey("roles"))
                    return false;
                var jtokenContent = tcont.Payload["roles"].ToString();
                var tokenContent = JsonConvert.DeserializeObject<JArray>(jtokenContent);
                return tokenContent.Any(x => x.Value<string>() == "Calls.JoinGroupCall.All");
            }
            catch (Exception e)
            {
                return false;
            }
        }

        [HttpPost("/TeamsApp/signin/{tenant}/{key}")]
        public async Task<IActionResult> SignIn(string tenant, string key)
        {
            try
            {
                var result = authenticationKeyService.ValidateKey(tenant, key);
                if (result)
                {
                    return Ok();
                }
                else
                {
                    return BadRequest();
                }
            }
            catch (Exception ex)
            {
                return BadRequest(ex);
            }
        }

        [HttpPost("/TeamsApp/isauthenticated/{tenant}")]
        public async Task<IActionResult> IsAuthenticated(string tenant)
        {
            try
            {
                var result = authenticationKeyService.IsAuthenticated(tenant);
                if (result)
                {
                    return Ok();
                }
                else
                {
                    return BadRequest();
                }
            }
            catch (Exception ex)
            {
                return BadRequest(ex);
            }
        }

        [HttpGet("/TeamsApp/signout/{tenant}")]
        public async Task<IActionResult> SignOut(string tenant)
        {
            authenticationKeyService.SignOut(tenant);
            return Ok();
        }
    }
}