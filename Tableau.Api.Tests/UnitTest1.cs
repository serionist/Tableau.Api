using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tableau.Api.Tests
{
    [TestClass]
    public class UnitTest1
    {
        private static TableauApiClient client;
        private static TestContext context;
        [ClassInitialize]
        public static void Init(TestContext _context)
        {
            context = _context;
            client = new TableauApiClient(new Uri((string)_context.Properties["Url"]), (string)_context.Properties["Version"]);
        }

        [TestMethod]
        public async Task A01_SignIn()
        {
            var session = await client.SignIn((string) context.Properties["User"], (string) context.Properties["Password"],
                (string) context.Properties["Site"], (string) context.Properties["ImpersonateUserId"]);
        }
    }
}
