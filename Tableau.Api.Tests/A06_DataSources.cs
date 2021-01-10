using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Enums;
using Tableau.Api.Tests.Base;

namespace Tableau.Api.Tests
{
    [TestClass]
    public class A06_DataSources : TestBase
    {
        [TestMethod]
        [PreRequisites(nameof(A02_Authentication.A02_3_LoginAdminUser))]
        public async Task A06_0_QueryDataSources()
        {
            var sources = await Context.Client.QueryDataSources(Context.AdminSession);

        }

        [TestMethod]
        [PreRequisites(nameof(A02_Authentication.A02_3_LoginAdminUser))]//, nameof(A04_Projects.A04_1_CreateProject))]
        public async Task A06_1_PublishTde()
        {
            var project = await Context.Client.QueryProjects(Context.AdminSession);
            Context._a06_publishedDatasourceName = Guid.NewGuid().ToString().Replace("-", "");
            var uploadSession = await Context.Client.InitiateFileUpload(Context.AdminSession);
            using (var resourceStrm = this.GetResource("sample.tde"))
            {
                while (resourceStrm.Position < resourceStrm.Length)
                    uploadSession = await Context.Client.AppendToFileUpload(Context.AdminSession,
                        uploadSession.uploadSessionId, resourceStrm);
            }
            var publishTde = await Context.Client.PublishDatasource(Context.AdminSession, uploadSession.uploadSessionId,
                Context._a06_publishedDatasourceName, DatasourceFileType.tde, project.projects[0].id);
            Context._a06_publishedDatasourceId = publishTde.id;

            Assert.IsNotNull(publishTde.id, "Published tde has no ID");
            var sources =
                await Context.Client.QueryDataSource(Context.AdminSession, Context._a06_publishedDatasourceId);
            Assert.IsNotNull(sources, "Published datasource not found");



        }

        [TestMethod]
        [PreRequisites(nameof(A02_Authentication.A02_3_LoginAdminUser), nameof(A06_1_PublishTde))]
        public async Task A06_2_QueryDataSourcesByFilter()
        {
            await Task.Delay(3000);
            var sources = await Context.Client.QueryDataSources(Context.AdminSession, 100, 1, null, $"name:eq:{Context._a06_publishedDatasourceName}");
            if (sources.datasources.Length != 1)
                Assert.Fail($"New datasource not found by query");

        }

    }
}

