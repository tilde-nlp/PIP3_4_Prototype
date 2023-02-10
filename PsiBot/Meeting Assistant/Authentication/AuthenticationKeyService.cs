using LiteDB;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using PsiBot.Service.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PsiBot.Services.Authentication
{
    public class AuthenticationData
    {
        public string Id { get; set; }
        public string TenantId { get; set; }
        public string Key { get; set; }

        public AuthenticationData() { }
        public AuthenticationData(string tenant, string key)
        {
            Id = Guid.NewGuid().ToString();
            TenantId = tenant;
            Key = key;
        }

    }

    public class AuthenticationKeyService
    {
        const string DB_NAME = "AuthenticationData.db";
        readonly string _dbDirectory = "App_Data";
        string dbFile { get { return Path.Combine(_dbDirectory, DB_NAME); } }

        readonly public List<string> _keys;
        readonly public string _mailTo;


        public AuthenticationKeyService(IOptions<AuthenticationConfiguration> authKeys, IHostingEnvironment env, IOptions<BotConfiguration> config)
        {
            _keys = authKeys.Value.Keys;
            _mailTo = authKeys.Value.MailTo;
            _dbDirectory = Path.Combine(config.Value.TranscriptFolder, _dbDirectory);
            if (!Directory.Exists(_dbDirectory))
            {
                Directory.CreateDirectory(_dbDirectory);
            }
        }


        public bool ValidateKey(string tenant, string key)
        {
            bool result = _keys.Any(k => k.Equals(key));
            if (result)
            {
                using (var db = new LiteDatabase(dbFile))
                {
                    var col = db.GetCollection<AuthenticationData>("AuthenticationData");
                    // doesn't exists -> add record
                    if (col.Query()
                        .Where(x => x.TenantId == tenant)
                        .Count() == 0)
                    {
                        var auth = new AuthenticationData(tenant, key);
                        col.Insert(auth);
                    }

                }
            }
            return result;
        }

        public bool IsAuthenticated(string tenant)
        {
            using (var db = new LiteDatabase(dbFile))
            {
                var col = db.GetCollection<AuthenticationData>("AuthenticationData");
                var exists = col.Query()
                    .Where(x => x.TenantId == tenant)
                    .Count() > 0;
                return exists;
            }
        }

        public bool SignOut(string tenant)
        {
            using (var db = new LiteDatabase(dbFile))
            {
                var col = db.GetCollection<AuthenticationData>("AuthenticationData");
                col.DeleteAll();
            }
            return true;
        }
    }
}
