using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using NPOI.XWPF.UserModel;
using PsiBot.Model.Models;
using PsiBot.Service.Settings;
using PsiBot.Services.Bot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PsiBot.Services
{
    public class TranscriptMessage
    {
        public string userid { get; set; }
        public long timestamp { get; set; }
        public string name { get; set; }
        public string text { get; set; }
        public string prefix { get; set; }
        public long id { get; set; }
        public string lang { get; set; }
        public Dictionary<string, string> translations { get; set; }
    }

    public class TranscriptMessageViewModel
    {
        public string name { get; set; }
        public string text { get; set; }
        public string prefix { get; set; }
        public long id { get; set; }
        public long timestamp { get; set; }
    }

    public class Transcript
    {
        public string thread { get; set; }
        public List<TranscriptMessage> messages { get; set; }
        public List<string> decisions { get; set; }
        public List<string> tasks { get; set; }
        public string nextMeeting { get; set; }
        public List<string> participants { get; set; }
        public string timeZone { get; set; }
        public GraphChat meetingInfo { get; set; }
        public string culture { get; set; }
        private IServiceScopeFactory serviceScopeFactory { get; set; }    

        public Transcript(IServiceScopeFactory _serviceScopeFactory, string _thread)
        {
            messages = new List<TranscriptMessage>();
            decisions = new List<string>();
            tasks = new List<string>();
            participants = new List<string>();
            serviceScopeFactory = _serviceScopeFactory;
            thread = _thread;
        }

        public void checkAndReset()
        {
            var timestamp = DateTime.Now.ToUniversalTime().Ticks / TimeSpan.TicksPerMillisecond - 62135596800000;
            timestamp -= 7200000;
            if (!messages.Any(x => x.timestamp > timestamp))
            {
                messages = new List<TranscriptMessage>();
                decisions = new List<string>();
                tasks = new List<string>();
                participants = new List<string>();
            }
        }

        public void appendMessage(TranscriptMessage message)
        {
            lock (message)
            {
                messages.Add(message);
            }
        }

        public void appendDecision(string decision)
        {
            decisions.Add(decision);
        }

        public void appendTask(string task)
        {
            tasks.Add(task);
        }

        public void addParticipant(string participant)
        {
            if (!string.IsNullOrWhiteSpace(participant) && !participants.Contains(participant))
                participants.Add(participant);
        }

        public void setNextMeeting(string parm)
        {
            nextMeeting = parm;
        }

        public List<TranscriptMessage> getMessages()
        {
            return messages;
        }

        public async Task<string> save()
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(culture);
            Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(culture);

            //Assumes it's currently the last paragraph, as in - just created
            void insertParagraph(XWPFDocument doc, XWPFParagraph par, int pos)
            {
                var idx = doc.GetPosOfParagraph(par);
                for (int i = idx; i > pos; i--)
                {
                    doc.SetParagraph(doc.Paragraphs[i - 1], i);
                }
                doc.SetParagraph(par, pos);
            }

            try
            {
                using (var serviceScope = serviceScopeFactory.CreateScope())
                {
                    var localizer = serviceScope.ServiceProvider.GetRequiredService<IStringLocalizer<Transcript>>();
                    var config = serviceScope.ServiceProvider.GetRequiredService<IOptions<BotConfiguration>>();
                    if (!Directory.Exists(config.Value.TranscriptFolder))
                    {
                        Directory.CreateDirectory(config.Value.TranscriptFolder);
                    }
                    string subject = meetingInfo?.topic ?? "Meeting";
                    foreach (char c in Path.GetInvalidFileNameChars())
                    {
                        subject = subject.Replace(c, '_');
                    }
                    var tzInfo = TimeZoneInfo.FindSystemTimeZoneById(timeZone ?? TimeZoneInfo.Local.Id);                    
                    var tempfilename = $"{config.Value.TranscriptFolder}/temp{subject}_{DateTime.Now.ToString("yyyyMMddhhmmss")}";
                    var ret = $"{subject}_{DateTime.Now.ToString("yyyyMMddhhmmss")}";
                    var filename = $"{config.Value.TranscriptFolder}/{ret}";
                    var template = @"Templates/MeetingNotesTemplate.docx";
                    using (var rs = File.OpenRead(template))
                    {
                        var doc = new XWPFDocument(rs);
                        using (var res = File.OpenWrite(tempfilename))
                        {
                            XWPFParagraph parTrans = null;
                            XWPFParagraph parDesc = null;

                            foreach (var para in doc.Paragraphs)
                            {
                                if (para.Text.Contains("{ph_transcription}"))
                                {
                                    para.RemoveRun(0);
                                    parTrans = para;
                                }
                                if (para.Text.Contains("{ph_decisions}"))
                                {
                                    para.RemoveRun(0);
                                    parDesc = para;
                                }
                                if (para.Text.Contains("{ph_participants}"))
                                {
                                    para.RemoveRun(0);
                                    foreach (var line in participants)
                                    {
                                        var run = para.CreateRun();
                                        run.SetText(line);
                                        run.AddCarriageReturn();
                                    }
                                }

                                if (para.Text.Contains("{ph_nextmeetingdate}"))
                                    para.ReplaceText("{ph_nextmeetingdate}", nextMeeting);
                                if (para.Text.Contains("{ph_listofparticipants}"))
                                    para.ReplaceText("{ph_listofparticipants}", localizer["LIST_OF_PARTICIPANTS"]);
                                if (para.Text.Contains("{ph_meetingnotes}"))
                                    para.ReplaceText("{ph_meetingnotes}", localizer["MEETING_NOTES"]);
                                if (para.Text.Contains("{ph_decisionstitle}"))
                                    para.ReplaceText("{ph_decisionstitle}", localizer["DECISIONS_TITLE"]);
                                if (para.Text.Contains("{ph_taskstitle}"))
                                    para.ReplaceText("{ph_taskstitle}", localizer["TASKS_TITLE"]);
                                if (para.Text.Contains("{ph_nextmeetingtitle}"))
                                    para.ReplaceText("{ph_nextmeetingtitle}", localizer["NEXT_MEETING_TITLE"]);
                                if (para.Text.Contains("{ph_transcriptiontitle}"))
                                    para.ReplaceText("{ph_transcriptiontitle}", localizer["TRANSCRIPTION_TITLE"]);
                                if (para.Text.Contains("{ph_date}"))
                                {
                                    para.ReplaceText("{ph_date}", DateTime.Today.ToString("dd.MM.yyyy"));
                                    var run = para.CreateRun();
                                    run.AddCarriageReturn();
                                    run = para.CreateRun();                                                                        
                                    run.SetText("UTC " + new DateTimeOffset(DateTime.UtcNow).ToOffset(tzInfo.GetUtcOffset(DateTime.UtcNow)).ToString("zzz"));

                                }
                            }

                            if (parTrans != null)
                            {
                                string name = null;
                                lock (messages)
                                {
                                    foreach (var line in messages)
                                    {
                                        XWPFRun run;
                                        if (name != line.name)
                                        {
                                            name = line.name;
                                            run = parTrans.CreateRun();
                                            run.SetText(line.name);
                                            run.IsBold = true;
                                            run = parTrans.CreateRun();
                                            var dt = DateTimeOffset.FromUnixTimeMilliseconds(line.timestamp).DateTime;
                                            var offset = tzInfo.GetUtcOffset(dt);                                            
                                            run.SetText($" {(dt + offset).ToString("HH:mm:ss")} ");
                                            run.SetColor("#808080");
                                        }
                                        run = parTrans.CreateRun();
                                        run.SetText(line.text);
                                        run.AddCarriageReturn();
                                    }
                                }
                            }

                            if (parDesc != null)
                            {
                                var num = parDesc.GetNumID();
                                var idx = doc.GetPosOfParagraph(parDesc);
                                doc.RemoveBodyElement(idx);

                                foreach (var line in decisions)
                                {
                                    var cc = doc.CreateParagraph();
                                    cc.SetNumID(num);
                                    var run = cc.CreateRun();
                                    run.SetText(line);
                                    insertParagraph(doc, cc, idx);
                                    idx++;
                                }
                            }

                            doc.Write(res);
                            await res.FlushAsync();
                        }
                    }
                    using (var rs = File.OpenRead(tempfilename))
                    {
                        var doc = new XWPFDocument(rs);
                        using (var res = File.OpenWrite(filename))
                        {
                            XWPFParagraph parTasks = null;
                            foreach (var para in doc.Paragraphs)
                            {
                                if (para.Text.Contains("{ph_tasks}"))
                                {
                                    para.RemoveRun(0);
                                    parTasks = para;
                                }
                            }

                            if (parTasks != null)
                            {
                                var num = parTasks.GetNumID();
                                var idx = doc.GetPosOfParagraph(parTasks);
                                doc.RemoveBodyElement(idx);

                                foreach (var line in tasks)
                                {
                                    var cc = doc.CreateParagraph();
                                    cc.SetNumID(num);
                                    var run = cc.CreateRun();
                                    run.SetText(line);
                                    insertParagraph(doc, cc, idx);
                                    idx++;
                                }
                            }
                            doc.Write(res);
                            await res.FlushAsync();
                        }
                    }
                    File.Delete(tempfilename);
                    return ret;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Error on transcript creation: {e.Message + e.StackTrace}");
                return null;
            }
        }
    }

    public class MeetingLogger
    {
        private MachineTranslation mt { get; set; }
        private Dictionary<string, Transcript> transcripts { get; set; }
        private IServiceScopeFactory serviceScopeFactory { get; set; }
        public MeetingLogger(MachineTranslation _mt, IServiceScopeFactory _services)
        {
            transcripts = new Dictionary<string, Transcript>();
            mt = _mt;
            serviceScopeFactory = _services;
        }

        public Transcript getTranscript(string thread)
        {
            if (thread == null)
                return null;
            if (!transcripts.ContainsKey(thread))
                transcripts.Add(thread, new Transcript( serviceScopeFactory, thread));
            return transcripts[thread];
        }

        public async Task<List<TranscriptMessageViewModel>> getTranscriptModel(string thread, string lang = "")
        {
            /*   if (!String.IsNullOrEmpty(lang))
               {
                   await ensureTranslation(thread, lang);
               }*/
            return getTranscript(thread).messages.Select(x => new TranscriptMessageViewModel
            {
                id = x.id,
                name = x.name,
                prefix = x.prefix,
                text = (string.IsNullOrEmpty(lang) || lang == x.lang || !x.translations.ContainsKey(lang)) ? x.text : x.translations[lang],
                timestamp = x.timestamp
            }).ToList();
        }




        public bool has(string thread)
        {
            return transcripts.ContainsKey(thread);
        }

        public async Task ensureTranslation(string thread, string lang)
        {
            var missing = getTranscript(thread).messages.Where(x => x.lang != lang && !x.translations.ContainsKey(lang));
            var grouping = missing.GroupBy(x => x.lang);
            foreach (var grp in grouping)
            {
                var system = mt.getSystemId(grp.Key, lang);
                if (system != null)
                {
                    var res = await mt.translateArray(system, grp.Select(x => x.text).ToList());
                    var list = grp.Select(x => x).ToList();

                    for (int i = 0; i < grp.Count(); i++)
                    {
                        list[i].translations.Add(lang, res != null ? res[i] : "<unk>");
                    }
                }
                else
                {
                    var list = grp.Select(x => x).ToList();
                    for (int i = 0; i < grp.Count(); i++)
                    {
                        list[i].translations.Add(lang, "<unk>");
                    }
                }
            }
        }
    }
}