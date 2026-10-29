using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Models;
using Tableau.Api.Tests.Base;

namespace Tableau.Api.Tests
{
    [TestClass]
    public class A01_Prepare: TestBase
    {
  
        [AssemblyInitialize]
        public static void Init(TestContext _context)
        {
            Context.RunSettings = new ReadOnlyDictionary<string, object>(_context.Properties);
            Console.WriteLine("Run settings read");




            Console.WriteLine("Creating client");
            Context.Client = new TableauApiClient(new Uri((string)_context.Properties["Url"]), (string)_context.Properties["Version"]);
        }

        [TestMethod]
        public void A01_0_VerifyPreparation()
        {
            if (Context.Client == null)
                Assert.Fail("Client is null");
        }
        //[TestMethod]
        //public async Task A01_SignIn()
        //{
        //    Session = await Client.SignIn((string) Context.Properties["User"], (string) Context.Properties["Password"],
        //        (string) Context.Properties["Site"], (string) Context.Properties["ImpersonateUserId"]);
        //}
    }
}
