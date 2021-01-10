using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Tests.Base;

namespace Tableau.Api.Tests
{
    [TestClass]
    public class A04_Projects:TestBase
    {

        [TestMethod]
        [PreRequisites(nameof(A02_Authentication.A02_3_LoginAdminUser))]
        public async Task A04_1_CreateProject()
        {
            Context.CreatedProjectName = Guid.NewGuid().ToString("N");
            Context.CreatedProjectId = (await Context.Client.CreateProject(Context.AdminSession, Context.CreatedProjectName, "anyad")).id;

        }
    }
}
