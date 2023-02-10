using System.Collections.Generic;

namespace PsiBot.Model.Models
{
    public class OptionsBody
    {
        public string Tid { get; set; }
        public string Oid { get; set; }
        public string Thread { get; set; }
        public string Token { get; set; }
    }

    public class OptionsViewModel
    {
        public bool isOrganizer { get; set; }
        public List<string> asrLangs { get; set; } = new List<string>();
        public List<string> mtLangs { get; set; } = new List<string>();
        public string activeAsrLang { get; set; }
        public Dictionary<string, string> langMap = new Dictionary<string, string>();

        public string Lang(string lang)
        {
            return langMap[lang];
            switch (lang)
            {
                case "":
                    {
                        return ("Do not translate");
                    }
                case "lv":
                    {
                        return ("Latvian");
                    }
                case "ru":
                    {
                        return ("Russian");

                    }
                case "et":
                    {
                        return ("Estonian");
                    }
                case "lt":
                    {
                        return ("Lithuanian");
                    }
                case "uk":
                case "en":
                    {
                        return ("English");
                    }
                default:
                    {
                        return (lang);
                    }
            }
        }
    }
}
