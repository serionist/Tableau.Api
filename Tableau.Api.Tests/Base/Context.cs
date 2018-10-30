using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Tableau.Api.Models;

namespace Tableau.Api.Tests
{
    public class Context
    {

        public ReadOnlyDictionary<string, object> RunSettings { get; set; }

        public T GetSetting<T>(string key)
        {
            if (RunSettings.ContainsKey(key))
                return (T) RunSettings[key];
            else return default(T);
        }

        public TableauApiClient Client { get; set; }

        public TableauSession _defaultSession { get; set; }
        public TableauSession AdminSession { get; set; }
        public TableauSession NonAdminAdminSession { get; set; }
        public TableauSession _nonAdminSessionImpersonate { get; set; }


        public string CreatedProjectName { get; set; }
        public string CreatedProjectId { get; set; }

        public string _a06_publishedDatasourceName { get; set; }
        public string _a06_publishedDatasourceId { get; set; }


    }
}
