using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PsiBot.Service.Settings
{
    public class ASRConfiguration
    {
        public List<ASRLangConfiguration> Langs { get; set; }        
    }

    public class ASRLangConfiguration
    {
        public string Service { get; set; }
        public string Id { get; set; }
        public string Url { get; set; }
        public string AppId { get; set; }
        public string AppSecret { get; set; }
        public string LangId { get; set; }
        public string BaseLang { get; set; }
        public int SilenceTimeout { get; set; }
    }
}